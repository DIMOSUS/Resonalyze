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
        private volatile bool inProgress;
        // Whether inProgress is held by an outstanding Claim rather than by a run.
        private bool claimed;
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

        /// <summary>
        /// What the current result's arrival time means. A measured sweep is
        /// referenced to its own loopback and carries real delay; an imported
        /// recording is referenced to nothing, and everything that compares delays
        /// ACROSS measurements has to refuse it rather than show a number.
        /// </summary>
        public TimingReference TimingReference { get; private set; } =
            TimingReference.SynchronizedLoopback;

        /// <summary>
        /// The time-scale correction the import applied, in parts per million, or
        /// null when the result did not come from an import or needed none.
        /// <para>
        /// Two things put a recording out of scale with the sweep it is analyzed
        /// against, and the data cannot tell them apart. One is the clocks:
        /// whatever played the file and whatever recorded it are separate crystals,
        /// tens of ppm apart. The other is the settings — the per-octave field is
        /// whole milliseconds, so the duration that produced a given file often
        /// cannot be typed back in exactly. Measured on a field pair: the sweep the
        /// panel could express ran 489 ppm longer than the one the exported file
        /// proves was played, while the two clocks agreed to within 25 ppm.
        /// </para>
        /// </summary>
        public double? ImportedTimeScalePpm { get; private set; }

        /// <summary>
        /// Which channel of an imported recording was measured — the one whose
        /// match against the sweep was strongest. Zero for a mono file and for
        /// every result that did not come from a multi-channel import.
        /// </summary>
        public int ImportedChannelIndex { get; private set; }
        public bool HasImpulseResponse => SweepDeconvolutionImpulseResponse != null;
        public bool InProgress => inProgress;
        public int SampleRate { get; private set; }
        public double LowFrequencyHz { get; private set; }
        public double HighFrequencyHz { get; private set; }
        // The band the sweep behind the CURRENT result actually swept, which is
        // what harmonic geometry reads. For a fresh capture it is the generated
        // sweep's; for a restored one it is what the file recorded, because a
        // rebuilt sweep cannot always reproduce it — a pre-band file's low edge
        // carried less than one whole cycle, which the whole-cycle model the
        // generator uses today cannot express.
        public double AchievedLowFrequencyHz { get; private set; }
        public double AchievedHighFrequencyHz { get; private set; }

        /// <summary>
        /// The band the sweep excited at FULL amplitude — what the measurement can
        /// honestly be read over, as opposed to what it reaches.
        /// </summary>
        /// <remarks>
        /// The generator widens the requested band by half an octave each side and
        /// puts the fades in those guard bands, so the envelope is flat across what
        /// was asked for. Inside a guard band the excitation is still there and H1 is
        /// still unbiased — the taper cancels in Gxy/Gxx — but its signal-to-noise
        /// falls away, and the validity weight the estimate carries attenuates it to
        /// match: measured on a 500-5000 Hz sweep the weight reads -13.0 dB at 400 Hz,
        /// -2.7 at 450 and -10.3 at 6300. Presenting that as the driver's response
        /// would draw the estimator's own roll-off as a measurement, on a perfect
        /// system, with nothing to say it was not real.
        /// </remarks>
        public double MeasuredLowFrequencyHz { get; private set; }
        public double MeasuredHighFrequencyHz { get; private set; }

        /// <summary>
        /// When this result was MEASURED, which is not when its file was written.
        /// </summary>
        /// <remarks>
        /// A spatial average built from it is shown with this date, because nothing
        /// records where an array's microphones stood and when they stood there is the
        /// only evidence a user has that two channels came from one sitting. Taking it
        /// from the file's own save stamp instead survives a load but not a re-save:
        /// open Monday's measurement and Friday's on Saturday, save each once, and
        /// both read Saturday — the warning then says the two came from one session,
        /// which is exactly the claim it exists to let a user check.
        /// </remarks>
        public DateTimeOffset MeasuredAtUtc { get; private set; } = DateTimeOffset.UtcNow;
        // Length of that same sweep. Also recorded rather than read back off the
        // rebuilt one, which caps its generation at MaxDurationSeconds and would
        // otherwise halve the harmonic offsets of a restored 200-second sweep.
        public int AchievedSweepSampleCount { get; private set; }

        /// <summary>Length in seconds of the sweep behind the current result.</summary>
        public double AchievedSweepDurationSeconds =>
            SampleRate > 0 ? AchievedSweepSampleCount / (double)SampleRate : 0.0;
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
        public IReadOnlyList<int> WaveArrayInputChannelOffsets { get; private set; } = [];
        public IReadOnlyList<int> AsioArrayInputChannelOffsets { get; private set; } = [];

        /// <summary>
        /// The array microphone channels for the backend in use — the further
        /// microphones this measurement records for its spatial average.
        /// </summary>
        public IReadOnlyList<int> ArrayInputChannelOffsets =>
            AudioBackend == AudioBackend.Asio
                ? AsioArrayInputChannelOffsets
                : WaveArrayInputChannelOffsets;

        /// <summary>
        /// Every microphone of the last completed measurement's array, the
        /// measurement microphone first; empty when no array was configured.
        /// </summary>
        /// <remarks>
        /// Settable from outside for the same reason
        /// <see cref="MeasurementSplCalibration"/> is: loading a file restores the
        /// result's own snapshot, and the loaded array belongs to that result
        /// rather than to whatever the next run is configured to record.
        /// </remarks>
        internal IReadOnlyList<ArrayMicrophoneCurve> ArrayMicrophones { get; set; } = [];

        public int AverageRunCount { get; private set; } = 1;
        public int AcceptedAverageRunCount { get; private set; } = 1;
        public ProtectiveHighPassConfiguration ProtectiveHighPass { get; private set; } =
            ProtectiveHighPassConfiguration.Off;

        /// <summary>
        /// The calibration CONFIGURED for the measurement microphone — the curve
        /// the analysis views read it through. Nothing here uses it; it is carried
        /// so the saved file can say which microphone response the impulse
        /// response was taken with, which is what makes a measurement portable.
        /// </summary>
        public VirtualCrossoverCalibrationSettings? MicrophoneCalibration { get; set; }

        /// <summary>
        /// The notes and calibrations configured for the array microphones, keyed
        /// by channel. Same reason: measured curves are stored raw, and this is
        /// what says how to read them.
        /// </summary>
        internal IReadOnlyList<ArrayMicrophoneMetadata> ArrayMicrophoneMetadata { get; set; } = [];

        /// <summary>
        /// The microphone calibration in force when the last run STARTED, frozen
        /// onto its result the way the SPL anchor and the protective high-pass are.
        /// </summary>
        internal VirtualCrossoverCalibrationSettings? MeasurementMicrophoneCalibration
        { get; set; }

        private IReadOnlyList<ArrayMicrophoneMetadata> measurementArrayMetadata = [];
        // The SPL calibration CONFIGURED for the next run (follows the settings /
        // dialog). It is snapshotted into MeasurementSplCalibration when a run
        // starts, so recalibrating afterwards never rewrites an existing result.
        public SplCalibration? SplCalibration { get; set; }

        // The SPL calibration that belongs to the CURRENT result: a snapshot taken
        // when the sweep ran, or the calibration a loaded file carried. This — not
        // the configured one — is what the plot reads and what a save stamps, so a
        // later recalibration (or a preamp-gain change followed by one) cannot
        // retroactively change the meaning of an already-measured impulse response.
        public SplCalibration? MeasurementSplCalibration { get; set; }

        // The protective high-pass that belongs to the CURRENT result: a snapshot
        // taken when the sweep ran, or the one a loaded file carried. NULL means
        // unknown — a file written before this was recorded — and that is a different
        // answer from Off, which is why a save must stamp this rather than the
        // configured ProtectiveHighPass above. Stamping the live setting would turn
        // "nobody knows what filter this response was corrected for" into a confident
        // claim about a filter it never passed through.
        public ProtectiveHighPassConfiguration? MeasurementProtectiveHighPass { get; set; }

        // The input identity the CURRENT result was produced on: a snapshot of the
        // input when the sweep ran, or the input a loaded file was measured on. The
        // SPL anchor is validated against this, not the app's current configuration,
        // so re-saving a loaded file cannot drop an anchor that was valid for it.
        public MeasurementInputIdentity? MeasurementInput { get; set; }

        /// <summary>
        /// Whether <paramref name="calibration"/> was captured on the same digital
        /// input that produced the current result. Both the live plot and the saved
        /// file gate on this — the anchor is valid only for its own tract.
        /// </summary>
        public bool InputMatches(SplCalibration calibration)
        {
            ArgumentNullException.ThrowIfNull(calibration);
            return MeasurementInput is { } identity && calibration.MatchesInput(identity);
        }

        /// <summary>
        /// Whether the NEXT run will carry a usable SPL anchor: an SPL calibration is
        /// configured and was captured on the input this measurement is configured to
        /// run on. Mirrors exactly what <see cref="RunAsync"/> freezes onto the result
        /// (the configured calibration plus the current input identity), so the shell
        /// can predict dB SPL availability before starting a sweep.
        /// </summary>
        public bool NextRunHasSplAnchor =>
            SplCalibration is { } calibration &&
            calibration.MatchesInput(CurrentInputIdentity());

        /// <summary>
        /// The input the measurement microphone is on for the backend in use.
        /// </summary>
        internal int ActiveMicrophoneChannelOffset =>
            AudioBackend == AudioBackend.Asio ? AsioInputChannelOffset : WaveInputChannelOffset;

        internal MeasurementInputIdentity CurrentInputIdentity() => new(
            AudioBackend,
            SampleRate,
            Bits,
            AudioBackend == AudioBackend.Asio ? AsioInputChannelOffset : WaveInputChannelOffset,
            AudioBackend == AudioBackend.Wave ? InputDeviceNumber : null,
            WasapiCaptureEndpointId,
            AsioDriverName);

        // Per-run acceptance outcome of the last completed measurement; null until
        // a measurement ran (or when the result was restored from a file).
        internal SweepRunQualityReport? QualityReport { get; private set; }

        // What the published result's own shape has to say about itself, or null
        // when it has nothing. Set alongside QualityReport and cleared with it, so
        // a restored file never wears the last run's verdict.
        internal SweepResultCaution? ResultCaution { get; private set; }
        public Exception? LastError { get; private set; }
        internal InputLevelMeterSnapshot CurrentLevels
        {
            get => (InputLevelMeterSnapshot)currentLevels;
            private set => currentLevels = value;
        }

        public void Init(SweepMeasurementConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ThrowIfDisposed();
            if (InProgress)
            {
                throw new InvalidOperationException("Cannot reinitialize an active measurement.");
            }

            InitCore(configuration);
        }

        // The body of Init without its guard, for the callers that already hold
        // the measurement busy themselves (see ImportRecordedSweep and
        // RestoreImpulseResponse) and would otherwise have to drop that claim
        // across the call to re-enter here.
        private void InitCore(SweepMeasurementConfiguration configuration)
        {
            SweepSignalConfiguration signal = configuration.Signal;
            SweepAudioConfiguration audio = configuration.Audio;
            SweepAveragingConfiguration averaging = configuration.Averaging;
            PlaybackChannel = Enum.IsDefined(signal.PlaybackChannel)
                ? signal.PlaybackChannel
                : PlaybackChannel.Mono;
            SampleRate = signal.SampleRate;
            Bits = signal.Bits;
            LowFrequencyHz = signal.LowFrequencyHz;
            HighFrequencyHz = signal.HighFrequencyHz;
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
            int normalizedWaveInputChannelOffset = audio.Backend.IsWasapi()
                ? Math.Max(0, audio.WaveInputChannelOffset)
                : Math.Clamp(audio.WaveInputChannelOffset, 0, 1);
            int? normalizedWaveLoopbackInputChannelOffset = audio.Backend.IsWasapi()
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
            WaveArrayInputChannelOffsets = NormalizeArrayChannels(
                audio.WaveArrayInputChannelOffsets,
                normalizedWaveInputChannelOffset,
                normalizedWaveLoopbackInputChannelOffset);
            AsioArrayInputChannelOffsets = NormalizeArrayChannels(
                audio.AsioArrayInputChannelOffsets,
                audio.AsioInputChannelOffset,
                audio.AsioLoopbackInputChannelOffset);
            ArrayMicrophones = [];
            sweepDeconvolutionResult = null;
            transferResult = null;
            // The old result is gone; its calibration and input snapshots go with it.
            // The next run re-snapshots the configured calibration and input.
            MeasurementSplCalibration = null;
            MeasurementProtectiveHighPass = null;
            MeasurementMicrophoneCalibration = null;
            measurementArrayMetadata = [];
            MeasurementInput = null;
            TransferCoherence = null;
            MicrophoneRecordedSamples = null;
            LoopbackRecordedSamples = null;
            MeasurementMode = SweepMeasurementMode.SweepDeconvolution;
            TimingReference = TimingReference.SynchronizedLoopback;
            ImportedTimeScalePpm = null;
            ImportedChannelIndex = 0;
            AverageRunCount = Math.Clamp(averaging.RunCount, 1, 64);
            AcceptedAverageRunCount = 0;
            ProtectiveHighPass = ProtectiveHighPassConfiguration.Normalize(
                configuration.ProtectiveHighPass);
            QualityReport = null;
            ResultCaution = null;
            LastError = null;
            CurrentLevels = InputLevelMeterSnapshot.Empty;

            Sweep?.Dispose();
            Sweep = new ExponentialSineSweep();
            // LowFrequencyHz/HighFrequencyHz keep the REQUESTED band (persisted and
            // round-trip stable); the achieved, phase-aligned band lives on the
            // Sweep and is what the harmonic geometry and masking read.
            Sweep.FillData(
                signal.LowFrequencyHz,
                signal.HighFrequencyHz,
                signal.RequestedDurationSeconds,
                signal.Bits,
                signal.SampleRate);
            AchievedLowFrequencyHz = Sweep.LowFrequencyHz;
            AchievedHighFrequencyHz = Sweep.HighFrequencyHz;
            MeasuredLowFrequencyHz = Sweep.Spec.FullAmplitudeLowFrequencyHz;
            MeasuredHighFrequencyHz = Sweep.Spec.FullAmplitudeHighFrequencyHz;
            AchievedSweepSampleCount = Sweep.SweepSamples;
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
                // An outstanding claim owns this measurement for an operation
                // that has no task to wait on — a file import spanning its
                // decode. A run started underneath it would publish over the
                // import's result, and its own completion would clear the busy
                // flag the import is still holding.
                if (claimed)
                {
                    throw new InvalidOperationException(
                        "The measurement is already busy.");
                }

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = new CancellationTokenSource();
                inProgress = true;
                sweepDeconvolutionResult = null;
                transferResult = null;
                // Freeze the calibration and the input in effect at run start onto this
                // result, so a later setting change cannot rewrite what it means or
                // which input it is validated against.
                MeasurementSplCalibration = SplCalibration;
                MeasurementProtectiveHighPass = ProtectiveHighPass;
                MeasurementMicrophoneCalibration = MicrophoneCalibration;
                measurementArrayMetadata = ArrayMicrophoneMetadata;
                MeasurementInput = CurrentInputIdentity();
                TransferCoherence = null;
                MicrophoneRecordedSamples = null;
                LoopbackRecordedSamples = null;
                MeasurementMode = SweepMeasurementMode.SweepDeconvolution;
                AcceptedAverageRunCount = 0;
                QualityReport = null;
                ResultCaution = null;
                LastError = null;
                CurrentLevels = InputLevelMeterSnapshot.Empty;
                measurementTask = RunCoreAsync(cancellationTokenSource.Token);
                return measurementTask;
            }
        }

        /// <summary>
        /// Claims the measurement for an operation that reaches beyond one call —
        /// decoding a file and then importing it — so everything that gates on
        /// <see cref="InProgress"/> (the record button, the settings panel's Apply)
        /// stays out for the whole of it. Disposing the claim releases it.
        /// </summary>
        /// <remarks>
        /// Without this the claim could only be taken once the samples were in
        /// hand, leaving the decode — seconds, on a long recording — as a window
        /// in which a measurement could be started and then quietly replaced by
        /// the import that was already under way.
        /// </remarks>
        public IDisposable Claim()
        {
            ThrowIfDisposed();
            lock (stateSync)
            {
                if (inProgress)
                {
                    throw new InvalidOperationException(
                        "The measurement is already busy.");
                }
                inProgress = true;
                claimed = true;
            }

            return new MeasurementClaim(this);
        }

        private void ReleaseClaim()
        {
            lock (stateSync)
            {
                claimed = false;
                inProgress = false;
            }
        }

        private sealed class MeasurementClaim(ExpSweepMeasurement measurement) : IDisposable
        {
            private ExpSweepMeasurement? owner = measurement;

            public void Dispose()
            {
                Interlocked.Exchange(ref owner, null)?.ReleaseClaim();
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
            double achievedRatio = AchievedFrequencyRatio;
            if (AchievedSweepSampleCount <= 0 || achievedRatio <= 1.0)
            {
                return 0;
            }
            return AchievedSweepSampleCount * Log(harmonic) / Log(achievedRatio);
        }

        /// <summary>
        /// High/low ratio of the sweep behind the current result. Harmonic packets
        /// sit at <c>SweepSamples * ln(harmonic) / ln(ratio)</c>, so this must be
        /// the ratio that was actually swept, not the one that was requested.
        /// </summary>
        public double AchievedFrequencyRatio =>
            AchievedLowFrequencyHz > 0 && AchievedHighFrequencyHz > AchievedLowFrequencyHz
                ? AchievedHighFrequencyHz / AchievedLowFrequencyHz
                : 0.0;

        /// <summary>
        /// Reinstates a stored result. <paramref name="achievedLowFrequencyHz"/> /
        /// <paramref name="achievedHighFrequencyHz"/> are the edges the stored
        /// sweep actually swept; they pin the harmonic geometry, because
        /// re-deriving a sweep from the requested band reproduces them only for
        /// files written by the band-based generator. Left at 0 the rebuilt
        /// sweep's own band stands in, which is correct only for those files.
        /// </summary>
        public void RestoreImpulseResponse(
            double lowFrequencyHz,
            double highFrequencyHz,
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
            int acceptedAverageRunCount = 1,
            double achievedLowFrequencyHz = 0.0,
            double achievedHighFrequencyHz = 0.0,
            TimingReference timingReference = TimingReference.SynchronizedLoopback,
            double measuredLowFrequencyHz = 0.0,
            double measuredHighFrequencyHz = 0.0,
            DateTimeOffset? measuredAtUtc = null)
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
            // A run blocks; a claim does not. The caller that decoded this file has
            // usually been holding one across the read and the analysis so nothing
            // could start a sweep underneath it, and it must still be able to publish
            // the result it went to the trouble of protecting. Same shape as
            // ImportRecordedSweep, which faced this first.
            bool claimedHere = false;
            lock (stateSync)
            {
                if (inProgress && !claimed)
                {
                    throw new InvalidOperationException(
                        "Cannot load an impulse response while a measurement is running.");
                }

                if (!inProgress)
                {
                    inProgress = true;
                    claimedHere = true;
                }
            }

            try
            {
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

            InitCore(new SweepMeasurementConfiguration(
                new SweepSignalConfiguration(
                    lowFrequencyHz,
                    highFrequencyHz,
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
                    WasapiBufferMilliseconds: WasapiBufferMilliseconds,
                    WaveArrayInputChannelOffsets: WaveArrayInputChannelOffsets,
                    AsioArrayInputChannelOffsets: AsioArrayInputChannelOffsets),
                new SweepAveragingConfiguration(AverageRunCount),
                ProtectiveHighPass));
            // InitCore just set these from the sweep it regenerated; the recorded
            // geometry wins, since that sweep is a reconstruction and this result
            // came from the original one. The length matters as much as the band:
            // generation is capped at MaxDurationSeconds while a stored sweep may
            // be minutes long, and the harmonic offsets scale with it.
            // Restored, never re-stamped: a file re-saved today was still measured
            // whenever it was measured. Null only for a source that carries no time of
            // its own, where the clock is the best answer there is.
            MeasuredAtUtc = measuredAtUtc ?? DateTimeOffset.UtcNow;
            if (achievedLowFrequencyHz > 0 &&
                achievedHighFrequencyHz > achievedLowFrequencyHz)
            {
                AchievedLowFrequencyHz = achievedLowFrequencyHz;
                AchievedHighFrequencyHz = achievedHighFrequencyHz;
                // A file written before the full-amplitude edges were recorded says
                // nothing about them, and the regenerated sweep's are the wrong
                // answer for a stored geometry. Fall back to the achieved band — what
                // such a file has always been read over.
                MeasuredLowFrequencyHz = measuredLowFrequencyHz > 0
                    ? measuredLowFrequencyHz
                    : achievedLowFrequencyHz;
                MeasuredHighFrequencyHz = measuredHighFrequencyHz > MeasuredLowFrequencyHz
                    ? measuredHighFrequencyHz
                    : achievedHighFrequencyHz;
            }
            int storedSampleCount = (int)Math.Round(sweepDurationSeconds * sampleRate);
            if (storedSampleCount > 0)
            {
                AchievedSweepSampleCount = storedSampleCount;
            }
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
            TimingReference = timingReference;
            AverageRunCount = Math.Clamp(averageRunCount, 1, 64);
            AcceptedAverageRunCount = Math.Clamp(
                acceptedAverageRunCount,
                1,
                AverageRunCount);
            LastError = null;
            Publish(ImpulseResponseChanged);
            }
            finally
            {
                if (claimedHere)
                {
                    lock (stateSync)
                    {
                        inProgress = false;
                    }
                }
            }
        }

        /// <summary>
        /// Publishes a sweep recorded OUTSIDE Resonalyze as this measurement's
        /// result. The sweep <paramref name="configuration"/> describes — the same
        /// signal the options panel exports as a WAV file — stands in for the
        /// loopback reference: the excitation is known exactly, because the
        /// recording was made by playing that very signal. From there the analysis
        /// is the live one: deconvolution against the inverse filter, and the H1
        /// transfer estimate gated to the sweep's excitation band.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The configuration is applied only once the recording has been analyzed
        /// and accepted, so a rejected import leaves the measurement currently on
        /// screen alone — the same promise loading a file makes.
        /// </para>
        /// <para>
        /// Only the excitation and its decay are analyzed, not the whole file: a
        /// recorder is usually started well before the sweep is played and stopped
        /// well after, and every FFT here is sized by what it is handed. Locating
        /// the excitation by level cannot be certain — a passage of speech before
        /// the sweep is loud and sustained too — so the candidates are analyzed in
        /// order until one produces a credible impulse response. When a take holds
        /// the sweep more than once, the first one is the one measured.
        /// </para>
        /// <para>
        /// Such a result carries no absolute time. Where the arrival lands is
        /// decided by when the recorder was started, not by the acoustic path, so
        /// the delay read-outs mean something only within one file — two imported
        /// recordings cannot be time-aligned against each other the way two
        /// loopback-referenced runs can. It carries no SPL anchor either: the gain
        /// of the recording chain is unknown, and <see cref="Init"/> clears
        /// <see cref="MeasurementSplCalibration"/> for this result.
        /// </para>
        /// </remarks>
        /// <summary>
        /// The same import over a multi-channel recording, measuring the channel
        /// that best MATCHES the sweep (see <see cref="RecordedSweepChannels"/>).
        /// Only for callers with no one to ask: when more than one channel
        /// plausibly holds the sweep the best match is not the answer — a DAW's
        /// reference track is a copy of the excitation and beats the microphone
        /// every time — so the UI ranks the channels itself and imports the one
        /// the user picks. The chosen one is reported through
        /// <see cref="ImportedChannelIndex"/>.
        /// </summary>
        public void ImportRecordedSweep(
            SweepMeasurementConfiguration configuration,
            float[][] channels,
            int sampleRate)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(channels);
            if (channels.Length == 0)
            {
                throw new InvalidOperationException("The recording has no channels.");
            }

            ImportRecordedSweep(
                configuration,
                channels,
                sampleRate,
                channels.Length > 1
                    ? RecordedSweepChannels.Best(
                        RecordedSweepChannels.Rank(configuration, channels))
                    : 0);
        }

        /// <summary>
        /// The import over a named channel of a multi-channel recording.
        /// </summary>
        public void ImportRecordedSweep(
            SweepMeasurementConfiguration configuration,
            float[][] channels,
            int sampleRate,
            int channel)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(channels);
            if (channels.Length == 0)
            {
                throw new InvalidOperationException("The recording has no channels.");
            }
            ArgumentOutOfRangeException.ThrowIfNegative(channel);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, channels.Length);

            ImportRecordedSweep(configuration, channels[channel], sampleRate);
            ImportedChannelIndex = channel;
        }

        public void ImportRecordedSweep(
            SweepMeasurementConfiguration configuration,
            float[] recordedSamples,
            int sampleRate)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(recordedSamples);
            // Held for the WHOLE import, not just checked on entry. The analysis
            // runs off the UI thread, and every other way to reconfigure this
            // measurement — the record button, the settings panel's Apply — is a
            // click away and gates on exactly this flag. Without it, a click
            // landing mid-import re-Inits the measurement underneath the analysis
            // or has its fresh run overwritten by the import's result.
            //
            // A caller that already holds a Claim (the file import, which took one
            // before it started decoding) keeps it: the busy state has to span the
            // decode as well, and re-taking it here would refuse the very operation
            // that owns it.
            bool claimedHere = false;
            lock (stateSync)
            {
                if (inProgress && !claimed)
                {
                    throw new InvalidOperationException(
                        "Cannot import a recording while a measurement is running.");
                }
                if (!inProgress)
                {
                    inProgress = true;
                    claimedHere = true;
                }
            }
            try
            {
                ImportRecordedSweepCore(configuration, recordedSamples, sampleRate);
            }
            finally
            {
                if (claimedHere)
                {
                    lock (stateSync)
                    {
                        inProgress = false;
                    }
                }
            }
        }

        private void ImportRecordedSweepCore(
            SweepMeasurementConfiguration configuration,
            float[] recordedSamples,
            int sampleRate)
        {
            SweepSignalConfiguration signal = configuration.Signal;
            if (sampleRate != signal.SampleRate)
            {
                throw new InvalidOperationException(
                    $"The recording is {sampleRate} Hz while the measurement is configured " +
                    $"for {signal.SampleRate} Hz. The sweep it would be deconvolved against " +
                    "is generated at the configured rate, so the two do not describe the " +
                    "same signal. Set the sample rate in Measurement Options to match the file.");
            }

            // Generated here rather than read off this.Sweep: the configuration is
            // not applied until the analysis has succeeded, so at this point the
            // measurement still holds the previous result and its sweep.
            using var sweep = new ExponentialSineSweep();
            sweep.FillData(
                signal.LowFrequencyHz,
                signal.HighFrequencyHz,
                signal.RequestedDurationSeconds,
                signal.Bits,
                signal.SampleRate);
            if (recordedSamples.Length < sweep.SweepSamples)
            {
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"The recording is {recordedSamples.Length / (double)sampleRate:0.00} s long while the sweep is {sweep.ComputedDuration:0.00} s, so it cannot hold the whole excitation. Check the band and the per-octave time in Measurement Options against the sweep this file was recorded from."));
            }

            // Every FFT below is sized by what it is handed, so the silence a
            // recorder leaves around the excitation would decide the cost of the
            // analysis and the length of the transfer IR it produces. Analyze the
            // excitation and its decay instead — trying the candidate stretches in
            // turn, because locating one by level alone cannot be certain.
            double bestCompactnessDb = double.NegativeInfinity;
            double bestSharpnessDb = double.NegativeInfinity;
            bool measurable = false;
            int longestExcitation = 0;
            IReadOnlyList<string>? captureIssues = null;
            foreach (RecordedSweepSpan span in RecordedSweepWindow.LocateCandidates(
                recordedSamples, sweep.SweepData, sampleRate))
            {
                // Measured from where the EXCITATION begins, not from the start of
                // the span: the span counts its lead-in silence too, so a take that
                // holds a pre-roll and then runs out mid-sweep would read as long
                // enough and be analyzed against an excitation it never finished.
                // No slack: the sweep is located by matching it, so its start is a
                // sample rather than the moment a level crossed a threshold.
                longestExcitation = Math.Max(longestExcitation, span.ExcitationLength);
                if (span.ExcitationLength < sweep.SweepSamples)
                {
                    // Found too close to the end to hold the whole sweep; a later
                    // candidate may still fit.
                    continue;
                }

                // The recording and the configured sweep can be slightly out of
                // scale with each other — separate crystals, or a duration the
                // per-octave field cannot express. Find the stretch that sharpens
                // the arrival most, then analyze against a reference rebuilt there.
                double timeScalePpm = EstimateTimeScalePpm(recordedSamples, span, sweep, sampleRate);
                using var reference = new ExponentialSineSweep();
                if (timeScalePpm != 0)
                {
                    reference.FillStretched(sweep.Spec, 1.0 + timeScalePpm * 1e-6, signal.Bits);
                }
                ImportedSweepAnalysis analysis = AnalyzeImportedSpan(
                    recordedSamples, span, timeScalePpm == 0 ? sweep : reference);

                // The same unambiguous capture failures the live path refuses a run
                // for. A clipped sweep still deconvolves into a compact impulse
                // response — it is simply full of harmonic products — so the shape
                // gate below cannot be what catches it.
                IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
                    analysis.Analyzed, loopback: null, sweep.SweepSamples);
                if (issues.Count > 0)
                {
                    captureIssues ??= issues;
                    continue;
                }
                TransferIrCompactness? compactness = TransferIrDiagnostics.MeasureCompactness(
                    analysis.TransferImpulseResponse, signal.SampleRate);
                double? sharpness = TransferIrDiagnostics.MeasureArrivalSharpnessDb(
                    analysis.TransferImpulseResponse, signal.SampleRate);
                if (compactness is { } value &&
                    double.IsFinite(value.InsideOutsideDb) &&
                    sharpness is { } arrival && double.IsFinite(arrival))
                {
                    measurable = true;
                    bestCompactnessDb = Math.Max(bestCompactnessDb, value.InsideOutsideDb);
                    bestSharpnessDb = Math.Max(bestSharpnessDb, arrival);
                    if (value.InsideOutsideDb >= TransferIrDiagnostics.MinimumCompactnessDb &&
                        arrival >= TransferIrDiagnostics.MinimumArrivalSharpnessDb)
                    {
                        PublishImportedSweep(configuration, analysis, timeScalePpm);
                        return;
                    }
                }
            }

            if (longestExcitation < sweep.SweepSamples)
            {
                // Two readings fit this, and the data does not choose between
                // them: a normalized match separates a clean take (1.0) from
                // noise (0.02) easily, but a noisy real take reads 0.06 and a
                // recording of a DIFFERENT sweep reads 0.21 — they overlap. So
                // the message names both rather than picking one on a threshold
                // that would be wrong as often as right.
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"The excitation was found only {longestExcitation / (double)sampleRate:0.00} s before the end of the recording, less than the sweep's own {sweep.ComputedDuration:0.00} s. Either the take is cut short — record again with the whole sweep inside it — or this is not a recording of this sweep, in which case check the band and the per-octave time in Measurement Options."));
            }
            if (captureIssues is { Count: > 0 })
            {
                throw new InvalidOperationException(
                    $"The recording cannot be measured: {string.Join("; ", captureIssues)}. " +
                    "Record again at a level that leaves headroom.");
            }

            throw RefuseImportedRecording(
                measurable, bestCompactnessDb, bestSharpnessDb, sweep, sampleRate);
        }

        private sealed record ImportedSweepAnalysis(
            float[] Analyzed,
            Complex[] SweepImpulseResponse,
            int SweepPeakIndex,
            Complex[] TransferImpulseResponse,
            int TransferPeakIndex,
            double[]? TransferCoherence);

        /// <summary>
        /// How far out of scale the search may believe a recording is, in parts
        /// per million. It has to cover both causes: two consumer crystals sit tens
        /// of ppm apart, and a duration the per-octave field cannot express exactly
        /// costs a few hundred (489 on the field pair this was calibrated against).
        /// Wide enough for both, and far short of a neighbouring sweep rate, which
        /// is percent away rather than ppm — the search must not be able to walk
        /// into one and call it a fit.
        /// </summary>
        private const double MaximumTimeScalePpm = 800.0;

        // Below this, a "better" fit is the metric's own noise: on a field take the
        // sharpness wandered by a few tenths of a dB across +-50 ppm, where the
        // arrival is already as sharp as the tract allows.
        private const double MeaningfulScaleGainDb = 0.5;

        // Where the search stops refining. Twelve ppm is a hundredth of a sample
        // over a four-second sweep — past the point where the objective still
        // carries information about the scale rather than about the room.
        private const double FinestScaleStepPpm = 12.5;

        // The coarse scan's step. The objective rises over roughly 200 ppm either
        // side of the truth, so a hundred cannot step over the peak, and the whole
        // scan plus its refinement is about two dozen deconvolutions of the
        // analyzed window — a second or two on an import, which happens once.
        private const double CoarseScaleStepPpm = 100.0;

        /// <summary>
        /// How far out of scale the recording is with the configured sweep, in
        /// ppm: the stretch of the reference that makes the deconvolved arrival
        /// sharpest. Zero when no stretch is meaningfully better than none — the
        /// honest answer both when the two really agree and when the measure is
        /// only wandering inside its own noise.
        /// </summary>
        /// <remarks>
        /// Searched on the sweep deconvolution alone: half the cost of a full
        /// analysis, and it is the deconvolution that a scale mismatch smears. The
        /// objective is the arrival's SHARPNESS rather than its height, so a
        /// recording whose level wanders cannot tilt the search.
        /// </remarks>
        private static double EstimateTimeScalePpm(
            float[] recordedSamples,
            RecordedSweepSpan span,
            ExponentialSineSweep sweep,
            int sampleRate)
        {
            float[] analyzed = span.Start == 0 && span.Length == recordedSamples.Length
                ? recordedSamples
                : recordedSamples[span.Start..(span.Start + span.Length)];
            using var stretched = new ExponentialSineSweep();

            double Sharpness(double ppm)
            {
                float[] inverse;
                if (ppm == 0)
                {
                    inverse = sweep.InverseFilter;
                }
                else
                {
                    stretched.FillStretched(sweep.Spec, 1.0 + ppm * 1e-6, sweep.BitsPerSample);
                    inverse = stretched.InverseFilter;
                }

                SweepDeconvolutionResult deconvolved = SweepAnalysis.DeconvolveWithInverseFilter(
                    analyzed, inverse, 2.0 / inverse.Length);
                return TransferIrDiagnostics.MeasureArrivalSharpnessDb(
                    Array.ConvertAll(deconvolved.ImpulseResponse, x => new Complex(x, 0.0)),
                    sampleRate) ?? double.NegativeInfinity;
            }

            // A COARSE SCAN of the whole range first, then refinement around the
            // winner. Walking downhill from zero does not work: the objective dips
            // on the way to its peak — for a recording 500 ppm out of scale, the
            // 200 ppm probe reads WORSE than no correction at all — so a search
            // that only steps while it improves stops before it has started.
            double baseline = Sharpness(0);
            double bestPpm = 0;
            double best = baseline;
            for (double ppm = -MaximumTimeScalePpm;
                ppm <= MaximumTimeScalePpm;
                ppm += CoarseScaleStepPpm)
            {
                if (ppm == 0)
                {
                    continue;
                }

                double sharpness = Sharpness(ppm);
                if (sharpness > best)
                {
                    best = sharpness;
                    bestPpm = ppm;
                }
            }

            for (double step = CoarseScaleStepPpm / 2; step >= FinestScaleStepPpm; step /= 2)
            {
                foreach (double ppm in new[] { bestPpm - step, bestPpm + step })
                {
                    if (Math.Abs(ppm) > MaximumTimeScalePpm)
                    {
                        continue;
                    }

                    double sharpness = Sharpness(ppm);
                    if (sharpness > best)
                    {
                        best = sharpness;
                        bestPpm = ppm;
                    }
                }
            }

            return best - baseline >= MeaningfulScaleGainDb ? bestPpm : 0.0;
        }

        private static ImportedSweepAnalysis AnalyzeImportedSpan(
            float[] recordedSamples,
            RecordedSweepSpan span,
            ExponentialSineSweep sweep)
        {
            float[] analyzed = span.Start == 0 && span.Length == recordedSamples.Length
                ? recordedSamples
                : recordedSamples[span.Start..(span.Start + span.Length)];

            SweepDeconvolutionResult deconvolved = SweepAnalysis.DeconvolveWithInverseFilter(
                analyzed,
                sweep.InverseFilter,
                2.0 / sweep.InverseFilter.Length);

            // The reference is the sweep laid at the START of a stretch as long as
            // the analyzed window. The estimator truncates both signals to the
            // shorter one, so handing it the bare sweep would cut the recording
            // down to the sweep's own length and throw away everything the room did
            // after the excitation stopped. Both sides are views rather than
            // buffers: the estimator fills its own FFT arrays from them, and a
            // materialized copy of each would be a second full-length signal beside
            // spectra that already dominate the import.
            TransferEstimateResult transfer = TransferFunction.ComputeAveragedRelativeIr(
                [new TransferFunctionFrame(
                    new PaddedExcitationView(sweep.SweepData, analyzed.Length),
                    new RecordedSamplesView(analyzed))],
                BuildExcitationGate(sweep));

            return new ImportedSweepAnalysis(
                analyzed,
                Array.ConvertAll(
                    deconvolved.ImpulseResponse,
                    sample => new Complex(sample, 0.0)),
                deconvolved.PeakIndex,
                Array.ConvertAll(
                    transfer.ImpulseResponse,
                    sample => new Complex(sample, 0.0)),
                transfer.PeakIndex,
                transfer.Coherence);
        }

        /// <summary>
        /// Where an imported measurement's arrival is placed on the time axis.
        /// <para>
        /// Its raw position is the recorder's start offset — 730 ms on one field
        /// take, 1220 ms on another — and every absolute read-out inherits that:
        /// measured group delay is referenced to the IR start, so those takes read
        /// 730 ms and 1220 ms of group delay on an axis that spans tens. Since the
        /// origin means nothing, it is chosen rather than inherited, and the whole
        /// IR is rotated to put the arrival here. Delays WITHIN the measurement are
        /// untouched — a rigid shift moves every reflection with the direct sound —
        /// while the reading becomes what it always was: time relative to this
        /// measurement's own arrival.
        /// </para>
        /// <para>
        /// Not zero: the honest start of an arrival sits a little before its peak
        /// (a low-frequency front can build for milliseconds), and at zero that
        /// front would wrap to the far end of the buffer, where the automatic gate
        /// would read it as a delay of nearly the whole record.
        /// </para>
        /// </summary>
        private const double ImportedArrivalSeconds = 0.010;

        private void PublishImportedSweep(
            SweepMeasurementConfiguration configuration,
            ImportedSweepAnalysis analysis,
            double timeScalePpm)
        {
            InitCore(configuration);
            // Init set the measured meaning; this result has the imported one.
            TimingReference = TimingReference.RecordedSweep;
            ImportedTimeScalePpm = timeScalePpm == 0 ? null : timeScalePpm;
            int arrival = Math.Min(
                (int)Math.Round(ImportedArrivalSeconds * SampleRate),
                analysis.TransferImpulseResponse.Length - 1);
            Complex[] transfer = RotateTo(
                analysis.TransferImpulseResponse, analysis.TransferPeakIndex, arrival);
            // No loopback entry: the reference is a generated signal, so metering it
            // would report an input level for an input that recorded nothing.
            ApplyAverageResult(new SweepAverageResult(
                analysis.SweepImpulseResponse,
                analysis.SweepPeakIndex,
                transfer,
                arrival,
                analysis.TransferCoherence,
                analysis.Analyzed,
                LoopbackRecordedSamples: null,
                CreateFinalLevelSnapshot([analysis.Analyzed], 0, loopbackIndex: null),
                AcceptedRunCount: 1,
                MicrophoneDistortion: null,
                LoopbackDistortion: null,
                LoopbackWorstRun: null,
                // An imported recording is one microphone by construction: it was
                // made outside Resonalyze, and nothing in the file says anything
                // about further positions.
                ArrayMicrophones: []));
        }

        /// <summary>
        /// Pairs the measured array curves with the notes and calibrations they were
        /// configured with, by channel.
        /// </summary>
        /// <remarks>
        /// By channel and not by position in the list: a microphone that failed every
        /// run is absent from the measured curves, so the two lists are not the same
        /// length and pairing them by index would hand each surviving microphone the
        /// next one's calibration.
        /// </remarks>
        private IReadOnlyList<ArrayMicrophoneCurve> AttachArrayMetadata(
            IReadOnlyList<ArrayMicrophoneCurve> microphones)
        {
            if (microphones.Count == 0)
            {
                return microphones;
            }

            var attached = new List<ArrayMicrophoneCurve>(microphones.Count);
            foreach (ArrayMicrophoneCurve microphone in microphones)
            {
                if (microphone.IsMeasurementMicrophone)
                {
                    attached.Add(microphone with
                    {
                        Calibration = MeasurementMicrophoneCalibration
                    });
                    continue;
                }

                ArrayMicrophoneMetadata? metadata = measurementArrayMetadata.FirstOrDefault(
                    candidate => candidate.ChannelOffset == microphone.ChannelOffset);
                attached.Add(microphone with
                {
                    Note = metadata?.Note,
                    Calibration = metadata?.Calibration
                });
            }

            return attached;
        }

        // A circular rotation that carries `from` to `to`. Circular because the
        // transfer IR already is: its acausal pre-ringing lives at the far end of
        // the buffer, and a rotation that dropped samples off one edge would cut
        // exactly that.
        private static Complex[] RotateTo(Complex[] impulseResponse, int from, int to)
        {
            int shift = from - to;
            if (shift == 0)
            {
                return impulseResponse;
            }

            int length = impulseResponse.Length;
            var rotated = new Complex[length];
            for (int i = 0; i < length; i++)
            {
                rotated[i] = impulseResponse[((i + shift) % length + length) % length];
            }

            return rotated;
        }

        // The imported counterpart of RequireCredibleTransferIr: the same shape
        // gate, but none of its diagnosis applies — nothing was wired, levelled or
        // recorded through Resonalyze here. What a shapeless transfer IR means for
        // an import is that the file is not a recording of THIS sweep. The figure
        // quoted is the best any candidate stretch reached.
        private static InvalidOperationException RefuseImportedRecording(
            bool measurable,
            double bestCompactnessDb,
            double bestSharpnessDb,
            ExponentialSineSweep sweep,
            int sampleRate)
        {
            if (!measurable)
            {
                return new InvalidOperationException(FormattableString.Invariant(
                    $"The recording did not deconvolve into an impulse response whose shape could be measured at all: it is degenerate, or carries non-finite samples. The sweep it was analyzed against runs {sweep.ComputedDuration:0.00} s at {sampleRate} Hz."));
            }

            // WHICH gate failed decides what to say. Quoting the compactness figure
            // for a sharpness failure is worse than useless: it reads "35 dB, where
            // a real measurement reads 29-49" — a number inside the range it is
            // being blamed for missing.
            if (bestCompactnessDb >= TransferIrDiagnostics.MinimumCompactnessDb)
            {
                return new InvalidOperationException(FormattableString.Invariant(
                    $"The recording deconvolves into a smeared arrival rather than an impulse response: its peak stands only {bestSharpnessDb:0.0} dB above the {TransferIrDiagnostics.ArrivalWindowSeconds * 1000:0} ms around it, where a real measurement reads 11-16 dB. The sweep in the file is not the one the settings describe — check the band and the per-octave time in Measurement Options against the sweep it was recorded from."));
            }

            return new InvalidOperationException(FormattableString.Invariant(
                $"The recording did not deconvolve into a credible impulse response: the energy around its peak is only {bestCompactnessDb:0.0} dB above the rest of the recording, at best (a real measurement reads 29-49 dB). It is most likely not a recording of this sweep — check that the band, the per-octave time and the sample rate in Measurement Options are the ones the sweep was generated with."));
        }

        internal void RestoreLevelSnapshot(InputLevelMeterSnapshot snapshot)
        {
            ThrowIfDisposed();
            CurrentLevels = snapshot;
            RaiseLevels(snapshot);
        }

        private async Task<bool> RunCoreAsync(CancellationToken cancellationToken)
        {
            // When the sweep RUNS, not when the measurement was configured. One
            // configuration serves every press of Record, so stamping it at Init gave
            // every result the same time — and the composition warning reads this date
            // to let a user notice that two channels came from different sittings.
            MeasuredAtUtc = DateTimeOffset.UtcNow;
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

                // The transfer estimator masks bins by the sweep geometry: zero
                // weight outside the ACHIEVED band (the sweep never went there),
                // full weight inside the REQUESTED band, ramps across the fade
                // guard bands between them. The ramps must sit inside the excited
                // fades — a ramp below the achieved edge half-passes unexcited
                // bins, which shows as garbage spikes just under the sweep start.
                var accumulator = new SweepAverageAccumulator(
                    BuildExcitationGate(sweep),
                    SampleRate,
                    ProtectiveHighPass,
                    ActiveMicrophoneChannelOffset,
                    ArrayInputChannelOffsets);
                var rejections = new List<SweepRunRejection>();
                // The one rejected capture, kept only to phrase the refusal below.
                AudioCaptureResult? rejectedCapture = null;
                int requestedRuns = AverageRunCount;
                for (int run = 1; run <= requestedRuns; run++)
                {
                    Publish(AverageProgressChanged, new SweepAverageProgress(
                        run,
                        requestedRuns,
                        accumulator.AcceptedRuns));
                    AudioCaptureResult? captured = await CaptureOneAsync().ConfigureAwait(false);
                    IReadOnlyList<string> issues = AssessRunQuality(captured, sweep);
                    if (issues.Count > 0)
                    {
                        // A bad run STOPS the measurement. There used to be one
                        // automatic retry, and the field answer is that it never
                        // recovered anything: what these checks catch is a gain set
                        // wrong, a cable in the wrong socket, a channel that is not
                        // there — configuration, which the next sweep reproduces
                        // exactly. Sweeping again to prove it wastes the user's time
                        // twice over, and dropping the run instead would leave an
                        // average quietly built on fewer runs than were asked for.
                        rejections.Add(new SweepRunRejection(run, issues));
                        rejectedCapture = captured;
                        break;
                    }

                    accumulator.Add(AnalyzeCapturedRun(captured, sweep));
                }

                QualityReport = new SweepRunQualityReport(
                    requestedRuns,
                    accumulator.AcceptedRuns,
                    rejections);
                if (rejections.Count > 0 || accumulator.AcceptedRuns == 0)
                {
                    // A whole measurement that failed on SHAPE is the bad-loopback
                    // case, and the diagnosis for it knows things a run issue cannot:
                    // that a quiet loopback means bleed instead of the wire, and which
                    // channel's distortion is the culprit. Now that a run can be
                    // rejected for shape, that diagnosis would otherwise be lost
                    // exactly where it is most wanted — so one rejected capture is
                    // analysed for it, at a point where nothing else is going to
                    // happen anyway.
                    DiagnoseTotalFailure(rejectedCapture, sweep, rejections);
                    throw new InvalidOperationException(
                        (rejections.Count > 0
                            ? $"Sweep run {rejections[0].Run} of {requestedRuns} failed " +
                                "the capture quality checks: "
                            : "Every sweep run failed the capture quality checks: ") +
                        string.Join(
                            "; ",
                            rejections.SelectMany(rejection => rejection.Issues).Distinct()) +
                        ". Check the input levels and the loopback wiring, then measure again.");
                }

                SweepAverageResult averageResult = accumulator.BuildResult();
                RequireCredibleTransferIr(averageResult);
                // After the refusal, never inside it: the other caller of that check
                // is the total-failure diagnosis, which describes a capture nothing
                // is going to publish.
                ResultCaution = DescribeResultCaution(averageResult);
                ApplyAverageResult(averageResult);
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
                    inProgress = false;
                }
                Publish(Completed, success);
            }

            return success;
        }

        // The requested band (this.LowFrequencyHz/HighFrequencyHz) is fully
        // excited; the achieved band (sweep.*) additionally covers the fade
        // guard bands. Clamps keep the gate ordered even when a short sweep
        // could not open a guard band on some side.
        // Read off the sweep, not off this.SampleRate: the two are the same for a
        // live run, but an import builds its gate before the configuration behind
        // the sweep has been applied.
        private static ExcitationBandGate BuildExcitationGate(ExponentialSineSweep sweep)
        {
            double nyquist = sweep.SampleRate / 2.0;
            if (!(nyquist > 0))
            {
                return ExcitationBandGate.FullBand;
            }

            // Where the envelope actually opens and closes, not where it was asked
            // to: a fade padded to its minimum length keeps rising past the
            // requested edge, and the ramps have to follow it or they pass bins
            // the sweep only grazed.
            ExpSweepSpec spec = sweep.Spec;
            double lowZero = Math.Clamp(sweep.LowFrequencyHz / nyquist, 0.0, 1.0);
            double highZero = Math.Clamp(sweep.HighFrequencyHz / nyquist, 0.0, 1.0);
            double lowFull = Math.Clamp(
                spec.FullAmplitudeLowFrequencyHz / nyquist, lowZero, 1.0);
            double highFull = Math.Clamp(
                spec.FullAmplitudeHighFrequencyHz / nyquist, 0.0, highZero);
            if (!(highFull > lowFull))
            {
                lowFull = lowZero;
                highFull = highZero;
            }
            return new ExcitationBandGate(lowZero, lowFull, highFull, highZero);
        }

        /// <summary>
        /// The configured array channels, refused rather than repaired when they
        /// collide with the measurement's own inputs.
        /// </summary>
        /// <remarks>
        /// Dropping a colliding channel silently would be the worse answer: the
        /// measurement would run, produce an array one microphone short of the one
        /// the user set up, and average a listening volume it never sampled.
        /// </remarks>
        private static IReadOnlyList<int> NormalizeArrayChannels(
            IReadOnlyList<int>? channels,
            int microphoneChannel,
            int? loopbackChannel)
        {
            if (channels == null || channels.Count == 0)
            {
                return [];
            }

            var normalized = new List<int>(channels.Count);
            foreach (int channel in channels)
            {
                if (channel < 0)
                {
                    throw new InvalidOperationException(
                        "An array microphone channel cannot be negative.");
                }
                if (channel == microphoneChannel || channel == loopbackChannel)
                {
                    throw new InvalidOperationException(
                        $"Array microphone channel {channel} is already in use by the " +
                        "measurement microphone or the loopback.");
                }
                if (normalized.Contains(channel))
                {
                    throw new InvalidOperationException(
                        $"Array microphone channel {channel} is configured twice.");
                }

                normalized.Add(channel);
            }

            return normalized;
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
                expectedCaptureSamples: sweep.SweepSamples + SampleRate * 2,
                arrayInputChannelOffsets: ArrayInputChannelOffsets);

        private AudioPlaybackSignal BuildPlaybackSignal(ExponentialSineSweep sweep) =>
            new(sweep.SweepData, SampleRate, Bits, PlaybackChannel, Loop: false);

        private void HandleSessionLevels(AudioInputLevels levels)
        {
            InputLevelMeterSnapshot snapshot = InputLevelMapping.Map(levels);
            CurrentLevels = snapshot;
            RaiseLevels(snapshot);
        }

        /// <summary>
        /// Replaces the generic "every run failed" refusal with the transfer
        /// function's own diagnosis, when the failures were about its SHAPE.
        /// </summary>
        /// <remarks>
        /// Throws that diagnosis rather than returning it: the caller's own throw is
        /// the fallback for everything this cannot improve on — a capture that will
        /// not analyse at all, or runs rejected for plain level faults, where the
        /// generic sentence is already the honest one.
        /// </remarks>
        private void DiagnoseTotalFailure(
            AudioCaptureResult? capture,
            ExponentialSineSweep sweep,
            IReadOnlyList<SweepRunRejection> rejections)
        {
            if (capture == null ||
                !rejections.Any(rejection => rejection.Issues.Any(
                    issue => issue.Contains("credible response", StringComparison.Ordinal))))
            {
                return;
            }

            SweepAverageResult result;
            try
            {
                var accumulator = new SweepAverageAccumulator(
                    BuildExcitationGate(sweep),
                    SampleRate,
                    ProtectiveHighPass,
                    ActiveMicrophoneChannelOffset,
                    // No array microphones: this exists to explain the MEASUREMENT
                    // pair's shape, and building array curves would refuse on the
                    // very fault being explained.
                    []);
                // The one capture that failed, which is now the only one there is:
                // the measurement stops on it rather than sweeping again. That is also
                // what bounds this — a diagnosis whose scratch is FFT-sized must not
                // be handed a list that grows with the run count.
                accumulator.Add(
                    AnalyzeCapturedRun(capture, sweep, raiseIntermediateLevels: false));
                result = accumulator.BuildResult();
            }
            catch (Exception)
            {
                // The capture cannot be analysed at all, which the caller's own
                // refusal already describes as well as anything could.
                return;
            }

            RequireCredibleTransferIr(result);
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

            // Every array microphone, judged exactly as the measurement one is: a run
            // that compromised any of them is not a run this measurement can use.
            //
            // The alternative — drop that microphone from that run and keep the rest —
            // is what this used to do, and it buys a measurement that looks complete
            // and is not: the array keeps only the curve each position produced, so a
            // position that lost its runs is simply absent from the average, and the
            // average of six positions where seven were set up is a different
            // measurement wearing the same name. A sweep is cheap; a spatial average
            // built on a position that was never there is not.
            //
            // Level faults first, for every channel, because a clipped or silent one
            // has already failed and dividing it would add a second sentence about the
            // same fault. What survives is asked the question the level checks cannot:
            // is this a RESPONSE at all.
            var judged = new List<(string Where, float[] Samples)>();
            if (issues.Count == 0)
            {
                // The measurement microphone faces the same question as every array
                // microphone, for the same reason and with the same arithmetic. A run
                // that recorded noise instead of the sweep passes every level check,
                // hides in the H1 average — the good runs still put an arrival in the
                // total — and scales the whole measurement by the fraction of runs that
                // were good: -2.50 dB for one bad run in four, whatever the noise level
                // was. On this channel that lands on the level every other channel is
                // compared against.
                judged.Add((string.Empty, microphone));
            }

            // The capture's channel indices are relative to the first channel the
            // session opened, which for ASIO is not the hardware input the user
            // configured: a rig on inputs 6, 8 and 9 arrives here as 1, 3 and 4. The
            // SAMPLES are matched correctly either way, but a refusal that sends
            // someone to check input 4 when the fault is on input 9 is a refusal that
            // costs an afternoon. The i-th capture channel is the i-th CONFIGURED
            // microphone (see BuildArrayCaptures), so the configured offset is what
            // gets named.
            IReadOnlyList<int> configured = ArrayInputChannelOffsets;
            for (int position = 0; position < captured.ArrayChannels.Count; position++)
            {
                int channel = captured.ArrayChannels[position];
                float[] samples = (uint)channel < (uint)channels.Length
                    ? channels[channel]
                    : [];
                int named = position < configured.Count ? configured[position] : channel;
                string where = $"array microphone on input {named + 1}";
                if (samples.Length == 0)
                {
                    issues.Add($"{where}: the channel was not captured.");
                    continue;
                }

                int before = issues.Count;
                foreach (string issue in SweepRunQualityCheck.AssessArrayMicrophone(
                    samples, sweep.SweepSamples))
                {
                    issues.Add($"{where}: {issue}.");
                }

                if (issues.Count == before)
                {
                    judged.Add((where, samples));
                }
            }

            AddIncredibleResponses(issues, loopback, judged, sweep);
            return issues;
        }

        /// <summary>
        /// Asks every channel that passed its level checks whether what it recorded
        /// divides into a response, against the ONE loopback recorded beside them all.
        /// </summary>
        /// <remarks>
        /// One reference, transformed once. Judging each microphone on its own
        /// transformed the same loopback again for every one of them: measured on
        /// eight channels of a 96 kHz / 20 s take, 3993 ms one at a time against
        /// 2093 ms shared. The answers are identical bin for bin, because the
        /// excitation gate and the regularization are functions of the reference alone
        /// (<see cref="TransferFunction.MeasureSingleFrameCompactness"/>). Only the
        /// VERDICTS come back, never the responses: one of those is 64 MiB at the
        /// transform length a 96 kHz twenty-second take reaches.
        /// </remarks>
        private void AddIncredibleResponses(
            List<string> issues,
            float[]? loopback,
            IReadOnlyList<(string Where, float[] Samples)> judged,
            ExponentialSineSweep sweep)
        {
            if (loopback == null || judged.Count == 0)
            {
                return;
            }

            // The same ceiling the loopback's own diagnosis lives under, and for the
            // same reason. This transform is 2 x NextPowerOfTwo(samples), and the
            // scratch around it is FFT-sized: at 2^22 bins that is already a few
            // hundred megabytes per run, and the rates and durations the app accepts
            // reach 2^24 (384 kHz, 20 s) and 2^27 (384 kHz, 100 s), where it would ask
            // for gigabytes to answer a yes-or-no question. Above the bound the run
            // check is skipped and the level checks stand alone; the AVERAGED verdict
            // still runs, because its transform is the one the measurement needed
            // anyway and only the inverse is added to it.
            if (!RunCredibilityDiagnosisFits(loopback.Length))
            {
                return;
            }

            TransferIrCompactness?[] shapes = TransferFunction.MeasureSingleFrameCompactness(
                new RecordedSamplesView(loopback),
                judged.Select(entry =>
                    (IReadOnlyList<double>)new RecordedSamplesView(entry.Samples)).ToList(),
                BuildExcitationGate(sweep),
                SampleRate);
            double floorDb = ArrayMicrophoneAnalysis.RunFloorDb(AverageRunCount);
            for (int i = 0; i < judged.Count; i++)
            {
                if (ArrayMicrophoneAnalysis.DescribeIncredibleShape(
                    shapes[i], floorDb) is not { } shape)
                {
                    continue;
                }

                (string where, _) = judged[i];
                issues.Add(where.Length == 0
                    ? "the microphone recorded a signal, but it did not divide into " +
                        $"a credible response ({shape})"
                    : $"{where}: it recorded a signal, but it did not divide into a " +
                        $"credible response ({shape})");
            }
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
                    AudioBackend.IsWasapi()
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

            // Read the harmonic content HERE, while this run's microphone
            // deconvolution is already in hand, so the refusal path never has
            // to deconvolve anything: by then the averaged IRs AND every run's
            // retained transfer frame are still alive, which is exactly the
            // wrong moment for FFT-sized allocations. Per-run readings are
            // also what the verdict needs — the refusal judges the average,
            // and the average is built from these same runs.
            float[]? loopbackSamples = captured.LoopbackChannel is int loopbackIndex &&
                (uint)loopbackIndex < (uint)sampleChannels.Length
                    ? sampleChannels[loopbackIndex]
                    : null;
            EssHarmonicEnergy? microphoneDistortion = MeasureDistortion(() => sweepResult);
            // The loopback's own deconvolution exists ONLY for this diagnosis
            // — the microphone's is the measurement's own, reused for free —
            // so its cost stays bounded: above the size bound the hint is
            // skipped rather than adding FFT-sized scratch to every run of a
            // long sweep (see MaxLoopbackDiagnosisFftLength for the numbers).
            EssHarmonicEnergy? loopbackDistortion = loopbackSamples == null ||
                !LoopbackDiagnosisFits(loopbackSamples.Length, sweep.InverseFilter.Length)
                    ? null
                    : MeasureDistortion(() => SweepAnalysis.DeconvolveWithInverseFilter(
                        loopbackSamples,
                        sweep.InverseFilter,
                        2.0 / sweep.InverseFilter.Length));

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
                finalLevels,
                microphoneDistortion,
                loopbackDistortion,
                BuildArrayCaptures(captured, sampleChannels));
        }

        /// <summary>
        /// One accepted run's array frames, positionally — index i is the i-th
        /// configured microphone.
        /// </summary>
        /// <remarks>
        /// No verdicts here any more. A run reaches this only after
        /// <see cref="AssessRunQuality"/> found every array microphone sound, because
        /// a compromised position now fails the RUN rather than dropping quietly out
        /// of it: the array keeps only the curve each position produced, so a position
        /// that lost its runs is simply absent from the average, and an average of six
        /// positions where seven were set up is a different measurement wearing the
        /// same name.
        /// <para>
        /// No loopback, no array: an array microphone is read as a transfer function
        /// against the loopback, and a measurement without one has nothing to
        /// reference it to. That is refused for the whole measurement long before
        /// here.
        /// </para>
        /// </remarks>
        private static IReadOnlyList<TransferFunctionFrame?> BuildArrayCaptures(
            AudioCaptureResult captured,
            float[][] sampleChannels)
        {
            if (captured.ArrayChannels.Count == 0)
            {
                return [];
            }

            var frames = new TransferFunctionFrame?[captured.ArrayChannels.Count];
            if (captured.LoopbackChannel is not int loopbackIndex ||
                (uint)loopbackIndex >= (uint)sampleChannels.Length)
            {
                return frames;
            }

            for (int microphone = 0; microphone < frames.Length; microphone++)
            {
                int channel = captured.ArrayChannels[microphone];
                if ((uint)channel < (uint)sampleChannels.Length &&
                    sampleChannels[channel].Length > 0)
                {
                    frames[microphone] = new TransferFunctionFrame(
                        new RecordedSamplesView(sampleChannels[loopbackIndex]),
                        new RecordedSamplesView(sampleChannels[channel]));
                }
            }

            return frames;
        }

        /// <summary>
        /// The harmonic reading of one channel, or null when it cannot be read.
        /// Never throws, and that is why the deconvolution is passed as a thunk
        /// rather than a value: for the loopback it is work done for the
        /// diagnosis alone, so a failure inside it must cost the user a hint,
        /// never the measurement itself. The microphone's is already computed
        /// and simply handed through.
        /// </summary>
        private EssHarmonicEnergy? MeasureDistortion(Func<SweepDeconvolutionResult> deconvolve)
        {
            try
            {
                SweepDeconvolutionResult deconvolved = deconvolve();
                // The geometry that places the packets is the sweep that
                // actually ran, not the requested band — the same rule the
                // harmonic views follow (see AchievedLowFrequencyHz).
                return EssHarmonicAnalysis.MeasureHarmonicEnergy(
                    deconvolved.ImpulseResponse,
                    new EssSweepMetadata(
                        AchievedLowFrequencyHz,
                        AchievedHighFrequencyHz,
                        AchievedSweepDurationSeconds,
                        SampleRate,
                        AchievedSweepSampleCount,
                        deconvolved.PeakIndex));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // A loopback whose peak sits this far down flags the LIKELY CULPRIT
        // in the shape-gate message (a wired reference is metered near full
        // scale; the field garbage set peaked at -33..-49 dBFS). Diagnosis
        // text only, never a rejection of its own: transfer estimation is
        // scale-invariant, so a cleanly attenuated wire — the readme itself
        // says to turn the playback level well down — measures fine and must
        // not be refused on level alone.
        private const double SuspiciouslyQuietLoopbackDbFs = -30;

        // Harmonic content (packets 2..5 against the linear one) above which a
        // channel is named as distorting in the refusal. A wired loopback reads
        // far below -40 dB and a loudspeaker measured through the air tens of dB
        // down, so -26 dB (5 %) accuses nothing healthy. The field case it was
        // written for: an overdriven loopback input carried 25 % second harmonic
        // (-12 dB) while the microphone in front of the speaker read 0.24 %
        // (-52 dB) — and the loopback peaked at -14.6 dBFS, so every level check
        // and the input meter saw a perfectly normal reference.
        private const double DistortingChannelDb = -26.0;

        // Ceiling on the FFT the loopback's diagnosis deconvolution may take:
        // 2^22 complex bins are two 67 MB spectra plus a 34 MB result (~170 MB
        // of transient scratch per run), reached around a 21-second sweep at
        // 96 kHz (10 s at 192 kHz, 43 s at 48 kHz) — several times past any
        // field sweep to date (3.2 s). That cost is real, not negligible; it
        // is accepted below the bound because the measurement's own microphone
        // deconvolution is the same size and has always run per run. The
        // loopback's exists only to phrase a refusal, so above the bound the
        // reading is skipped and a refusal falls back to the level heuristic
        // and the microphone reading (which reuses the measurement's own
        // deconvolution and is never skipped).
        internal const int MaxLoopbackDiagnosisFftLength = 1 << 22;

        /// <summary>
        /// Whether the per-run credibility check's transform stays under the same
        /// ceiling the loopback's diagnosis lives under. Its H1 pads to twice the
        /// capture, so this is the length that matters rather than a convolution's.
        /// </summary>
        internal static bool RunCredibilityDiagnosisFits(int recordedSamples)
        {
            // In long, so an absurd configuration reads as "does not fit" rather than
            // overflowing into an exception that would fail the measurement for the
            // sake of its own optional check.
            long padded = (long)recordedSamples * 2;
            return recordedSamples > 0 &&
                padded <= MaxLoopbackDiagnosisFftLength &&
                DspMath.NextPowerOfTwo((int)padded) <= MaxLoopbackDiagnosisFftLength;
        }

        internal static bool LoopbackDiagnosisFits(
            int recordedSamples,
            int inverseFilterSamples)
        {
            // Summed in long: an absurd configuration must read as "does not
            // fit", not overflow into an exception that would fail the whole
            // measurement for the sake of its own optional diagnosis.
            long convolutionLength = (long)recordedSamples + inverseFilterSamples - 1;
            return convolutionLength <= MaxLoopbackDiagnosisFftLength &&
                DspMath.NextPowerOfTwo((int)convolutionLength) <=
                    MaxLoopbackDiagnosisFftLength;
        }

        // The averaged transfer IR must LOOK like an impulse response before
        // anything is published: a genuine measurement is a localized event,
        // while a capture whose reference was unusable (field case: a
        // session whose "loopback" was playback bleed instead of the wire)
        // divides into stationary noise smeared over the whole buffer. The
        // shape gate is the honest refusal for that class — scale-invariant,
        // so it cannot punish a legitimately quiet capture — and the user
        // gets the reason instead of a garbage measurement.
        private void RequireCredibleTransferIr(SweepAverageResult result)
        {
            if (result.TransferImpulseResponse is not { } transfer)
            {
                return;
            }

            // FAIL-CLOSED: a shape that cannot be measured at all (null —
            // degenerate or non-finite content) is a refusal, not a pass. A
            // NaN anywhere in the capture would otherwise sail through every
            // comparison and publish an unusable measurement.
            TransferIrCompactness? compactness =
                TransferIrDiagnostics.MeasureCompactness(transfer, SampleRate);
            if (compactness is { } measured &&
                double.IsFinite(measured.InsideOutsideDb) &&
                measured.InsideOutsideDb >= TransferIrDiagnostics.MinimumCompactnessDb)
            {
                return;
            }

            InputLevelMeterEntry loopback = result.Levels.Loopback;
            string levelDiagnosis =
                loopback.Available &&
                loopback.PeakDbFs < SuspiciouslyQuietLoopbackDbFs
                    ? FormattableString.Invariant(
                        $" The loopback peaked at {loopback.PeakDbFs:0.0} dBFS while a wired reference sits near full scale — the input likely picked up bleed instead of the wire.")
                    : "";
            string shapeDiagnosis =
                compactness is { } value && double.IsFinite(value.InsideOutsideDb)
                    ? FormattableString.Invariant(
                        $"the energy around its peak is only {value.InsideOutsideDb:0.0} dB above the rest of the capture (a real measurement reads 29-49 dB; an unusable reference divides into noise well below {TransferIrDiagnostics.MinimumCompactnessDb:0} dB)")
                    : "its shape could not be measured at all (the capture is degenerate or carries non-finite samples)";
            // A named culprit replaces the generic advice; the two never both
            // apply, and the generic line is what left the field session
            // checking wiring that was correct all along.
            string distortionDiagnosis = DescribeDistortion(result);
            string advice = distortionDiagnosis.Length > 0
                ? distortionDiagnosis
                : " Check the microphone and loopback wiring and levels, then measure again.";
            throw new InvalidOperationException(
                $"The transfer function did not form a credible impulse response: {shapeDiagnosis}.{levelDiagnosis}{advice}");
        }

        /// <summary>
        /// The published result's pre-arrival reading, or null when this record
        /// cannot be given one — the excitation opened no guard band wide enough to
        /// tell the estimator's own kernel from the fault, or the record is too
        /// short to hold the window.
        /// </summary>
        private double? MeasurePreArrival(Complex[] transfer)
        {
            // Null is "no reading", not "bad record": nothing is refused on this
            // measure, and content that cannot be measured at all was already
            // turned away by the compactness check above.
            return TransferIrDiagnostics.CanJudgePreArrival(ExcitationGate())
                ? TransferIrDiagnostics.MeasurePreArrivalDb(transfer, SampleRate)
                : null;
        }

        /// <summary>
        /// The gate the current sweep excites through, or the full band when there
        /// is no sweep to read it off.
        /// </summary>
        private ExcitationBandGate ExcitationGate() => Sweep is { } sweep
            ? BuildExcitationGate(sweep)
            : ExcitationBandGate.FullBand;

        /// <summary>
        /// What the published result has to say about itself, or null when it has
        /// nothing. Never a refusal — see <see cref="SweepResultCaution"/>.
        /// </summary>
        private SweepResultCaution? DescribeResultCaution(SweepAverageResult result)
        {
            if (result.TransferImpulseResponse is not { } transfer)
            {
                return null;
            }

            return MeasurePreArrival(transfer) is { } preArrivalDb &&
                preArrivalDb > TransferIrDiagnostics.SuspectPreArrivalDb
                ? new SweepResultCaution(preArrivalDb)
                : null;
        }

        /// <summary>
        /// Names the channel whose own signal is distorting, when one is. The
        /// H1 estimate divides the microphone by the reference and so believes
        /// whatever the reference says was played: a reference driven past its
        /// input stage's limit produces a garbage transfer function while every
        /// level check passes, because analog distortion does not need full
        /// scale to happen. The figures come from the per-run readings, so what
        /// is reported covers the same runs the refused average was built from.
        /// Empty when neither channel is distorting or no run could be judged.
        /// </summary>
        private string DescribeDistortion(SweepAverageResult result)
        {
            if (result.LoopbackDistortion is { AffectedRuns: > 0 } reference)
            {
                // Every quoted companion fact comes from the run that produced
                // the worst figure, not from the aggregates: the aggregate
                // level is a maximum over runs, and juxtaposing it with a
                // different run's distortion would join facts no single
                // capture ever showed.
                WorstLoopbackRun worstRun = result.LoopbackWorstRun ??
                    new WorstLoopbackRun(null, InputLevelMeterEntry.Unavailable);
                string comparison = worstRun.MicrophoneDetectedDb is { } microphone
                    ? FormattableString.Invariant(
                        $", where the same run's microphone reads {microphone:0.0} dB")
                    : "";
                string meterNote = worstRun.LoopbackLevel.Available &&
                    worstRun.LoopbackLevel.PeakDbFs < -1.0
                        ? FormattableString.Invariant(
                            $", and on that run it peaked at only {worstRun.LoopbackLevel.PeakDbFs:0.0} dBFS, so the input meter had nothing to show")
                        : "";
                string microphoneToo = result.MicrophoneDistortion is { AffectedRuns: > 0 } acousticToo
                    ? FormattableString.Invariant(
                        $" The microphone path crossed the distortion threshold as well ({acousticToo.WorstDb:0.0} dB at worst) — fix the reference first: every analysis is divided by it.")
                    : "";
                return FormattableString.Invariant(
                    $" The LOOPBACK REFERENCE is distorting: its harmonic packets read {reference.WorstDb:0.0} dB relative to the direct one{DescribeSpread(reference, result.AcceptedRunCount)}{comparison}{meterNote}. That is what an input driven past its limit does, and the transfer function divides the microphone by it.{microphoneToo} Attenuate what reaches the loopback input — a line input instead of an instrument one, a pad in the loopback cable, or a lower playback level — and measure again. Attenuate only as far as it takes to leave the input's linear region: the transfer estimate is scale-invariant, but a reference driven down toward the input's own noise floor pays for it in coherence.");
            }

            if (result.MicrophoneDistortion is { AffectedRuns: > 0 } acoustic)
            {
                return FormattableString.Invariant(
                    $" The MICROPHONE PATH is distorting: its harmonic packets read {acoustic.WorstDb:0.0} dB relative to the direct one{DescribeSpread(acoustic, result.AcceptedRunCount)}, so the playback level is driving the loudspeaker, its amplifier or the microphone's own input past its limit. Turn the playback level down and measure again.");
            }

            return "";
        }

        // How the reading is distributed over an averaged measurement. A single
        // run needs no qualifier; several do, because the refusal is about their
        // average and the figure quoted is the worst one in it — "the loopback
        // distorts" reads very differently when it was one run out of eight.
        // When some runs produced no reading at all, the verdict must not be
        // stretched over them: it covers the judged runs, and the message says
        // how many of the averaged runs that was.
        private static string DescribeSpread(DistortionTally tally, int acceptedRuns)
        {
            if (acceptedRuns <= 1)
            {
                return "";
            }
            return tally.JudgedRuns == acceptedRuns
                ? FormattableString.Invariant(
                    $" at worst, on {tally.AffectedRuns} of the {acceptedRuns} averaged runs")
                : FormattableString.Invariant(
                    $" at worst, on {tally.AffectedRuns} of the {tally.JudgedRuns} judged runs ({acceptedRuns} were averaged)");
        }

        private void ApplyAverageResult(SweepAverageResult result)
        {
            ArrayMicrophones = AttachArrayMetadata(result.ArrayMicrophones);
            sweepDeconvolutionResult = new MeasurementImpulseResponse(
                result.SweepImpulseResponse,
                result.SweepPeakIndex);
            Complex[]? transferImpulseResponse = result.TransferImpulseResponse;
            int transferPeakIndex = result.TransferPeakIndex;
            double[]? transferCoherence = result.TransferCoherence;
            if (transferImpulseResponse != null && ProtectiveHighPass.Enabled)
            {
                ProtectiveHighPassCompensationResult compensation =
                    ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
                        transferImpulseResponse,
                        ProtectiveHighPass.ToEdge(),
                        SampleRate,
                        ProtectiveHighPassConfiguration.MaximumCompensationBoostDb);
                transferImpulseResponse = compensation.ImpulseResponse;
                transferCoherence = compensation.MaskCoherence(transferCoherence);
                // Removing the high-pass removes its group delay too, so its
                // corrected arrival is allowed to move on a synchronized live
                // measurement. An import is different: its peak was deliberately
                // placed at its own 10 ms origin before publication, so preserve
                // that convention by rotating the corrected arrival back there.
                int correctedPeakIndex = FindPeakIndex(transferImpulseResponse);
                if (TimingReference == TimingReference.RecordedSweep)
                {
                    transferImpulseResponse = RotateTo(
                        transferImpulseResponse,
                        correctedPeakIndex,
                        result.TransferPeakIndex);
                }
                else
                {
                    transferPeakIndex = correctedPeakIndex;
                }
            }
            transferResult = transferImpulseResponse != null
                ? new MeasurementImpulseResponse(
                    transferImpulseResponse,
                    transferPeakIndex)
                : null;
            TransferCoherence = transferCoherence;
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

            // Views, not copies: the estimator converts into its FFT buffers as it
            // fills them, so a double[] per channel would be two more full-length
            // copies of the capture for nothing.
            frame = new TransferFunctionFrame(
                new RecordedSamplesView(sampleChannels[loopbackIndex.Value]),
                new RecordedSamplesView(sampleChannels[microphoneIndex]));
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

        /// <summary>
        /// What one array microphone produced in one run: the frame to average,
        /// or the reasons it produced nothing.
        /// </summary>

        private sealed record SweepRunAnalysis(
            Complex[] SweepImpulseResponse,
            int SweepPeakIndex,
            TransferFunctionFrame? TransferFrame,
            float[][] SampleChannels,
            int MicrophoneIndex,
            int? LoopbackIndex,
            InputLevelMeterSnapshot Levels,
            EssHarmonicEnergy? MicrophoneDistortion,
            EssHarmonicEnergy? LoopbackDistortion,
            IReadOnlyList<TransferFunctionFrame?> ArrayCaptures);

        /// <summary>What one run's harmonic reading amounts to for the tally.</summary>
        internal enum DistortionVerdict
        {
            // No reading, or floors too high to confirm or exclude a
            // DistortingChannelDb-level fault — the run is not judged.
            Unjudged,
            // The reading could have found a threshold-level fault and found
            // none: every requested order was readable AND the ceiling — the
            // total energy at the packet positions — sits below the threshold.
            JudgedClean,
            Distorting
        }

        /// <summary>
        /// Classifies one run's reading against
        /// <see cref="DistortingChannelDb"/>. "Judged" means exactly that the
        /// diagnosis could have confirmed OR excluded a fault of that size:
        /// nothing detected over a loud record is no verdict, because an
        /// undetected packet can be harmonic in its entirety — the floors
        /// bound the background beside the packets, not inside them, so up to
        /// <c>floor * 10^0.6</c> of energy hides without leaving a detection.
        /// </summary>
        internal static DistortionVerdict ClassifyDistortionReading(
            EssHarmonicEnergy reading)
        {
            if (double.IsNaN(reading.CeilingDb))
            {
                return DistortionVerdict.Unjudged;
            }
            if (reading.DetectedDb is { } detected && detected >= DistortingChannelDb)
            {
                // What was found was found: an accusation stands on any
                // coverage, since the unread orders could only add to it.
                return DistortionVerdict.Distorting;
            }
            // A clean certificate needs the whole story: every requested
            // order read AND their total below the threshold. A reading that
            // could not reach the higher orders may accuse but never certify
            // — an unread order can hide anything, including a packet that
            // sits inside the record while its outer flank does not.
            return reading.CompleteCoverage && reading.CeilingDb < DistortingChannelDb
                ? DistortionVerdict.JudgedClean
                : DistortionVerdict.Unjudged;
        }

        /// <summary>
        /// One channel's harmonic readings over the accepted runs:
        /// <see cref="WorstDb"/> is the loudest detection,
        /// <see cref="AffectedRuns"/> how many runs crossed
        /// <see cref="DistortingChannelDb"/>, and <see cref="JudgedRuns"/> how
        /// many the diagnosis could confirm OR exclude such a fault on (see
        /// <see cref="ClassifyDistortionReading"/> — a floor too high to rule
        /// one out judges nothing). All three are needed to describe an
        /// averaged measurement honestly: a single bad run out of eight is a
        /// different story from all eight, and "the other seven read clean" is
        /// a different story from "the other seven could not be read".
        /// </summary>
        private readonly record struct DistortionTally(
            double WorstDb,
            int AffectedRuns,
            int JudgedRuns);

        /// <summary>
        /// The facts of the single run that produced the loopback tally's
        /// <see cref="DistortionTally.WorstDb"/>: that run's own microphone
        /// reading and its own loopback level. The refusal quotes these next
        /// to the worst figure, and they must all describe the SAME capture —
        /// the aggregate levels take the maximum over runs, so quoting them
        /// would juxtapose facts from different captures.
        /// </summary>
        private readonly record struct WorstLoopbackRun(
            double? MicrophoneDetectedDb,
            InputLevelMeterEntry LoopbackLevel);

        private sealed class DistortionAccumulator
        {
            private double worstDb = double.NegativeInfinity;
            private int affectedRuns;
            private int judgedRuns;

            /// <summary>
            /// Folds one run's reading in; true when this run now holds the
            /// worst DETECTION, so the caller can capture that run's context.
            /// </summary>
            public bool Add(EssHarmonicEnergy? reading)
            {
                if (reading is not { } value)
                {
                    return false;
                }

                switch (ClassifyDistortionReading(value))
                {
                    case DistortionVerdict.Distorting:
                        judgedRuns++;
                        affectedRuns++;
                        if (value.DetectedDb!.Value > worstDb)
                        {
                            worstDb = value.DetectedDb.Value;
                            return true;
                        }
                        break;
                    case DistortionVerdict.JudgedClean:
                        judgedRuns++;
                        worstDb = Math.Max(
                            worstDb,
                            value.DetectedDb ?? double.NegativeInfinity);
                        break;
                }
                return false;
            }

            // Null when no run could be judged at all, which is not the same as
            // every run reading clean.
            public DistortionTally? ToTally() =>
                judgedRuns == 0
                    ? null
                    : new DistortionTally(worstDb, affectedRuns, judgedRuns);
        }

        private sealed record SweepAverageResult(
            Complex[] SweepImpulseResponse,
            int SweepPeakIndex,
            Complex[]? TransferImpulseResponse,
            int TransferPeakIndex,
            double[]? TransferCoherence,
            float[]? MicrophoneRecordedSamples,
            float[]? LoopbackRecordedSamples,
            InputLevelMeterSnapshot Levels,
            int AcceptedRunCount,
            DistortionTally? MicrophoneDistortion,
            DistortionTally? LoopbackDistortion,
            WorstLoopbackRun? LoopbackWorstRun,
            IReadOnlyList<ArrayMicrophoneCurve> ArrayMicrophones);

        private sealed class SweepAverageAccumulator
        {
            private readonly ExcitationBandGate excitationGate;
            private readonly int sampleRate;
            private readonly ProtectiveHighPassConfiguration protectiveHighPass;
            private readonly int microphoneChannelOffset;
            private readonly IReadOnlyList<int> arrayChannelOffsets;
            private readonly List<TransferFunctionFrame>[] arrayFrames;
            private readonly List<TransferFunctionFrame> transferFrames = new();
            private readonly ChannelLevelAccumulator microphoneLevels = new(fullScaleReference: false);
            private readonly ChannelLevelAccumulator loopbackLevels = new(fullScaleReference: true);
            private readonly DistortionAccumulator microphoneDistortion = new();
            private readonly DistortionAccumulator loopbackDistortion = new();
            private WorstLoopbackRun? loopbackWorstRun;
            private Complex[]? sweepSum;
            private int referencePeakIndex;
            private float[]? lastMicrophoneSamples;
            private float[]? lastLoopbackSamples;

            public SweepAverageAccumulator(
                ExcitationBandGate excitationGate,
                int sampleRate,
                ProtectiveHighPassConfiguration protectiveHighPass,
                int microphoneChannelOffset,
                IReadOnlyList<int> arrayChannelOffsets)
            {
                this.excitationGate = excitationGate;
                this.sampleRate = sampleRate;
                this.protectiveHighPass = protectiveHighPass;
                this.microphoneChannelOffset = microphoneChannelOffset;
                this.arrayChannelOffsets = arrayChannelOffsets;
                arrayFrames = new List<TransferFunctionFrame>[arrayChannelOffsets.Count];
                for (int microphone = 0; microphone < arrayChannelOffsets.Count; microphone++)
                {
                    arrayFrames[microphone] = new List<TransferFunctionFrame>();
                }
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

                for (int microphone = 0;
                    microphone < arrayFrames.Length && microphone < run.ArrayCaptures.Count;
                    microphone++)
                {
                    if (run.ArrayCaptures[microphone] is TransferFunctionFrame arrayFrame)
                    {
                        arrayFrames[microphone].Add(arrayFrame);
                    }
                }

                microphoneDistortion.Add(run.MicrophoneDistortion);
                if (loopbackDistortion.Add(run.LoopbackDistortion))
                {
                    // This run now holds the worst loopback detection; freeze
                    // ITS microphone reading and ITS loopback level, so the
                    // refusal's side-by-side quotes all describe one capture.
                    loopbackWorstRun = new WorstLoopbackRun(
                        run.MicrophoneDistortion?.DetectedDb,
                        run.Levels.Loopback);
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
                        excitationGate);
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
                    AcceptedRuns,
                    microphoneDistortion.ToTally(),
                    loopbackDistortion.ToTally(),
                    loopbackWorstRun,
                    BuildArrayMicrophones());
            }

            /// <summary>
            /// The spatial average's microphones, the measurement one first.
            /// </summary>
            /// <remarks>
            /// It leads the list because it is the anchor every other microphone
            /// is levelled onto, and it is in the list at all because it is a
            /// position in the listening volume like the others — the only one
            /// whose level is tied to the SPL calibration.
            /// </remarks>
            private IReadOnlyList<ArrayMicrophoneCurve> BuildArrayMicrophones()
            {
                if (arrayChannelOffsets.Count == 0 || transferFrames.Count == 0)
                {
                    return [];
                }

                var microphones = new List<ArrayMicrophoneCurve>(arrayChannelOffsets.Count + 1)
                {
                    new(
                        microphoneChannelOffset,
                        IsMeasurementMicrophone: true,
                        ArrayMicrophoneAnalysis.BuildMeasurementCurve(
                            transferFrames,
                            excitationGate,
                            sampleRate,
                            protectiveHighPass),
                        transferFrames.Count)
                };
                for (int microphone = 0; microphone < arrayChannelOffsets.Count; microphone++)
                {
                    List<TransferFunctionFrame> frames = arrayFrames[microphone];
                    if (frames.Count == 0)
                    {
                        continue;
                    }

                    microphones.Add(new ArrayMicrophoneCurve(
                        arrayChannelOffsets[microphone],
                        IsMeasurementMicrophone: false,
                        ArrayMicrophoneAnalysis.BuildArrayCurve(
                            frames,
                            excitationGate,
                            sampleRate,
                            protectiveHighPass,
                            arrayChannelOffsets[microphone]),
                        frames.Count));
                }

                return microphones;
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
    int AcceptedRuns);
