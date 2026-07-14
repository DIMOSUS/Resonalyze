using System.Numerics;
using Resonalyze.Dsp;
using static System.Math;

namespace Resonalyze
{
    /// <summary>
    /// Coordinates sweep playback, recording, and FFT-based deconvolution. The
    /// backend lifecycle (device open/close, format negotiation, thread and
    /// event handling, alignment retries) lives entirely behind
    /// <see cref="IAudioSessionFactory"/>; this class owns only the measurement
    /// policy — run acceptance, averaging, deconvolution and the transfer
    /// function.
    /// </summary>
    public sealed class ExpSweepMeasurement : IDisposable
    {
        private readonly IAudioSessionFactory audioSessionFactory;
        private readonly object stateSync = new();
        private CancellationTokenSource? cancellationTokenSource;
        private Task<bool>? measurementTask;
        private TaskCompletionSource<bool>? averageConfirmation;
        private volatile bool inProgress;
        private volatile bool waitingForAverageConfirmation;
        private bool disposed;
        // Results are published from the measurement worker and read by the UI
        // without locks. Each impulse response travels with its peak index as one
        // immutable reference so a reader can never pair a new response with a
        // stale index. The level snapshot is a multi-field struct and is kept
        // boxed for the same reason: a reference swap is atomic, a struct copy is not.
        private volatile MeasurementImpulseResponse? sweepDeconvolutionResult;
        private volatile MeasurementImpulseResponse? transferResult;
        private volatile object currentLevels = InputLevelMeterSnapshot.Empty;

        public event Action<bool>? Completed;
        public event Action? ImpulseResponseChanged;
        public event Action<SweepAverageProgress>? AverageProgressChanged;
        internal event Action<InputLevelMeterSnapshot>? LevelsAvailable;

        public ExpSweepMeasurement(IAudioSessionFactory audioSessionFactory)
        {
            this.audioSessionFactory = audioSessionFactory ??
                throw new ArgumentNullException(nameof(audioSessionFactory));
        }

        public ExponentialSineSweep? Sweep { get; private set; }
        public MeasurementImpulseResponse? SweepDeconvolution => sweepDeconvolutionResult;
        public MeasurementImpulseResponse? Transfer => transferResult;
        public Complex[]? SweepDeconvolutionImpulseResponse => sweepDeconvolutionResult?.ImpulseResponse;
        public int SweepDeconvolutionPeakIndex => sweepDeconvolutionResult?.PeakIndex ?? 0;
        public Complex[]? TransferImpulseResponse => transferResult?.ImpulseResponse;
        public int TransferPeakIndex => transferResult?.PeakIndex ?? 0;
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
        public string? WasapiCaptureEndpointId { get; private set; }
        public string? WasapiRenderEndpointId { get; private set; }
        public string? WasapiCaptureEndpointName { get; private set; }
        public string? WasapiRenderEndpointName { get; private set; }
        public int WasapiBufferMilliseconds { get; private set; } = 100;
        public AudioSessionDiagnostics? LastAudioSessionDiagnostics { get; private set; }
        public string? AsioDriverName { get; private set; }
        public int WaveInputChannelOffset { get; private set; }
        public int? WaveLoopbackInputChannelOffset { get; private set; }
        public int AsioInputChannelOffset { get; private set; }
        public int? AsioLoopbackInputChannelOffset { get; private set; }
        public int AsioOutputChannelOffset { get; private set; }
        public int AverageRunCount { get; private set; } = 1;
        public int AcceptedAverageRunCount { get; private set; } = 1;
        public bool ConfirmEachAverageRun { get; private set; }
        // Per-run acceptance outcome of the last completed measurement; null until
        // a measurement ran (or when the result was restored from a file).
        internal SweepRunQualityReport? QualityReport { get; private set; }
        public Exception? LastError { get; private set; }
        public bool WaitingForAverageConfirmation => waitingForAverageConfirmation;
        internal InputLevelMeterSnapshot CurrentLevels
        {
            get => (InputLevelMeterSnapshot)currentLevels;
            private set => currentLevels = value;
        }

        /// <summary>
        /// Compatibility adapter for callers compiled against the original flat
        /// parameter list. New code should use the grouped configuration overload.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
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
            int averageRunCount = 1,
            bool confirmEachAverageRun = false,
            string? wasapiCaptureEndpointId = null,
            string? wasapiRenderEndpointId = null,
            int wasapiBufferMilliseconds = 100,
            string? wasapiCaptureEndpointName = null,
            string? wasapiRenderEndpointName = null)
        {
            Init(new SweepMeasurementConfiguration(
                new SweepSignalConfiguration(
                    octaves,
                    sampleRate,
                    bits,
                    requestedDuration,
                    playbackChannel),
                new SweepAudioConfiguration(
                    audioBackend,
                    outputDeviceNumber,
                    inputDeviceNumber,
                    waveInputChannelOffset,
                    waveLoopbackInputChannelOffset,
                    asioDriverName,
                    asioInputChannelOffset,
                    asioLoopbackInputChannelOffset,
                    asioOutputChannelOffset,
                    wasapiCaptureEndpointId,
                    wasapiRenderEndpointId,
                    wasapiCaptureEndpointName,
                    wasapiRenderEndpointName,
                    wasapiBufferMilliseconds),
                new SweepAveragingConfiguration(
                    averageRunCount,
                    confirmEachAverageRun)));
        }

        public void Init(SweepMeasurementConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ThrowIfDisposed();
            if (InProgress)
            {
                throw new InvalidOperationException("Cannot reinitialize an active measurement.");
            }

            SweepSignalConfiguration signal = configuration.Signal;
            SweepAudioConfiguration audio = configuration.Audio;
            SweepAveragingConfiguration averaging = configuration.Averaging;
            PlaybackChannel = Enum.IsDefined(signal.PlaybackChannel)
                ? signal.PlaybackChannel
                : PlaybackChannel.Mono;
            SampleRate = signal.SampleRate;
            Bits = signal.Bits;
            Octaves = signal.Octaves;
            OutputDeviceNumber = audio.OutputDeviceNumber;
            InputDeviceNumber = audio.InputDeviceNumber;
            WasapiCaptureEndpointId = audio.WasapiCaptureEndpointId;
            WasapiRenderEndpointId = audio.WasapiRenderEndpointId;
            WasapiCaptureEndpointName = audio.WasapiCaptureEndpointName;
            WasapiRenderEndpointName = audio.WasapiRenderEndpointName;
            WasapiBufferMilliseconds = Math.Clamp(audio.WasapiBufferMilliseconds, 10, 100);
            LastAudioSessionDiagnostics = null;
            AudioBackend = audio.Backend;
            AsioDriverName = audio.AsioDriverName;
            int normalizedWaveInputChannelOffset = IsWasapiBackend(audio.Backend)
                ? Math.Max(0, audio.WaveInputChannelOffset)
                : Math.Clamp(audio.WaveInputChannelOffset, 0, 1);
            int? normalizedWaveLoopbackInputChannelOffset = IsWasapiBackend(audio.Backend)
                ? NormalizeOptionalWasapiChannel(audio.WaveLoopbackInputChannelOffset)
                : NormalizeOptionalWaveChannel(audio.WaveLoopbackInputChannelOffset);
            if (audio.Backend != AudioBackend.Asio &&
                normalizedWaveLoopbackInputChannelOffset == normalizedWaveInputChannelOffset)
            {
                throw new InvalidOperationException(
                    "Microphone and loopback inputs must use different channels.");
            }
            WaveInputChannelOffset = normalizedWaveInputChannelOffset;
            WaveLoopbackInputChannelOffset = normalizedWaveLoopbackInputChannelOffset;
            AsioInputChannelOffset = audio.AsioInputChannelOffset;
            AsioLoopbackInputChannelOffset = audio.AsioLoopbackInputChannelOffset;
            AsioOutputChannelOffset = audio.AsioOutputChannelOffset;
            sweepDeconvolutionResult = null;
            transferResult = null;
            TransferCoherence = null;
            MicrophoneRecordedSamples = null;
            LoopbackRecordedSamples = null;
            MeasurementMode = SweepMeasurementMode.SweepDeconvolution;
            AverageRunCount = Math.Clamp(averaging.RunCount, 1, 64);
            AcceptedAverageRunCount = 0;
            ConfirmEachAverageRun = averaging.ConfirmEachRun;
            QualityReport = null;
            LastError = null;
            CurrentLevels = InputLevelMeterSnapshot.Empty;

            Sweep?.Dispose();
            Sweep = new ExponentialSineSweep();
            Sweep.FillData(
                signal.Octaves,
                signal.RequestedDurationSeconds,
                signal.Bits,
                signal.SampleRate);
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
                if (Sweep == null)
                {
                    throw new InvalidOperationException("Measurement is not initialized.");
                }

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = new CancellationTokenSource();
                inProgress = true;
                sweepDeconvolutionResult = null;
                transferResult = null;
                TransferCoherence = null;
                MicrophoneRecordedSamples = null;
                LoopbackRecordedSamples = null;
                MeasurementMode = SweepMeasurementMode.SweepDeconvolution;
                AcceptedAverageRunCount = 0;
                QualityReport = null;
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

            Init(new SweepMeasurementConfiguration(
                new SweepSignalConfiguration(
                    octaves,
                    sampleRate,
                    bits,
                    sweepDurationSeconds,
                    playChannel),
                new SweepAudioConfiguration(
                    Backend: AudioBackend,
                    OutputDeviceNumber: OutputDeviceNumber,
                    InputDeviceNumber: InputDeviceNumber,
                    WaveInputChannelOffset: WaveInputChannelOffset,
                    WaveLoopbackInputChannelOffset: WaveLoopbackInputChannelOffset,
                    AsioDriverName: AsioDriverName,
                    AsioInputChannelOffset: AsioInputChannelOffset,
                    AsioLoopbackInputChannelOffset: AsioLoopbackInputChannelOffset,
                    AsioOutputChannelOffset: AsioOutputChannelOffset,
                    WasapiCaptureEndpointId: WasapiCaptureEndpointId,
                    WasapiRenderEndpointId: WasapiRenderEndpointId,
                    WasapiCaptureEndpointName: WasapiCaptureEndpointName,
                    WasapiRenderEndpointName: WasapiRenderEndpointName,
                    WasapiBufferMilliseconds: WasapiBufferMilliseconds),
                new SweepAveragingConfiguration(
                    AverageRunCount,
                    ConfirmEachAverageRun)));
            sweepDeconvolutionResult = new MeasurementImpulseResponse(
                sweepDeconvolutionImpulseResponse.ToArray(),
                sweepDeconvolutionPeakIndex);
            transferResult = transferImpulseResponse != null
                ? new MeasurementImpulseResponse(
                    transferImpulseResponse.ToArray(),
                    transferPeakIndex!.Value)
                : null;
            TransferCoherence = transferCoherence?.ToArray();
            MicrophoneRecordedSamples = null;
            LoopbackRecordedSamples = null;
            MeasurementMode = measurementMode;
            AverageRunCount = Math.Clamp(averageRunCount, 1, 64);
            AcceptedAverageRunCount = Math.Clamp(
                acceptedAverageRunCount,
                1,
                AverageRunCount);
            LastError = null;
            Publish(ImpulseResponseChanged);
        }

        internal void RestoreLevelSnapshot(InputLevelMeterSnapshot snapshot)
        {
            ThrowIfDisposed();
            CurrentLevels = snapshot;
            RaiseLevels(snapshot);
        }

        private async Task<bool> RunCoreAsync(CancellationToken cancellationToken)
        {
            ExponentialSineSweep sweep = Sweep!;
            bool success = false;
            IAudioDuplexSession? session = null;

            try
            {
                AudioSessionRequest request = BuildSessionRequest(sweep);
                AudioPlaybackSignal signal = BuildPlaybackSignal(sweep);
                session = await audioSessionFactory
                    .OpenDuplexAsync(request, signal, cancellationToken).ConfigureAwait(false);
                session.InputLevelsAvailable += HandleSessionLevels;

                async Task<AudioCaptureResult> CaptureOneAsync()
                {
                    AudioCaptureResult result = await session
                        .PlayAndCaptureAsync(SampleRate, cancellationToken)
                        .ConfigureAwait(false);
                    if (result.Diagnostics != null)
                    {
                        LastAudioSessionDiagnostics = result.Diagnostics;
                    }
                    return result;
                }

                // The sweep spans Octaves octaves up to Nyquist, so its low
                // edge is Nyquist / 2^Octaves — the transfer estimator masks
                // the never-excited bins below it.
                var accumulator = new SweepAverageAccumulator(
                    Math.Pow(2.0, -sweep.Octaves));
                var rejections = new List<SweepRunRejection>();
                int requestedRuns = AverageRunCount;
                for (int run = 1; run <= requestedRuns; run++)
                {
                    Publish(AverageProgressChanged, new SweepAverageProgress(
                        run,
                        requestedRuns,
                        accumulator.AcceptedRuns,
                        SweepAverageProgressState.Running));
                    AudioCaptureResult? captured = await CaptureOneAsync().ConfigureAwait(false);
                    IReadOnlyList<string> issues = AssessRunQuality(captured, sweep);
                    if (issues.Count > 0)
                    {
                        // One automatic retry per bad run; a second failure skips
                        // the run so it cannot contaminate the average.
                        rejections.Add(new SweepRunRejection(run, Retried: false, issues));
                        Publish(AverageProgressChanged, new SweepAverageProgress(
                            run,
                            requestedRuns,
                            accumulator.AcceptedRuns,
                            SweepAverageProgressState.Retrying));
                        captured = await CaptureOneAsync().ConfigureAwait(false);
                        issues = AssessRunQuality(captured, sweep);
                        if (issues.Count > 0)
                        {
                            rejections.Add(new SweepRunRejection(run, Retried: true, issues));
                            captured = null;
                        }
                    }
                    if (captured != null)
                    {
                        accumulator.Add(AnalyzeCapturedRun(captured, sweep));
                    }

                    if (ConfirmEachAverageRun && run < requestedRuns)
                    {
                        await WaitForAverageConfirmationAsync(
                            run,
                            requestedRuns,
                            accumulator.AcceptedRuns,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                QualityReport = new SweepRunQualityReport(
                    requestedRuns,
                    accumulator.AcceptedRuns,
                    rejections);
                if (accumulator.AcceptedRuns == 0)
                {
                    throw new InvalidOperationException(
                        "Every sweep run failed the capture quality checks: " +
                        string.Join(
                            "; ",
                            rejections.SelectMany(rejection => rejection.Issues).Distinct()) +
                        ". Check the input levels and the loopback wiring, then measure again.");
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
                    if (session != null)
                    {
                        session.InputLevelsAvailable -= HandleSessionLevels;
                        await session.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    // A device teardown failure must not demote results that were
                    // already published: ApplyAverageResult has run and raised
                    // ImpulseResponseChanged, so the captured data is complete
                    // regardless of how the device teardown went.
                    LastError ??= exception;
                }

                lock (stateSync)
                {
                    waitingForAverageConfirmation = false;
                    averageConfirmation = null;
                    inProgress = false;
                }
                Publish(Completed, success);
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

            Publish(AverageProgressChanged, new SweepAverageProgress(
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

        private AudioSessionRequest BuildSessionRequest(ExponentialSineSweep sweep) =>
            AudioSessionRequestBuilder.Build(
                AudioBackend,
                SampleRate,
                Bits,
                PlaybackChannel,
                WaveInputChannelOffset,
                WaveLoopbackInputChannelOffset,
                AsioInputChannelOffset,
                AsioLoopbackInputChannelOffset,
                AsioOutputChannelOffset,
                OutputDeviceNumber,
                InputDeviceNumber,
                WasapiCaptureEndpointId,
                WasapiRenderEndpointId,
                AsioDriverName,
                WasapiBufferMilliseconds,
                expectedCaptureSamples: sweep.SweepSamples + SampleRate * 2);

        private AudioPlaybackSignal BuildPlaybackSignal(ExponentialSineSweep sweep) =>
            new(sweep.SweepData, SampleRate, Bits, PlaybackChannel, Loop: false);

        private void HandleSessionLevels(AudioInputLevels levels)
        {
            InputLevelMeterSnapshot snapshot = InputLevelMapping.Map(levels);
            CurrentLevels = snapshot;
            RaiseLevels(snapshot);
        }

        private IReadOnlyList<string> AssessRunQuality(
            AudioCaptureResult captured,
            ExponentialSineSweep sweep)
        {
            float[][] channels = captured.Channels;
            float[] microphone = (uint)captured.MicrophoneChannel < (uint)channels.Length
                ? channels[captured.MicrophoneChannel]
                : Array.Empty<float>();
            float[]? loopback = captured.LoopbackChannel is int loopbackIndex &&
                (uint)loopbackIndex < (uint)channels.Length
                    ? channels[loopbackIndex]
                    : null;
            var issues = SweepRunQualityCheck.Assess(
                microphone,
                loopback,
                sweep.SweepSamples).ToList();
            if (captured.Anomalies.HasFlag(AudioCaptureAnomalies.CaptureDiscontinuity))
            {
                issues.Add("WASAPI reported a capture packet discontinuity.");
            }
            if (captured.Anomalies.HasFlag(AudioCaptureAnomalies.CaptureTimestampError))
            {
                issues.Add("WASAPI reported an invalid capture timestamp.");
            }
            if (captured.Anomalies.HasFlag(AudioCaptureAnomalies.RenderUnderrun))
            {
                issues.Add("WASAPI reported a render buffer underrun.");
            }
            return issues;
        }

        private SweepRunAnalysis AnalyzeCapturedRun(
            AudioCaptureResult captured,
            ExponentialSineSweep sweep,
            bool raiseIntermediateLevels = true)
        {
            float[][] sampleChannels = captured.Channels;
            if (captured.StereoSeparationExpected &&
                captured.LoopbackChannel is int validationLoopbackIndex)
            {
                RecordedChannelValidator.EnsureDifferentSignals(
                    sampleChannels,
                    captured.MicrophoneChannel,
                    validationLoopbackIndex,
                    IsWasapiBackend(AudioBackend)
                        ? "WASAPI measurement"
                        : "Wave measurement");
            }

            float[] recorded = (uint)captured.MicrophoneChannel < (uint)sampleChannels.Length
                ? sampleChannels[captured.MicrophoneChannel]
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
                captured.MicrophoneChannel,
                captured.LoopbackChannel,
                out TransferFunctionFrame frame))
            {
                transferFrame = frame;
            }

            InputLevelMeterSnapshot finalLevels = CreateFinalLevelSnapshot(
                sampleChannels,
                captured.MicrophoneChannel,
                captured.LoopbackChannel);
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
                captured.MicrophoneChannel,
                captured.LoopbackChannel,
                finalLevels);
        }

        private void ApplyAverageResult(SweepAverageResult result)
        {
            sweepDeconvolutionResult = new MeasurementImpulseResponse(
                result.SweepImpulseResponse,
                result.SweepPeakIndex);
            transferResult = result.TransferImpulseResponse != null
                ? new MeasurementImpulseResponse(
                    result.TransferImpulseResponse,
                    result.TransferPeakIndex)
                : null;
            TransferCoherence = result.TransferCoherence;
            MicrophoneRecordedSamples = result.MicrophoneRecordedSamples;
            LoopbackRecordedSamples = result.LoopbackRecordedSamples;
            MeasurementMode = result.TransferImpulseResponse != null
                ? SweepMeasurementMode.LoopbackTransfer
                : SweepMeasurementMode.SweepDeconvolution;
            AcceptedAverageRunCount = result.AcceptedRunCount;
            CurrentLevels = result.Levels;
            RaiseLevels(result.Levels);
            Publish(ImpulseResponseChanged);
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
            AudioChannelLevel[] measuredLevels = RecordedLevelMetering.MeasureChannels(sampleChannels);
            return InputLevelMapping.Map(measuredLevels, microphoneIndex, loopbackIndex);
        }

        private void RaiseLevels(InputLevelMeterSnapshot snapshot)
        {
            Publish(LevelsAvailable, snapshot);
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
            private readonly double excitationLowNyquistFraction;
            private readonly List<TransferFunctionFrame> transferFrames = new();
            private readonly ChannelLevelAccumulator microphoneLevels = new(fullScaleReference: false);
            private readonly ChannelLevelAccumulator loopbackLevels = new(fullScaleReference: true);
            private Complex[]? sweepSum;
            private int referencePeakIndex;
            private float[]? lastMicrophoneSamples;
            private float[]? lastLoopbackSamples;

            public SweepAverageAccumulator(double excitationLowNyquistFraction)
            {
                this.excitationLowNyquistFraction = excitationLowNyquistFraction;
            }

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
                        transferFrames,
                        excitationLowNyquistFraction);
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

                AudioChannelLevel level = RecordedLevelMetering.Measure(peak, sumSquares, sampleCount);
                return new InputLevelMeterEntry(
                    true,
                    level.PeakDbFs,
                    level.RmsDbFs,
                    !fullScaleReference && level.FullScale,
                    fullScaleReference && level.FullScale);
            }
        }

        private static int? NormalizeOptionalWaveChannel(int? offset)
        {
            return offset.HasValue
                ? Math.Clamp(offset.Value, 0, 1)
                : null;
        }

        private static int? NormalizeOptionalWasapiChannel(int? offset) =>
            offset.HasValue ? Math.Max(0, offset.Value) : null;

        private static bool IsWasapiBackend(AudioBackend backend) =>
            backend is AudioBackend.WasapiShared or AudioBackend.WasapiExclusive;

        private static void Publish(Action? handlers)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
            {
                try
                {
                    handler();
                }
                catch
                {
                    // Measurement notifications are observational. A broken UI
                    // subscriber must not change the measurement outcome.
                }
            }
        }

        private static void Publish<T>(Action<T>? handlers, T value)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Action<T> handler in handlers.GetInvocationList().Cast<Action<T>>())
            {
                try
                {
                    handler(value);
                }
                catch
                {
                    // Continue with the remaining subscribers and cleanup.
                }
            }
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
            Sweep?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// An impulse response published together with the index of its peak as one
    /// immutable reference, so cross-thread readers always see a matching pair.
    /// </summary>
    public sealed record MeasurementImpulseResponse(
        Complex[] ImpulseResponse,
        int PeakIndex);
}

public readonly record struct SweepAverageProgress(
    int CurrentRun,
    int TotalRuns,
    int AcceptedRuns,
    SweepAverageProgressState State);

public enum SweepAverageProgressState
{
    Running,
    WaitingForConfirmation,
    // The run failed the capture quality checks and its single automatic
    // retry is being captured.
    Retrying
}
