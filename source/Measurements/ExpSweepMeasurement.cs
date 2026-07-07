using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using NAudio.Wave;
using Resonalyze.Dsp;
using static System.Math;

namespace Resonalyze
{
    /// <summary>
    /// Coordinates sweep playback, recording, and FFT-based deconvolution.
    /// </summary>
    public sealed class ExpSweepMeasurement : IDisposable
    {
        private readonly object stateSync = new();
        private readonly object levelSync = new();
        private SoundRecorder? soundRecorder;
        private SoundRecorder? loopbackRecorder;
        private AudioChannelLevel[] latestMicrophoneLevels = Array.Empty<AudioChannelLevel>();
        private AudioChannelLevel[] latestLoopbackLevels = Array.Empty<AudioChannelLevel>();
        private CancellationTokenSource? cancellationTokenSource;
        private Task<bool>? measurementTask;
        private TaskCompletionSource<bool>? averageConfirmation;
        private volatile bool inProgress;
        private volatile bool waitingForAverageConfirmation;
        private bool disposed;

        public event Action<bool>? Completed;
        public event Action? ImpulseResponseChanged;
        public event Action<SweepAverageProgress>? AverageProgressChanged;
        internal event Action<InputLevelMeterSnapshot>? LevelsAvailable;

        public ExponentialSineSweep? Sweep { get; private set; }
        public Complex[]? SweepDeconvolutionImpulseResponse { get; private set; }
        public int SweepDeconvolutionPeakIndex { get; private set; }
        public Complex[]? TransferImpulseResponse { get; private set; }
        public int TransferPeakIndex { get; private set; }
        public double[]? TransferCoherence { get; private set; }
        public float[]? MicrophoneRecordedSamples { get; private set; }
        public float[]? LoopbackRecordedSamples { get; private set; }
        public SweepMeasurementMode MeasurementMode { get; private set; } =
            SweepMeasurementMode.SweepDeconvolution;
        public bool HasImpulseResponse => SweepDeconvolutionImpulseResponse != null;
        public bool InProgress => inProgress;
        public int SampleRate { get; private set; }
        public int Octaves { get; private set; }
        public int Bits { get; private set; }
        public PlaybackChannel PlaybackChannel { get; private set; }
        public AudioBackend AudioBackend { get; private set; } = AudioBackend.Wave;
        public int OutputDeviceNumber { get; private set; } = -1;
        public int InputDeviceNumber { get; private set; } = -1;
        public string? AsioDriverName { get; private set; }
        public int WaveInputChannelOffset { get; private set; }
        public int? WaveLoopbackInputChannelOffset { get; private set; }
        // When set (and different from the microphone device), the Wave loopback is captured
        // from this separate input device instead of from another channel of the microphone
        // device. The two devices run on independent clocks, so the loopback is only
        // best-effort aligned (see DualDeviceCapture).
        public int? WaveLoopbackDeviceNumber { get; private set; }
        public int AsioInputChannelOffset { get; private set; }
        public int? AsioLoopbackInputChannelOffset { get; private set; }
        public int AsioOutputChannelOffset { get; private set; }
        public int AverageRunCount { get; private set; } = 1;
        public int AcceptedAverageRunCount { get; private set; } = 1;
        public bool ConfirmEachAverageRun { get; private set; }
        public Exception? LastError { get; private set; }
        public int RecordedSamples => soundRecorder?.ReadSamples ?? 0;
        public bool WaitingForAverageConfirmation => waitingForAverageConfirmation;
        internal InputLevelMeterSnapshot CurrentLevels { get; private set; } =
            InputLevelMeterSnapshot.Empty;

        public void Init(
            int octaves,
            int sampleRate,
            int bits,
            double requestedDuration,
            PlaybackChannel playbackChannel,
            int outputDeviceNumber = -1,
            int inputDeviceNumber = -1,
            AudioBackend audioBackend = AudioBackend.Wave,
            string? asioDriverName = null,
            int asioInputChannelOffset = 0,
            int asioOutputChannelOffset = 0,
            int waveInputChannelOffset = 0,
            int? waveLoopbackInputChannelOffset = null,
            int? asioLoopbackInputChannelOffset = null,
            int? waveLoopbackDeviceNumber = null,
            int averageRunCount = 1,
            bool confirmEachAverageRun = false)
        {
            ThrowIfDisposed();
            if (InProgress)
            {
                throw new InvalidOperationException("Cannot reinitialize an active measurement.");
            }

            PlaybackChannel = Enum.IsDefined(playbackChannel)
                ? playbackChannel
                : PlaybackChannel.Mono;
            SampleRate = sampleRate;
            Bits = bits;
            Octaves = octaves;
            OutputDeviceNumber = outputDeviceNumber;
            InputDeviceNumber = inputDeviceNumber;
            AudioBackend = audioBackend;
            AsioDriverName = asioDriverName;
            WaveInputChannelOffset = Math.Clamp(waveInputChannelOffset, 0, 1);
            WaveLoopbackInputChannelOffset = NormalizeOptionalWaveChannel(
                waveLoopbackInputChannelOffset);
            WaveLoopbackDeviceNumber = waveLoopbackDeviceNumber;
            AsioInputChannelOffset = asioInputChannelOffset;
            AsioLoopbackInputChannelOffset = asioLoopbackInputChannelOffset;
            AsioOutputChannelOffset = asioOutputChannelOffset;
            SweepDeconvolutionImpulseResponse = null;
            SweepDeconvolutionPeakIndex = 0;
            TransferImpulseResponse = null;
            TransferPeakIndex = 0;
            TransferCoherence = null;
            MicrophoneRecordedSamples = null;
            LoopbackRecordedSamples = null;
            MeasurementMode = SweepMeasurementMode.SweepDeconvolution;
            AverageRunCount = Math.Clamp(averageRunCount, 1, 64);
            AcceptedAverageRunCount = 0;
            ConfirmEachAverageRun = confirmEachAverageRun;
            LastError = null;
            CurrentLevels = InputLevelMeterSnapshot.Empty;

            Sweep?.Dispose();
            Sweep = new ExponentialSineSweep();
            Sweep.FillData(octaves, requestedDuration, bits, sampleRate);

            DisposeRecorders();
            lock (levelSync)
            {
                latestMicrophoneLevels = Array.Empty<AudioChannelLevel>();
                latestLoopbackLevels = Array.Empty<AudioChannelLevel>();
            }

            bool separateLoopbackDevice = UsesSeparateWaveLoopbackDevice;
            soundRecorder = new SoundRecorder();
            int recorderChannelCount = audioBackend switch
            {
                AudioBackend.Wave when separateLoopbackDevice => WaveInputChannelOffset + 1,
                AudioBackend.Wave => GetRequiredWaveInputChannelCount(),
                _ => 1
            };
            soundRecorder.Init(
                sampleRate,
                bits,
                recorderChannelCount,
                inputDeviceNumber,
                expectedSamples: Sweep.SweepSamples + sampleRate * 2);
            soundRecorder.LevelsAvailable += separateLoopbackDevice
                ? HandleMicrophoneOnlyLevels
                : HandleWaveLevelsAvailable;

            if (separateLoopbackDevice)
            {
                loopbackRecorder = new SoundRecorder();
                loopbackRecorder.Init(
                    sampleRate,
                    bits,
                    WaveLoopbackInputChannelOffset!.Value + 1,
                    WaveLoopbackDeviceNumber!.Value,
                    expectedSamples: Sweep.SweepSamples + sampleRate * 2);
                loopbackRecorder.LevelsAvailable += HandleLoopbackOnlyLevels;
            }
        }

        private bool UsesSeparateWaveLoopbackDevice =>
            AudioBackend == AudioBackend.Wave &&
            WaveLoopbackInputChannelOffset.HasValue &&
            WaveLoopbackDeviceNumber.HasValue &&
            WaveLoopbackDeviceNumber.Value != InputDeviceNumber;

        private void DisposeRecorders()
        {
            if (soundRecorder != null)
            {
                soundRecorder.LevelsAvailable -= HandleWaveLevelsAvailable;
                soundRecorder.LevelsAvailable -= HandleMicrophoneOnlyLevels;
                soundRecorder.Dispose();
                soundRecorder = null;
            }
            if (loopbackRecorder != null)
            {
                loopbackRecorder.LevelsAvailable -= HandleLoopbackOnlyLevels;
                loopbackRecorder.Dispose();
                loopbackRecorder = null;
            }
        }

        public Task<bool> RunAsync()
        {
            ThrowIfDisposed();
            lock (stateSync)
            {
                if (measurementTask is { IsCompleted: false })
                {
                    return measurementTask;
                }
                if (Sweep == null || soundRecorder == null)
                {
                    throw new InvalidOperationException("Measurement is not initialized.");
                }

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = new CancellationTokenSource();
                inProgress = true;
                SweepDeconvolutionImpulseResponse = null;
                SweepDeconvolutionPeakIndex = 0;
                TransferImpulseResponse = null;
                TransferPeakIndex = 0;
                TransferCoherence = null;
                MicrophoneRecordedSamples = null;
                LoopbackRecordedSamples = null;
                MeasurementMode = SweepMeasurementMode.SweepDeconvolution;
                AcceptedAverageRunCount = 0;
                LastError = null;
                CurrentLevels = InputLevelMeterSnapshot.Empty;
                measurementTask = RunCoreAsync(cancellationTokenSource.Token);
                return measurementTask;
            }
        }

        public void ContinueAverageRun()
        {
            lock (stateSync)
            {
                averageConfirmation?.TrySetResult(true);
            }
        }

        public async Task AbortAsync()
        {
            Task<bool>? runningTask;
            lock (stateSync)
            {
                cancellationTokenSource?.Cancel();
                runningTask = measurementTask;
            }

            if (runningTask != null)
            {
                try
                {
                    await runningTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        public double HarmonicIROffset(double harmonic)
        {
            if (Sweep == null)
            {
                return 0;
            }
            return Sweep.SweepSamples * Log(harmonic) / Log(Pow(2, Octaves));
        }

        public void RestoreImpulseResponse(
            int octaves,
            int sampleRate,
            int bits,
            double sweepDurationSeconds,
            PlaybackChannel playChannel,
            Complex[] sweepDeconvolutionImpulseResponse,
            int sweepDeconvolutionPeakIndex,
            SweepMeasurementMode measurementMode = SweepMeasurementMode.SweepDeconvolution,
            Complex[]? transferImpulseResponse = null,
            int? transferPeakIndex = null,
            double[]? transferCoherence = null,
            int averageRunCount = 1,
            int acceptedAverageRunCount = 1)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(sweepDeconvolutionImpulseResponse);
            if (transferImpulseResponse == null &&
                measurementMode == SweepMeasurementMode.LoopbackTransfer)
            {
                throw new ArgumentException(
                    "Transfer impulse response is required for loopback transfer measurements.",
                    nameof(transferImpulseResponse));
            }
            if (InProgress)
            {
                throw new InvalidOperationException(
                    "Cannot load an impulse response while a measurement is running.");
            }
            if (sweepDeconvolutionImpulseResponse.Length == 0)
            {
                throw new ArgumentException(
                    "Sweep deconvolution impulse response cannot be empty.",
                    nameof(sweepDeconvolutionImpulseResponse));
            }
            if ((uint)sweepDeconvolutionPeakIndex >=
                (uint)sweepDeconvolutionImpulseResponse.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(sweepDeconvolutionPeakIndex));
            }
            if (transferImpulseResponse is { Length: 0 })
            {
                throw new ArgumentException(
                    "Transfer impulse response cannot be empty.",
                    nameof(transferImpulseResponse));
            }
            if (transferImpulseResponse != null &&
                (!transferPeakIndex.HasValue ||
                    (uint)transferPeakIndex.Value >= (uint)transferImpulseResponse.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(transferPeakIndex));
            }

            Init(
                octaves,
                sampleRate,
                bits,
                sweepDurationSeconds,
                playChannel,
                OutputDeviceNumber,
                InputDeviceNumber,
                AudioBackend,
                AsioDriverName,
                AsioInputChannelOffset,
                AsioOutputChannelOffset,
                WaveInputChannelOffset,
                WaveLoopbackInputChannelOffset,
                AsioLoopbackInputChannelOffset,
                WaveLoopbackDeviceNumber,
                AverageRunCount,
                ConfirmEachAverageRun);
            SweepDeconvolutionImpulseResponse = sweepDeconvolutionImpulseResponse.ToArray();
            SweepDeconvolutionPeakIndex = sweepDeconvolutionPeakIndex;
            TransferImpulseResponse = transferImpulseResponse?.ToArray();
            TransferCoherence = transferCoherence?.ToArray();
            MicrophoneRecordedSamples = null;
            LoopbackRecordedSamples = null;
            MeasurementMode = measurementMode;
            TransferPeakIndex = transferImpulseResponse != null
                ? transferPeakIndex!.Value
                : 0;
            AverageRunCount = Math.Clamp(averageRunCount, 1, 64);
            AcceptedAverageRunCount = Math.Clamp(
                acceptedAverageRunCount,
                1,
                AverageRunCount);
            LastError = null;
            ImpulseResponseChanged?.Invoke();
        }

        internal void RestoreLevelSnapshot(InputLevelMeterSnapshot snapshot)
        {
            ThrowIfDisposed();
            CurrentLevels = snapshot;
            RaiseLevels(snapshot);
        }

        private void HandleWaveLevelsAvailable(AudioChannelLevel[] channels)
        {
            RaiseLevels(MapWaveLevels(channels));
        }

        // With a separate loopback device, the microphone and loopback levels arrive from two
        // independent recorders. Keep the latest of each and raise a combined snapshot so both
        // meters update live.
        private void HandleMicrophoneOnlyLevels(AudioChannelLevel[] channels)
        {
            lock (levelSync)
            {
                latestMicrophoneLevels = channels;
            }
            RaiseCombinedDualDeviceLevels();
        }

        private void HandleLoopbackOnlyLevels(AudioChannelLevel[] channels)
        {
            lock (levelSync)
            {
                latestLoopbackLevels = channels;
            }
            RaiseCombinedDualDeviceLevels();
        }

        private void RaiseCombinedDualDeviceLevels()
        {
            AudioChannelLevel[] microphoneChannels;
            AudioChannelLevel[] loopbackChannels;
            lock (levelSync)
            {
                microphoneChannels = latestMicrophoneLevels;
                loopbackChannels = latestLoopbackLevels;
            }

            InputLevelMeterEntry microphone = CreateEntry(
                TryGetLevel(microphoneChannels, WaveInputChannelOffset),
                fullScaleReference: false);
            InputLevelMeterEntry loopback = CreateEntry(
                WaveLoopbackInputChannelOffset is int loopbackChannel
                    ? TryGetLevel(loopbackChannels, loopbackChannel)
                    : null,
                fullScaleReference: true);
            RaiseLevels(new InputLevelMeterSnapshot(microphone, loopback));
        }

        private async Task<bool> RunCoreAsync(CancellationToken cancellationToken)
        {
            ExponentialSineSweep sweep = Sweep!;
            SoundRecorder recorder = soundRecorder!;
            bool success = false;

            try
            {
                var accumulator = new SweepAverageAccumulator();
                int requestedRuns = AverageRunCount;
                for (int run = 1; run <= requestedRuns; run++)
                {
                    AverageProgressChanged?.Invoke(new SweepAverageProgress(
                        run,
                        requestedRuns,
                        accumulator.AcceptedRuns,
                        SweepAverageProgressState.Running));
                    CapturedSweepSamples captured = AudioBackend == AudioBackend.Asio
                        ? await CaptureAsioAsync(sweep, cancellationToken).ConfigureAwait(false)
                        : await CaptureWaveAsync(sweep, recorder, cancellationToken).ConfigureAwait(false);
                    SweepRunAnalysis analysis = AnalyzeCapturedRun(captured, sweep);
                    accumulator.Add(analysis);

                    if (ConfirmEachAverageRun && run < requestedRuns)
                    {
                        await WaitForAverageConfirmationAsync(
                            run,
                            requestedRuns,
                            accumulator.AcceptedRuns,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                ApplyAverageResult(accumulator.BuildResult());
                success = true;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                LastError = exception;
            }
            finally
            {
                try
                {
                    await recorder.StopRecordingAsync().ConfigureAwait(false);
                    SoundRecorder? loopback = loopbackRecorder;
                    if (loopback != null)
                    {
                        await loopback.StopRecordingAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    LastError ??= exception;
                    success = false;
                }

                lock (stateSync)
                {
                    waitingForAverageConfirmation = false;
                    averageConfirmation = null;
                    inProgress = false;
                }
                try
                {
                    Completed?.Invoke(success);
                }
                catch
                {
                    // UI subscribers must not break measurement cleanup.
                }
            }

            return success;
        }

        private async Task WaitForAverageConfirmationAsync(
            int completedRun,
            int requestedRuns,
            int acceptedRuns,
            CancellationToken cancellationToken)
        {
            Task waitTask;
            lock (stateSync)
            {
                averageConfirmation = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                waitingForAverageConfirmation = true;
                waitTask = averageConfirmation.Task;
            }

            AverageProgressChanged?.Invoke(new SweepAverageProgress(
                completedRun,
                requestedRuns,
                acceptedRuns,
                SweepAverageProgressState.WaitingForConfirmation));

            try
            {
                await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (stateSync)
                {
                    waitingForAverageConfirmation = false;
                    averageConfirmation = null;
                }
            }
        }

        private async Task<CapturedSweepSamples> CaptureWaveAsync(
            ExponentialSineSweep sweep,
            SoundRecorder recorder,
            CancellationToken cancellationToken)
        {
            if (UsesSeparateWaveLoopbackDevice)
            {
                return await CaptureWaveDualDeviceAsync(sweep, recorder, loopbackRecorder!, cancellationToken)
                    .ConfigureAwait(false);
            }

            using var player = new WaveOutEvent
            {
                DeviceNumber = OutputDeviceNumber
            };
            await recorder.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
            int recordingStart = recorder.ReadSamples;

            RawSourceWaveStream stream = sweep.GetStream(PlaybackChannel);
            stream.Position = 0;
            player.Init(stream);
            await PlayToEndAsync(player, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;

            int requiredSamples = recordingStart + sweep.SweepSamples + SampleRate;
            await recorder.WaitForSamplesAsync(requiredSamples, cancellationToken).ConfigureAwait(false);
            await recorder.StopRecordingAsync().ConfigureAwait(false);

            int microphoneIndex = WaveInputChannelOffset;
            int? loopbackIndex = WaveLoopbackInputChannelOffset;
            return new CapturedSweepSamples(
                recorder.GetSamplesSnapshot(),
                microphoneIndex,
                loopbackIndex,
                ValidateSharedDeviceStereo: true);
        }

        // Microphone and loopback come from two independent Wave devices. Both recorders are
        // started before playback, then aligned at their first sample and trimmed to a shared
        // length (best-effort; see DualDeviceCapture). The merged array is [microphone, loopback].
        private async Task<CapturedSweepSamples> CaptureWaveDualDeviceAsync(
            ExponentialSineSweep sweep,
            SoundRecorder microphone,
            SoundRecorder loopback,
            CancellationToken cancellationToken)
        {
            using var player = new WaveOutEvent
            {
                DeviceNumber = OutputDeviceNumber
            };
            await microphone.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
            await loopback.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
            int microphoneStart = microphone.ReadSamples;
            int loopbackStart = loopback.ReadSamples;

            RawSourceWaveStream stream = sweep.GetStream(PlaybackChannel);
            stream.Position = 0;
            player.Init(stream);
            await PlayToEndAsync(player, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;

            int margin = sweep.SweepSamples + SampleRate;
            await microphone
                .WaitForSamplesAsync(microphoneStart + margin, cancellationToken)
                .ConfigureAwait(false);
            await loopback
                .WaitForSamplesAsync(loopbackStart + margin, cancellationToken)
                .ConfigureAwait(false);
            await microphone.StopRecordingAsync().ConfigureAwait(false);
            await loopback.StopRecordingAsync().ConfigureAwait(false);

            float[][] merged = DualDeviceCapture.MergeMicrophoneAndLoopback(
                microphone.GetSamplesSnapshot(),
                WaveInputChannelOffset,
                loopback.GetSamplesSnapshot(),
                WaveLoopbackInputChannelOffset!.Value,
                microphoneStart,
                loopbackStart);
            return new CapturedSweepSamples(
                merged,
                MicrophoneIndex: 0,
                LoopbackIndex: 1,
                ValidateSharedDeviceStereo: false);
        }

        private async Task<CapturedSweepSamples> CaptureAsioAsync(
            ExponentialSineSweep sweep,
            CancellationToken cancellationToken)
        {
            int firstInputOffset = GetAsioCaptureFirstInputOffset();
            int inputChannelCount = GetRequiredAsioInputChannelCount(firstInputOffset);

            using var session = new AsioFullDuplexSession(
                AsioDriverName ?? string.Empty,
                firstInputOffset,
                AsioOutputChannelOffset,
                inputChannelCount);
            session.LevelsAvailable += channels => RaiseLevels(
                MapLevelsByIndex(
                    channels,
                    AsioInputChannelOffset - firstInputOffset,
                    AsioLoopbackInputChannelOffset.HasValue
                        ? AsioLoopbackInputChannelOffset.Value - firstInputOffset
                        : null));
            using FloatArrayWaveStream stream = FloatArrayWaveStream.FromMonoSamples(
                sweep.SweepData,
                SampleRate,
                PlaybackChannel);

            await session.StartAsync(
                stream,
                SampleRate,
                autoStop: false,
                cancellationToken,
                expectedTotalSamples: sweep.SweepSamples + SampleRate * 2)
                .ConfigureAwait(false);
            int requiredSamples = session.ReadSamples + sweep.SweepSamples + SampleRate;
            await session.WaitForSamplesAsync(requiredSamples, cancellationToken)
                .ConfigureAwait(false);
            await session.StopAsync().ConfigureAwait(false);

            int microphoneIndex = AsioInputChannelOffset - firstInputOffset;
            int? loopbackIndex = AsioLoopbackInputChannelOffset.HasValue
                ? AsioLoopbackInputChannelOffset.Value - firstInputOffset
                : null;
            return new CapturedSweepSamples(
                session.GetSamplesSnapshot(),
                microphoneIndex,
                loopbackIndex,
                ValidateSharedDeviceStereo: false);
        }

        private static async Task PlayToEndAsync(WaveOutEvent player, CancellationToken cancellationToken)
        {
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void PlaybackStopped(object? sender, StoppedEventArgs args)
            {
                if (args.Exception != null)
                {
                    stopped.TrySetException(args.Exception);
                }
                else
                {
                    stopped.TrySetResult(true);
                }
            }

            player.PlaybackStopped += PlaybackStopped;
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() =>
                {
                    player.Stop();
                    stopped.TrySetCanceled(cancellationToken);
                });

            try
            {
                player.Play();
                await stopped.Task.ConfigureAwait(false);
            }
            finally
            {
                player.PlaybackStopped -= PlaybackStopped;
            }
        }

        private SweepRunAnalysis AnalyzeCapturedRun(
            CapturedSweepSamples captured,
            ExponentialSineSweep sweep,
            bool raiseIntermediateLevels = true)
        {
            float[][] sampleChannels = captured.SampleChannels;
            if (captured.ValidateSharedDeviceStereo &&
                sampleChannels.Length > 1 &&
                (WaveInputChannelOffset > 0 ||
                    WaveLoopbackInputChannelOffset.HasValue))
            {
                RecordedChannelValidator.EnsureDifferentSignals(
                    sampleChannels,
                    0,
                    1,
                    "Wave measurement");
            }

            float[] recorded = (uint)captured.MicrophoneIndex < (uint)sampleChannels.Length
                ? sampleChannels[captured.MicrophoneIndex]
                : Array.Empty<float>();
            if (recorded.Length == 0)
            {
                throw new InvalidOperationException("No audio samples were recorded.");
            }

            SweepDeconvolutionResult sweepResult = SweepAnalysis.DeconvolveWithInverseFilter(
                recorded,
                sweep.InverseFilter,
                2.0 / sweep.InverseFilter.Length);
            Complex[] sweepImpulseResponse = Array.ConvertAll(
                sweepResult.ImpulseResponse,
                x => new Complex(x, 0.0));

            TransferFunctionFrame? transferFrame = null;
            if (TryBuildTransferFrame(
                sampleChannels,
                captured.MicrophoneIndex,
                captured.LoopbackIndex,
                out TransferFunctionFrame frame))
            {
                transferFrame = frame;
            }

            InputLevelMeterSnapshot finalLevels = CreateFinalLevelSnapshot(
                sampleChannels,
                captured.MicrophoneIndex,
                captured.LoopbackIndex);
            if (raiseIntermediateLevels)
            {
                CurrentLevels = finalLevels;
                RaiseLevels(finalLevels);
            }

            return new SweepRunAnalysis(
                sweepImpulseResponse,
                sweepResult.PeakIndex,
                transferFrame,
                sampleChannels,
                captured.MicrophoneIndex,
                captured.LoopbackIndex,
                finalLevels);
        }

        private void ApplyAverageResult(SweepAverageResult result)
        {
            SweepDeconvolutionImpulseResponse = result.SweepImpulseResponse;
            SweepDeconvolutionPeakIndex = result.SweepPeakIndex;
            TransferImpulseResponse = result.TransferImpulseResponse;
            TransferPeakIndex = result.TransferPeakIndex;
            TransferCoherence = result.TransferCoherence;
            MicrophoneRecordedSamples = result.MicrophoneRecordedSamples;
            LoopbackRecordedSamples = result.LoopbackRecordedSamples;
            MeasurementMode = result.TransferImpulseResponse != null
                ? SweepMeasurementMode.LoopbackTransfer
                : SweepMeasurementMode.SweepDeconvolution;
            AcceptedAverageRunCount = result.AcceptedRunCount;
            CurrentLevels = result.Levels;
            RaiseLevels(result.Levels);
            ImpulseResponseChanged?.Invoke();
        }

        private bool TryBuildTransferFrame(
            float[][] sampleChannels,
            int microphoneIndex,
            int? loopbackIndex,
            out TransferFunctionFrame frame)
        {
            frame = default;
            if (!loopbackIndex.HasValue ||
                (uint)microphoneIndex >= (uint)sampleChannels.Length ||
                (uint)loopbackIndex.Value >= (uint)sampleChannels.Length)
            {
                return false;
            }

            RecordedChannelValidator.EnsureDifferentSignals(
                sampleChannels,
                microphoneIndex,
                loopbackIndex.Value,
                    $"{AudioBackend} loopback transfer");

            double[] loopback = Array.ConvertAll(
                sampleChannels[loopbackIndex.Value],
                sample => (double)sample);
            double[] microphone = Array.ConvertAll(
                sampleChannels[microphoneIndex],
                sample => (double)sample);
            frame = new TransferFunctionFrame(loopback, microphone);
            return true;
        }

        private InputLevelMeterSnapshot CreateFinalLevelSnapshot(
            float[][] sampleChannels,
            int microphoneIndex,
            int? loopbackIndex)
        {
            AudioChannelLevel[] measuredLevels = MeasureRecordedChannels(sampleChannels);
            return MapLevelsByIndex(measuredLevels, microphoneIndex, loopbackIndex);
        }

        private InputLevelMeterSnapshot MapWaveLevels(AudioChannelLevel[] channels)
        {
            return MapLevelsByIndex(
                channels,
                WaveInputChannelOffset,
                WaveLoopbackInputChannelOffset);
        }

        private static InputLevelMeterSnapshot MapLevelsByIndex(
            AudioChannelLevel[] channels,
            int microphoneIndex,
            int? loopbackIndex)
        {
            InputLevelMeterEntry microphone = CreateEntry(
                TryGetLevel(channels, microphoneIndex),
                fullScaleReference: false);
            InputLevelMeterEntry loopback = CreateEntry(
                loopbackIndex.HasValue
                    ? TryGetLevel(channels, loopbackIndex.Value)
                    : null,
                fullScaleReference: true);
            return new InputLevelMeterSnapshot(
                microphone,
                loopback);
        }

        private void RaiseLevels(InputLevelMeterSnapshot snapshot)
        {
            LevelsAvailable?.Invoke(snapshot);
        }

        private static AudioChannelLevel? TryGetLevel(
            AudioChannelLevel[] channels,
            int channelIndex)
        {
            return (uint)channelIndex < (uint)channels.Length
                ? channels[channelIndex]
                : null;
        }

        private static InputLevelMeterEntry CreateEntry(
            AudioChannelLevel? level,
            bool fullScaleReference)
        {
            if (level == null)
            {
                return InputLevelMeterEntry.Unavailable;
            }

            AudioChannelLevel value = level.Value;
            return new InputLevelMeterEntry(
                true,
                value.PeakDbFs,
                value.RmsDbFs,
                !fullScaleReference && value.FullScale,
                fullScaleReference && value.FullScale);
        }

        private static AudioChannelLevel[] MeasureRecordedChannels(float[][] sampleChannels)
        {
            var levels = new AudioChannelLevel[sampleChannels.Length];
            for (int channel = 0; channel < sampleChannels.Length; channel++)
            {
                levels[channel] = MeasureRecordedChannel(sampleChannels[channel]);
            }

            return levels;
        }

        private static AudioChannelLevel MeasureRecordedChannel(float[] samples) =>
            AudioLevelMetering.MeasureSamples(samples);

        private int GetRequiredWaveInputChannelCount() =>
            CaptureChannelLayout.RequiredWaveInputChannelCount(
                WaveInputChannelOffset,
                WaveLoopbackInputChannelOffset);

        private int GetAsioCaptureFirstInputOffset() =>
            CaptureChannelLayout.AsioFirstInputOffset(
                AsioInputChannelOffset,
                AsioLoopbackInputChannelOffset);

        private int GetRequiredAsioInputChannelCount(int firstInputOffset) =>
            CaptureChannelLayout.AsioInputChannelCount(
                AsioInputChannelOffset,
                AsioLoopbackInputChannelOffset);

        private static int FindPeakIndex(IReadOnlyList<double> samples)
        {
            double maxMagnitude = 0;
            int peakIndex = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                double magnitude = Math.Abs(samples[i]);
                if (magnitude > maxMagnitude)
                {
                    maxMagnitude = magnitude;
                    peakIndex = i;
                }
            }

            return peakIndex;
        }

        private static int FindPeakIndex(IReadOnlyList<Complex> samples)
        {
            double maxMagnitude = 0;
            int peakIndex = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                double magnitude = samples[i].Magnitude;
                if (magnitude > maxMagnitude)
                {
                    maxMagnitude = magnitude;
                    peakIndex = i;
                }
            }

            return peakIndex;
        }

        private sealed record CapturedSweepSamples(
            float[][] SampleChannels,
            int MicrophoneIndex,
            int? LoopbackIndex,
            bool ValidateSharedDeviceStereo);

        private sealed record SweepRunAnalysis(
            Complex[] SweepImpulseResponse,
            int SweepPeakIndex,
            TransferFunctionFrame? TransferFrame,
            float[][] SampleChannels,
            int MicrophoneIndex,
            int? LoopbackIndex,
            InputLevelMeterSnapshot Levels);

        private sealed record SweepAverageResult(
            Complex[] SweepImpulseResponse,
            int SweepPeakIndex,
            Complex[]? TransferImpulseResponse,
            int TransferPeakIndex,
            double[]? TransferCoherence,
            float[]? MicrophoneRecordedSamples,
            float[]? LoopbackRecordedSamples,
            InputLevelMeterSnapshot Levels,
            int AcceptedRunCount);

        private sealed class SweepAverageAccumulator
        {
            private readonly List<TransferFunctionFrame> transferFrames = new();
            private readonly ChannelLevelAccumulator microphoneLevels = new(fullScaleReference: false);
            private readonly ChannelLevelAccumulator loopbackLevels = new(fullScaleReference: true);
            private Complex[]? sweepSum;
            private int referencePeakIndex;
            private float[]? lastMicrophoneSamples;
            private float[]? lastLoopbackSamples;

            public int AcceptedRuns { get; private set; }

            public void Add(SweepRunAnalysis run)
            {
                ArgumentNullException.ThrowIfNull(run);
                if (sweepSum == null)
                {
                    sweepSum = new Complex[run.SweepImpulseResponse.Length];
                    referencePeakIndex = run.SweepPeakIndex;
                }

                int offset = run.SweepPeakIndex - referencePeakIndex;
                for (int destination = 0; destination < sweepSum.Length; destination++)
                {
                    int source = destination + offset;
                    if ((uint)source < (uint)run.SweepImpulseResponse.Length)
                    {
                        sweepSum[destination] += run.SweepImpulseResponse[source];
                    }
                }

                if (run.TransferFrame is TransferFunctionFrame frame)
                {
                    transferFrames.Add(frame);
                }

                if ((uint)run.MicrophoneIndex < (uint)run.SampleChannels.Length)
                {
                    float[] samples = run.SampleChannels[run.MicrophoneIndex];
                    microphoneLevels.Add(samples);
                    lastMicrophoneSamples = samples.ToArray();
                }
                if (run.LoopbackIndex is int loopbackIndex &&
                    (uint)loopbackIndex < (uint)run.SampleChannels.Length)
                {
                    float[] samples = run.SampleChannels[loopbackIndex];
                    loopbackLevels.Add(samples);
                    lastLoopbackSamples = samples.ToArray();
                }

                AcceptedRuns++;
            }

            public SweepAverageResult BuildResult()
            {
                if (sweepSum == null || AcceptedRuns == 0)
                {
                    throw new InvalidOperationException("No sweep runs were accepted.");
                }

                var sweepAverage = new Complex[sweepSum.Length];
                double scale = 1.0 / AcceptedRuns;
                for (int i = 0; i < sweepAverage.Length; i++)
                {
                    sweepAverage[i] = sweepSum[i] * scale;
                }

                Complex[]? transferImpulseResponse = null;
                int transferPeakIndex = 0;
                double[]? transferCoherence = null;
                if (transferFrames.Count == AcceptedRuns)
                {
                    TransferEstimateResult transfer = TransferFunction.ComputeAveragedRelativeIr(
                        transferFrames);
                    transferImpulseResponse = Array.ConvertAll(
                        transfer.ImpulseResponse,
                        sample => new Complex(sample, 0.0));
                    transferPeakIndex = transfer.PeakIndex;
                    transferCoherence = transfer.Coherence;
                }

                return new SweepAverageResult(
                    sweepAverage,
                    FindPeakIndex(sweepAverage),
                    transferImpulseResponse,
                    transferPeakIndex,
                    transferCoherence,
                    lastMicrophoneSamples,
                    lastLoopbackSamples,
                    new InputLevelMeterSnapshot(
                        microphoneLevels.ToEntry(),
                        loopbackLevels.ToEntry()),
                    AcceptedRuns);
            }
        }

        private sealed class ChannelLevelAccumulator
        {
            private readonly bool fullScaleReference;
            private double peak;
            private double sumSquares;
            private long sampleCount;

            public ChannelLevelAccumulator(bool fullScaleReference)
            {
                this.fullScaleReference = fullScaleReference;
            }

            public void Add(IReadOnlyList<float> samples)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    double sample = samples[i];
                    peak = Math.Max(peak, Math.Abs(sample));
                    sumSquares += sample * sample;
                }
                sampleCount += samples.Count;
            }

            public InputLevelMeterEntry ToEntry()
            {
                if (sampleCount == 0)
                {
                    return InputLevelMeterEntry.Unavailable;
                }

                double rms = Math.Sqrt(sumSquares / sampleCount);
                return new InputLevelMeterEntry(
                    true,
                    DataHelper.AmplitudeToDecibels(peak),
                    DataHelper.AmplitudeToDecibels(rms),
                    !fullScaleReference && peak >= 0.999,
                    fullScaleReference && peak >= 0.999);
            }
        }

        private static int? NormalizeOptionalWaveChannel(int? offset)
        {
            return offset.HasValue
                ? Math.Clamp(offset.Value, 0, 1)
                : null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExpSweepMeasurement));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellationTokenSource?.Cancel();
            try
            {
                measurementTask?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            cancellationTokenSource?.Dispose();
            DisposeRecorders();
            Sweep?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

public readonly record struct SweepAverageProgress(
    int CurrentRun,
    int TotalRuns,
    int AcceptedRuns,
    SweepAverageProgressState State);

public enum SweepAverageProgressState
{
    Running,
    WaitingForConfirmation
}
