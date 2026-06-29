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
        private volatile bool inProgress;
        private bool disposed;

        public event Action<bool>? Completed;
        public event Action? ImpulseResponseChanged;
        internal event Action<InputLevelMeterSnapshot>? LevelsAvailable;

        public ExponentialSineSweep? Sweep { get; private set; }
        public Complex[]? SweepDeconvolutionImpulseResponse { get; private set; }
        public int SweepDeconvolutionPeakIndex { get; private set; }
        public Complex[]? TransferImpulseResponse { get; private set; }
        public int TransferPeakIndex { get; private set; }
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
        public Exception? LastError { get; private set; }
        public int RecordedSamples => soundRecorder?.ReadSamples ?? 0;
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
            int? waveLoopbackDeviceNumber = null)
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
            MicrophoneRecordedSamples = null;
            LoopbackRecordedSamples = null;
            MeasurementMode = SweepMeasurementMode.SweepDeconvolution;
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
                inputDeviceNumber);
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
                    WaveLoopbackDeviceNumber!.Value);
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
                MicrophoneRecordedSamples = null;
                LoopbackRecordedSamples = null;
                MeasurementMode = SweepMeasurementMode.SweepDeconvolution;
                LastError = null;
                CurrentLevels = InputLevelMeterSnapshot.Empty;
                measurementTask = RunCoreAsync(cancellationTokenSource.Token);
                return measurementTask;
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
            int? transferPeakIndex = null)
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
                WaveLoopbackDeviceNumber);
            SweepDeconvolutionImpulseResponse = sweepDeconvolutionImpulseResponse.ToArray();
            SweepDeconvolutionPeakIndex = sweepDeconvolutionPeakIndex;
            TransferImpulseResponse = transferImpulseResponse?.ToArray();
            MicrophoneRecordedSamples = null;
            LoopbackRecordedSamples = null;
            MeasurementMode = measurementMode;
            TransferPeakIndex = transferImpulseResponse != null
                ? transferPeakIndex!.Value
                : 0;
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
                if (AudioBackend == AudioBackend.Asio)
                {
                    await RunAsioAsync(sweep, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await RunWaveAsync(sweep, recorder, cancellationToken).ConfigureAwait(false);
                }
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

        private async Task RunWaveAsync(
            ExponentialSineSweep sweep,
            SoundRecorder recorder,
            CancellationToken cancellationToken)
        {
            if (UsesSeparateWaveLoopbackDevice)
            {
                await RunWaveDualDeviceAsync(sweep, recorder, loopbackRecorder!, cancellationToken)
                    .ConfigureAwait(false);
                return;
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
            ProcessImpulseResponse(
                recorder.GetSamplesSnapshot(),
                sweep,
                microphoneIndex,
                loopbackIndex,
                validateSharedDeviceStereo: true);
        }

        // Microphone and loopback come from two independent Wave devices. Both recorders are
        // started before playback, then aligned at their first sample and trimmed to a shared
        // length (best-effort; see DualDeviceCapture). The merged array is [microphone, loopback].
        private async Task RunWaveDualDeviceAsync(
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
            ProcessImpulseResponse(
                merged,
                sweep,
                channelIndex: 0,
                loopbackIndex: 1,
                validateSharedDeviceStereo: false);
        }

        private async Task RunAsioAsync(
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
                cancellationToken).ConfigureAwait(false);
            int requiredSamples = session.ReadSamples + sweep.SweepSamples + SampleRate;
            await session.WaitForSamplesAsync(requiredSamples, cancellationToken).ConfigureAwait(false);
            await session.StopAsync().ConfigureAwait(false);

            int microphoneIndex = AsioInputChannelOffset - firstInputOffset;
            int? loopbackIndex = AsioLoopbackInputChannelOffset.HasValue
                ? AsioLoopbackInputChannelOffset.Value - firstInputOffset
                : null;
            ProcessImpulseResponse(
                session.GetSamplesSnapshot(),
                sweep,
                microphoneIndex,
                loopbackIndex,
                validateSharedDeviceStereo: false);
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

        private void ProcessImpulseResponse(
            float[][] sampleChannels,
            ExponentialSineSweep sweep,
            int channelIndex,
            int? loopbackIndex,
            bool validateSharedDeviceStereo)
        {
            if (validateSharedDeviceStereo &&
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

            float[] recorded = (uint)channelIndex < (uint)sampleChannels.Length
                ? sampleChannels[channelIndex]
                : Array.Empty<float>();
            if (recorded.Length == 0)
            {
                throw new InvalidOperationException("No audio samples were recorded.");
            }

            MicrophoneRecordedSamples = recorded.ToArray();
            SweepDeconvolutionResult sweepResult = SweepAnalysis.DeconvolveWithInverseFilter(
                recorded,
                sweep.InverseFilter,
                2.0 / sweep.InverseFilter.Length);
            SweepDeconvolutionImpulseResponse = Array.ConvertAll(
                sweepResult.ImpulseResponse,
                x => new Complex(x, 0.0));
            SweepDeconvolutionPeakIndex = sweepResult.PeakIndex;

            if (TryCaptureLoopback(sampleChannels, channelIndex, loopbackIndex, out double[]? relativeIr))
            {
                TransferImpulseResponse = Array.ConvertAll(relativeIr, x => new Complex(x, 0.0));
                TransferPeakIndex = FindPeakIndex(relativeIr);
                MeasurementMode = SweepMeasurementMode.LoopbackTransfer;
            }
            else
            {
                TransferImpulseResponse = null;
                TransferPeakIndex = 0;
                MeasurementMode = SweepMeasurementMode.SweepDeconvolution;
            }

            InputLevelMeterSnapshot finalLevels = CreateFinalLevelSnapshot(
                sampleChannels,
                channelIndex,
                loopbackIndex);
            CurrentLevels = finalLevels;
            RaiseLevels(finalLevels);
            ImpulseResponseChanged?.Invoke();
        }

        private bool TryCaptureLoopback(
            float[][] sampleChannels,
            int microphoneIndex,
            int? loopbackIndex,
            [NotNullWhen(true)] out double[]? relativeIr)
        {
            relativeIr = null;
            LoopbackRecordedSamples = null;
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

            LoopbackRecordedSamples = sampleChannels[loopbackIndex.Value].ToArray();
            double[] loopback = Array.ConvertAll(
                LoopbackRecordedSamples,
                sample => (double)sample);
            double[] microphone = Array.ConvertAll(
                sampleChannels[microphoneIndex],
                sample => (double)sample);
            relativeIr = TransferFunction.ComputeRelativeIr(loopback, microphone);
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

        private static AudioChannelLevel MeasureRecordedChannel(float[] samples)
        {
            double peak = 0;
            double sumSquares = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double magnitude = Math.Abs(samples[i]);
                peak = Math.Max(peak, magnitude);
                sumSquares += samples[i] * samples[i];
            }

            double rms = samples.Length > 0
                ? Math.Sqrt(sumSquares / samples.Length)
                : 0;
            return new AudioChannelLevel(
                DataHelper.AmplitudeToDecibels(peak),
                DataHelper.AmplitudeToDecibels(rms),
                peak >= 0.999);
        }

        private int GetRequiredWaveInputChannelCount()
        {
            int maxChannelOffset = WaveInputChannelOffset;
            if (WaveLoopbackInputChannelOffset.HasValue)
            {
                maxChannelOffset = Math.Max(
                    maxChannelOffset,
                    WaveLoopbackInputChannelOffset.Value);
            }

            return maxChannelOffset + 1;
        }

        private int GetAsioCaptureFirstInputOffset()
        {
            return AsioLoopbackInputChannelOffset.HasValue
                ? Math.Min(AsioInputChannelOffset, AsioLoopbackInputChannelOffset.Value)
                : AsioInputChannelOffset;
        }

        private int GetRequiredAsioInputChannelCount(int firstInputOffset)
        {
            int lastInputOffset = AsioLoopbackInputChannelOffset.HasValue
                ? Math.Max(AsioInputChannelOffset, AsioLoopbackInputChannelOffset.Value)
                : AsioInputChannelOffset;
            return lastInputOffset - firstInputOffset + 1;
        }

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
