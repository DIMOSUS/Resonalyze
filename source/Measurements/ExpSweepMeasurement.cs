using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze
{
    /// <summary>
    /// Sweep playback, recording and deconvolution policy; the device lifecycle lives behind <see cref="IAudioSessionFactory"/>.
    /// Holds the configuration of the next run only: a run or an import hands its <see cref="MeasurementResult"/> out.
    /// </summary>
    public sealed class ExpSweepMeasurement : IDisposable
    {
        private readonly IAudioSessionFactory audioSessionFactory;
        private readonly object stateSync = new();
        private CancellationTokenSource? cancellationTokenSource;
        private Task<MeasurementResult?>? measurementTask;
        private volatile bool inProgress;
        private bool disposed;

        /// <summary>Null when the run failed or was aborted (<see cref="LastError"/> says which).</summary>
        internal event Action<MeasurementResult?>? Completed;
        public event Action<SweepAverageProgress>? AverageProgressChanged;
        internal event Action<InputLevelMeterSnapshot>? LevelsAvailable;

        public ExpSweepMeasurement(IAudioSessionFactory audioSessionFactory)
        {
            this.audioSessionFactory = audioSessionFactory ??
                throw new ArgumentNullException(nameof(audioSessionFactory));
        }

        public ExponentialSineSweep? Sweep { get; private set; }

        /// <summary>A run holds the device; an import never touches the engine.</summary>
        public bool InProgress => inProgress;
        public int SampleRate { get; private set; }
        public double LowFrequencyHz { get; private set; }
        public double HighFrequencyHz { get; private set; }
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

        public IReadOnlyList<int> ArrayInputChannelOffsets =>
            AudioBackend == AudioBackend.Asio
                ? AsioArrayInputChannelOffsets
                : WaveArrayInputChannelOffsets;

        public int AverageRunCount { get; private set; } = 1;
        public ProtectiveHighPassConfiguration ProtectiveHighPass { get; private set; } =
            ProtectiveHighPassConfiguration.Off;

        /// <summary>Configured measurement-mic calibration; unused here, carried so a result states which mic response the IR was taken with.</summary>
        public VirtualCrossoverCalibrationSettings? MicrophoneCalibration { get; set; }

        /// <summary>Array microphone notes and calibrations by channel; measured curves are stored raw.</summary>
        internal IReadOnlyList<ArrayMicrophoneMetadata> ArrayMicrophoneMetadata { get; set; } = [];

        /// <summary>Configured for the next run; frozen onto its result at run start.</summary>
        public SplCalibration? SplCalibration { get; set; }

        /// <summary>Whether the next run will carry a usable SPL anchor; mirrors what <see cref="RunAsync"/> freezes onto the result.</summary>
        public bool NextRunHasSplAnchor =>
            SplCalibration is { } calibration &&
            calibration.MatchesInput(CurrentInputIdentity());

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

        // Null until a run completes.
        internal SweepRunQualityReport? QualityReport { get; private set; }

        internal SweepResultCaution? ResultCaution { get; private set; }
        public Exception? LastError { get; private set; }

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

        private void InitCore(SweepMeasurementConfiguration configuration)
        {
            SweepSignalConfiguration signal = configuration.Signal;
            SweepAudioConfiguration audio = configuration.Audio;
            SweepAveragingConfiguration averaging = configuration.Averaging;
            PlaybackChannel = NormalizePlaybackChannel(signal.PlaybackChannel);
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
            AverageRunCount = Math.Clamp(averaging.RunCount, 1, 64);
            ProtectiveHighPass = ProtectiveHighPassConfiguration.Normalize(
                configuration.ProtectiveHighPass);
            QualityReport = null;
            ResultCaution = null;
            LastError = null;

            Sweep?.Dispose();
            Sweep = new ExponentialSineSweep();
            // Low/HighFrequencyHz keep the REQUESTED band (round-trip stable); the achieved band lives on Sweep.
            Sweep.FillData(
                signal.LowFrequencyHz,
                signal.HighFrequencyHz,
                signal.RequestedDurationSeconds,
                signal.Bits,
                signal.SampleRate);
        }

        internal Task<MeasurementResult?> RunAsync()
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
                QualityReport = null;
                ResultCaution = null;
                LastError = null;
                // Frozen at run start so later setting changes cannot rewrite this result.
                var frozen = new FrozenRun(
                    NextRunHasSplAnchor ? SplCalibration : null,
                    ProtectiveHighPass,
                    MicrophoneCalibration,
                    ArrayMicrophoneMetadata);
                measurementTask = RunCoreAsync(frozen, cancellationTokenSource.Token);
                return measurementTask;
            }
        }

        public async Task AbortAsync()
        {
            Task<MeasurementResult?>? runningTask;
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

        /// <summary>
        /// Measures a sweep recorded outside Resonalyze, on the channel that best matches the configured sweep
        /// (<see cref="RecordedSweepChannels"/>). Only for callers with no user to ask: a DAW reference track beats the mic.
        /// See docs/tech/sweep-measurement.md#importing-a-recorded-sweep.
        /// </summary>
        internal static RecordedSweepImport ImportRecordedSweep(
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

            return ImportRecordedSweep(
                configuration,
                channels,
                sampleRate,
                channels.Length > 1
                    ? RecordedSweepChannels.Best(
                        RecordedSweepChannels.Rank(configuration, channels))
                    : 0);
        }

        internal static RecordedSweepImport ImportRecordedSweep(
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

            return ImportRecordedSweep(configuration, channels[channel], sampleRate) with { Channel = channel };
        }

        /// <summary>Touches no engine state: the configuration describes the sweep the file holds, not the next run.</summary>
        internal static RecordedSweepImport ImportRecordedSweep(
            SweepMeasurementConfiguration configuration,
            float[] recordedSamples,
            int sampleRate)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(recordedSamples);
            return ImportRecordedSweepCore(configuration, recordedSamples, sampleRate);
        }

        private static RecordedSweepImport ImportRecordedSweepCore(
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

            // FFTs are sized by their input, so analyze only the excitation and decay, trying candidates in turn.
            double bestCompactnessDb = double.NegativeInfinity;
            double bestSharpnessDb = double.NegativeInfinity;
            bool measurable = false;
            int longestExcitation = 0;
            IReadOnlyList<string>? captureIssues = null;
            foreach (RecordedSweepSpan span in RecordedSweepWindow.LocateCandidates(
                recordedSamples, sweep.SweepData, sampleRate))
            {
                // From the excitation start, not the span start: a pre-roll would make a truncated take look long enough.
                longestExcitation = Math.Max(longestExcitation, span.ExcitationLength);
                if (span.ExcitationLength < sweep.SweepSamples)
                {
                    continue;
                }

                // Compensate a clock or duration scale mismatch. See docs/tech/sweep-measurement.md#import-time-scale.
                double timeScalePpm = EstimateTimeScalePpm(recordedSamples, span, sweep, sampleRate);
                using var reference = new ExponentialSineSweep();
                if (timeScalePpm != 0)
                {
                    reference.FillStretched(sweep.Spec, 1.0 + timeScalePpm * 1e-6, signal.Bits);
                }
                ImportedSweepAnalysis analysis = AnalyzeImportedSpan(
                    recordedSamples, span, timeScalePpm == 0 ? sweep : reference);

                // A clipped sweep still deconvolves compactly, so the shape gate below cannot catch it.
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
                        return BuildImportedSweep(configuration, sweep, analysis, timeScalePpm);
                    }
                }
            }

            if (longestExcitation < sweep.SweepSamples)
            {
                // Noisy real take (match 0.06) and a different sweep (0.21) overlap, so the message names both.
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

        /// <summary>Search bound: covers crystal drift and per-octave rounding (489 ppm field), far short of a neighbouring sweep rate.</summary>
        private const double MaximumTimeScalePpm = 800.0;

        // Below this a better fit is metric noise.
        private const double MeaningfulScaleGainDb = 0.5;

        // Refinement floor (50 µs over a 4 s sweep); finer steps read the room rather than the scale.
        private const double FinestScaleStepPpm = 12.5;

        // Objective rises over ~200 ppm either side of the truth, so 100 cannot step over the peak.
        private const double CoarseScaleStepPpm = 100.0;

        /// <summary>Reference stretch (ppm) that makes the deconvolved arrival sharpest; 0 unless meaningfully better than none.</summary>
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

            // Coarse scan first: the objective dips on the way to its peak, so hill-climbing from zero stalls.
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

            // Sweep laid at the start of a window-length view: the estimator truncates both to the shorter signal,
            // so the bare sweep would discard the room decay.
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

        /// <summary>Where an imported arrival is placed. See docs/tech/sweep-measurement.md#imported-arrival-placement.</summary>
        private const double ImportedArrivalSeconds = 0.010;

        private static RecordedSweepImport BuildImportedSweep(
            SweepMeasurementConfiguration configuration,
            ExponentialSineSweep sweep,
            ImportedSweepAnalysis analysis,
            double timeScalePpm)
        {
            SweepSignalConfiguration signal = configuration.Signal;
            int arrival = Math.Min(
                (int)Math.Round(ImportedArrivalSeconds * signal.SampleRate),
                analysis.TransferImpulseResponse.Length - 1);
            Complex[] transfer = RotateTo(
                analysis.TransferImpulseResponse, analysis.TransferPeakIndex, arrival);
            // No loopback entry: the reference is generated, so there is no input level to meter.
            var average = new SweepAverageResult(
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
                ArrayMicrophones: []);
            // A recording made elsewhere freezes nothing: no anchor, mic curve or filter is known for it.
            var origin = new ResultOrigin(
                signal.SampleRate,
                signal.Bits,
                NormalizePlaybackChannel(signal.PlaybackChannel),
                signal.LowFrequencyHz,
                signal.HighFrequencyHz,
                Math.Clamp(configuration.Averaging.RunCount, 1, 64),
                ProtectiveHighPassConfiguration.Normalize(configuration.ProtectiveHighPass),
                sweep,
                TimingReference.RecordedSweep,
                DateTimeOffset.UtcNow,
                Frozen: null,
                Diagnostics: null);
            return new RecordedSweepImport(
                BuildResult(average, origin),
                Channel: 0,
                timeScalePpm == 0 ? null : timeScalePpm);
        }

        /// <summary>Pairs array curves with configured metadata by channel, not index: failed mics are absent from the curves.</summary>
        private static IReadOnlyList<ArrayMicrophoneCurve> AttachArrayMetadata(
            IReadOnlyList<ArrayMicrophoneCurve> microphones,
            FrozenRun? frozen)
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
                        Calibration = frozen?.MicrophoneCalibration
                    });
                    continue;
                }

                ArrayMicrophoneMetadata? metadata = frozen?.ArrayMetadata.FirstOrDefault(
                    candidate => candidate.ChannelOffset == microphone.ChannelOffset);
                attached.Add(microphone with
                {
                    Note = metadata?.Note,
                    Calibration = metadata?.Calibration
                });
            }

            return attached;
        }

        // Circular, because the transfer IR's acausal pre-ringing lives at the buffer's far end.
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

        // Import counterpart of RequireCredibleTransferIr: a shapeless transfer IR means the file is not a recording of THIS sweep.
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

            // Name the gate that failed: quoting compactness for a sharpness failure shows a number inside the passing range.
            if (bestCompactnessDb >= TransferIrDiagnostics.MinimumCompactnessDb)
            {
                return new InvalidOperationException(FormattableString.Invariant(
                    $"The recording deconvolves into a smeared arrival rather than an impulse response: its peak stands only {bestSharpnessDb:0.0} dB above the {TransferIrDiagnostics.ArrivalWindowSeconds * 1000:0} ms around it, where a real measurement reads 11-16 dB. The sweep in the file is not the one the settings describe — check the band and the per-octave time in Measurement Options against the sweep it was recorded from."));
            }

            return new InvalidOperationException(FormattableString.Invariant(
                $"The recording did not deconvolve into a credible impulse response: the energy around its peak is only {bestCompactnessDb:0.0} dB above the rest of the recording, at best (a real measurement reads 29-49 dB). It is most likely not a recording of this sweep — check that the band, the per-octave time and the sample rate in Measurement Options are the ones the sweep was generated with."));
        }

        private async Task<MeasurementResult?> RunCoreAsync(
            FrozenRun frozen,
            CancellationToken cancellationToken)
        {
            // Stamped per run, not at Init: one configuration serves every Record press.
            DateTimeOffset measuredAtUtc = DateTimeOffset.UtcNow;
            ExponentialSineSweep sweep = Sweep!;
            MeasurementResult? measured = null;
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

                // Gate masks bins by sweep geometry. See docs/tech/sweep-measurement.md#excitation-band-gate.
                var accumulator = new SweepAverageAccumulator(
                    BuildExcitationGate(sweep),
                    SampleRate,
                    ProtectiveHighPass,
                    ActiveMicrophoneChannelOffset,
                    ArrayInputChannelOffsets);
                var rejections = new List<SweepRunRejection>();
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
                        // A bad run stops the measurement: its faults are configuration, which a retry reproduces exactly.
                        // See docs/tech/sweep-measurement.md#run-acceptance.
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
                    // A shape rejection is the bad-loopback case; diagnose the rejected capture for it.
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
                // After the refusal, never inside it: the total-failure diagnosis also calls that check.
                ResultCaution = DescribeResultCaution(averageResult);
                measured = BuildResult(
                    averageResult,
                    new ResultOrigin(
                        SampleRate,
                        Bits,
                        PlaybackChannel,
                        LowFrequencyHz,
                        HighFrequencyHz,
                        AverageRunCount,
                        ProtectiveHighPass,
                        sweep,
                        TimingReference.SynchronizedLoopback,
                        measuredAtUtc,
                        frozen,
                        LastAudioSessionDiagnostics));
                RaiseLevels(averageResult.Levels);
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
                    // Teardown failure must not demote results already published.
                    LastError ??= exception;
                }

                lock (stateSync)
                {
                    inProgress = false;
                }
                Publish(Completed, measured);
            }

            return measured;
        }

        // Requested band fully excited; achieved band adds fade guard bands. Clamps keep the gate ordered for short sweeps.
        // Rate read off the sweep: an import builds the gate before its configuration is applied.
        private static ExcitationBandGate BuildExcitationGate(ExponentialSineSweep sweep)
        {
            double nyquist = sweep.SampleRate / 2.0;
            if (!(nyquist > 0))
            {
                return ExcitationBandGate.FullBand;
            }

            // Actual envelope edges: a fade padded to minimum length rises past the requested edge.
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

        /// <summary>Array channels colliding with the measurement inputs are refused, not dropped (a silent drop averages an unsampled position).</summary>
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
            RaiseLevels(InputLevelMapping.Map(levels));
        }

        /// <summary>Throws the transfer function's shape diagnosis in place of the generic every-run-failed refusal, when it applies.</summary>
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
                    // No array: building array curves would refuse on the very fault being explained.
                    []);
                // Only one capture exists (the measurement stops on the first failure), which bounds FFT-sized scratch.
                accumulator.Add(
                    AnalyzeCapturedRun(capture, sweep, raiseIntermediateLevels: false));
                result = accumulator.BuildResult();
            }
            catch (Exception)
            {
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

            // Every array mic judged like the measurement mic; any compromised channel fails the run.
            // See docs/tech/sweep-measurement.md#run-acceptance.
            var judged = new List<(string Where, float[] Samples)>();
            if (issues.Count == 0)
            {
                // A noise-only run passes level checks and scales the H1 average by the good-run fraction (-2.50 dB for 1 bad in 4).
                judged.Add((string.Empty, microphone));
            }

            // Name the CONFIGURED input: capture indices are relative to the first opened channel (ASIO inputs 6,8,9 arrive as 1,3,4).
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

        /// <summary>Per-channel response credibility against the one shared loopback. See docs/tech/sweep-measurement.md#run-acceptance.</summary>
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

            // Size bound as for the loopback diagnosis; above it only level checks judge the run. See docs/tech/sweep-measurement.md#diagnosis-size-bounds.
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

            // Harmonics read here while this run's deconvolution exists, so the refusal path never allocates FFT-sized buffers.
            float[]? loopbackSamples = captured.LoopbackChannel is int loopbackIndex &&
                (uint)loopbackIndex < (uint)sampleChannels.Length
                    ? sampleChannels[loopbackIndex]
                    : null;
            EssHarmonicEnergy? microphoneDistortion = MeasureDistortion(() => sweepResult);
            // Loopback deconvolution exists only for this hint, so it is skipped above the size bound.
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

        /// <summary>One accepted run's array frames; index i is the i-th configured microphone. Verdicts were made in <see cref="AssessRunQuality"/>.</summary>
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

        /// <summary>Harmonic reading of one channel, or null. Never throws; the thunk lets a failed loopback deconvolution cost only the hint.</summary>
        private EssHarmonicEnergy? MeasureDistortion(Func<SweepDeconvolutionResult> deconvolve)
        {
            try
            {
                SweepDeconvolutionResult deconvolved = deconvolve();
                // Geometry of the sweep that actually ran, not the requested band.
                ExponentialSineSweep sweep = Sweep!;
                return EssHarmonicAnalysis.MeasureHarmonicEnergy(
                    deconvolved.ImpulseResponse,
                    new EssSweepMetadata(
                        sweep.LowFrequencyHz,
                        sweep.HighFrequencyHz,
                        sweep.SweepSamples / (double)SampleRate,
                        SampleRate,
                        sweep.SweepSamples,
                        deconvolved.PeakIndex));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Diagnosis hint only, never a rejection (transfer estimation is scale-invariant). See docs/tech/sweep-measurement.md#distortion-diagnosis.
        private const double SuspiciouslyQuietLoopbackDbFs = -30;

        // Packets 2..5 vs linear; -26 dB (5 %) accuses nothing healthy. See docs/tech/sweep-measurement.md#distortion-diagnosis.
        private const double DistortingChannelDb = -26.0;

        // ~170 MB transient scratch per run at the bound. See docs/tech/sweep-measurement.md#diagnosis-size-bounds.
        internal const int MaxLoopbackDiagnosisFftLength = 1 << 22;

        /// <summary>Whether the per-run credibility H1 (padded to twice the capture) fits under the loopback diagnosis ceiling.</summary>
        internal static bool RunCredibilityDiagnosisFits(int recordedSamples)
        {
            // In long so an absurd configuration reads as "does not fit" instead of throwing.
            long padded = (long)recordedSamples * 2;
            return recordedSamples > 0 &&
                padded <= MaxLoopbackDiagnosisFftLength &&
                DspMath.NextPowerOfTwo((int)padded) <= MaxLoopbackDiagnosisFftLength;
        }

        internal static bool LoopbackDiagnosisFits(
            int recordedSamples,
            int inverseFilterSamples)
        {
            long convolutionLength = (long)recordedSamples + inverseFilterSamples - 1;
            return convolutionLength <= MaxLoopbackDiagnosisFftLength &&
                DspMath.NextPowerOfTwo((int)convolutionLength) <=
                    MaxLoopbackDiagnosisFftLength;
        }

        // The averaged transfer IR must look like an impulse response before publishing. See docs/tech/sweep-measurement.md#shape-gate.
        private void RequireCredibleTransferIr(SweepAverageResult result)
        {
            if (result.TransferImpulseResponse is not { } transfer)
            {
                return;
            }

            // Fail-closed: unmeasurable shape (degenerate or non-finite) is a refusal; a NaN would pass every comparison.
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
            // A named culprit replaces the generic wiring advice; the two never both apply.
            string distortionDiagnosis = DescribeDistortion(result);
            string advice = distortionDiagnosis.Length > 0
                ? distortionDiagnosis
                : " Check the microphone and loopback wiring and levels, then measure again.";
            throw new InvalidOperationException(
                $"The transfer function did not form a credible impulse response: {shapeDiagnosis}.{levelDiagnosis}{advice}");
        }

        /// <summary>Pre-arrival reading, or null when no guard band can separate the estimator kernel from the fault or the record is too short.</summary>
        private double? MeasurePreArrival(Complex[] transfer)
        {
            // Null means no reading, not a bad record; nothing is refused on this measure.
            return TransferIrDiagnostics.CanJudgePreArrival(ExcitationGate())
                ? TransferIrDiagnostics.MeasurePreArrivalDb(transfer, SampleRate)
                : null;
        }

        private ExcitationBandGate ExcitationGate() => Sweep is { } sweep
            ? BuildExcitationGate(sweep)
            : ExcitationBandGate.FullBand;

        /// <summary>Never a refusal; see <see cref="SweepResultCaution"/>.</summary>
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

        /// <summary>Names the channel whose own signal distorts; H1 believes whatever the reference says was played. Empty when none.</summary>
        private string DescribeDistortion(SweepAverageResult result)
        {
            if (result.LoopbackDistortion is { AffectedRuns: > 0 } reference)
            {
                // Companion facts come from the worst run itself; aggregate levels are maxima over runs and would mix captures.
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

        // The figure quoted is the worst of several runs; say how many runs were judged and affected.
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

        private static MeasurementResult BuildResult(SweepAverageResult result, ResultOrigin origin)
        {
            Complex[]? transferImpulseResponse = result.TransferImpulseResponse;
            int transferPeakIndex = result.TransferPeakIndex;
            double[]? transferCoherence = result.TransferCoherence;
            if (transferImpulseResponse != null && origin.Compensation.Enabled)
            {
                ProtectiveHighPassCompensationResult compensation =
                    ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
                        transferImpulseResponse,
                        origin.Compensation.ToEdge(),
                        origin.SampleRate,
                        ProtectiveHighPassConfiguration.MaximumCompensationBoostDb);
                transferImpulseResponse = compensation.ImpulseResponse;
                transferCoherence = compensation.MaskCoherence(transferCoherence);
                // Live runs let the corrected arrival move with the removed HP group delay; imports rotate back to their 10 ms origin.
                int correctedPeakIndex = FindPeakIndex(transferImpulseResponse);
                if (origin.TimingReference == TimingReference.RecordedSweep)
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

            ExponentialSineSweep sweep = origin.Sweep;
            return new MeasurementResult
            {
                SampleRate = origin.SampleRate,
                Bits = origin.Bits,
                PlaybackChannel = origin.PlaybackChannel,
                LowFrequencyHz = origin.LowFrequencyHz,
                HighFrequencyHz = origin.HighFrequencyHz,
                AchievedLowFrequencyHz = sweep.LowFrequencyHz,
                AchievedHighFrequencyHz = sweep.HighFrequencyHz,
                MeasuredLowFrequencyHz = sweep.Spec.FullAmplitudeLowFrequencyHz,
                MeasuredHighFrequencyHz = sweep.Spec.FullAmplitudeHighFrequencyHz,
                SweepDurationSeconds = sweep.SweepSamples / (double)origin.SampleRate,
                MeasuredAtUtc = origin.MeasuredAtUtc,
                MeasurementMode = result.TransferImpulseResponse != null
                    ? SweepMeasurementMode.LoopbackTransfer
                    : SweepMeasurementMode.SweepDeconvolution,
                TimingReference = origin.TimingReference,
                SweepDeconvolution = new MeasurementImpulseResponse(
                    result.SweepImpulseResponse,
                    result.SweepPeakIndex),
                Transfer = transferImpulseResponse != null
                    ? new MeasurementImpulseResponse(transferImpulseResponse, transferPeakIndex)
                    : null,
                TransferCoherence = transferCoherence,
                AverageRunCount = origin.AverageRunCount,
                AcceptedAverageRunCount = result.AcceptedRunCount,
                Levels = result.Levels,
                SplCalibration = origin.Frozen?.SplCalibration,
                MicrophoneCalibration = origin.Frozen?.MicrophoneCalibration,
                ProtectiveHighPass = origin.Frozen?.ProtectiveHighPass,
                ArrayMicrophones = AttachArrayMetadata(result.ArrayMicrophones, origin.Frozen),
                AudioSession = ImpulseResponseFile.CreateAudioSessionFileEntry(
                    origin.Diagnostics,
                    origin.SampleRate,
                    origin.Bits)
            };
        }

        /// <summary>The calibration a run is taken through, fixed when it starts.</summary>
        private sealed record FrozenRun(
            SplCalibration? SplCalibration,
            ProtectiveHighPassConfiguration ProtectiveHighPass,
            VirtualCrossoverCalibrationSettings? MicrophoneCalibration,
            IReadOnlyList<ArrayMicrophoneMetadata> ArrayMetadata);

        /// <summary>What a result takes from the sweep that produced it, beside the analysis itself.</summary>
        private sealed record ResultOrigin(
            int SampleRate,
            int Bits,
            PlaybackChannel PlaybackChannel,
            double LowFrequencyHz,
            double HighFrequencyHz,
            int AverageRunCount,
            ProtectiveHighPassConfiguration Compensation,
            ExponentialSineSweep Sweep,
            TimingReference TimingReference,
            DateTimeOffset MeasuredAtUtc,
            FrozenRun? Frozen,
            AudioSessionDiagnostics? Diagnostics);

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

            // Views, not copies: the estimator converts as it fills its FFT buffers.
            frame = new TransferFunctionFrame(
                new RecordedSamplesView(sampleChannels[loopbackIndex.Value]),
                new RecordedSamplesView(sampleChannels[microphoneIndex]));
            return true;
        }

        private static InputLevelMeterSnapshot CreateFinalLevelSnapshot(
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
            InputLevelMeterSnapshot Levels,
            EssHarmonicEnergy? MicrophoneDistortion,
            EssHarmonicEnergy? LoopbackDistortion,
            IReadOnlyList<TransferFunctionFrame?> ArrayCaptures);

        internal enum DistortionVerdict
        {
            // No reading, or floors too high to confirm or exclude a DistortingChannelDb-level fault.
            Unjudged,
            // Every requested order readable AND total packet energy below the threshold.
            JudgedClean,
            Distorting
        }

        /// <summary>
        /// Judged = could confirm OR exclude a <see cref="DistortingChannelDb"/> fault. Floors bound the background beside
        /// the packets, so up to <c>floor * 10^0.6</c> can hide inside an undetected one.
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
                // An accusation stands on any coverage; unread orders could only add to it.
                return DistortionVerdict.Distorting;
            }
            // Certifying clean needs every order read: an unread order can hide anything.
            return reading.CompleteCoverage && reading.CeilingDb < DistortingChannelDb
                ? DistortionVerdict.JudgedClean
                : DistortionVerdict.Unjudged;
        }

        /// <summary>Harmonic readings over accepted runs; worst, affected and judged counts all matter for an honest averaged message.</summary>
        private readonly record struct DistortionTally(
            double WorstDb,
            int AffectedRuns,
            int JudgedRuns);

        /// <summary>Facts from the single run that produced the worst loopback reading, so quotes describe one capture.</summary>
        private readonly record struct WorstLoopbackRun(
            double? MicrophoneDetectedDb,
            InputLevelMeterEntry LoopbackLevel);

        private sealed class DistortionAccumulator
        {
            private double worstDb = double.NegativeInfinity;
            private int affectedRuns;
            private int judgedRuns;

            /// <summary>True when this run now holds the worst detection.</summary>
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

            // Null when no run was judged, which differs from every run reading clean.
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

            /// <summary>Measurement mic first: it anchors the levelling and is the only one tied to the SPL calibration.</summary>
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

        private static PlaybackChannel NormalizePlaybackChannel(PlaybackChannel channel) =>
            Enum.IsDefined(channel) ? channel : PlaybackChannel.Mono;

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
                    // Notifications are observational; a broken subscriber must not change the outcome.
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

    /// <summary>IR and its peak index as one immutable reference for cross-thread readers.</summary>
    public sealed record MeasurementImpulseResponse(
        Complex[] ImpulseResponse,
        int PeakIndex);

    /// <summary>A recorded sweep's result, and what the import decided on the way: the channel and any time-scale correction.</summary>
    /// <param name="TimeScalePpm">Correction applied, in ppm; null when none. See docs/tech/sweep-measurement.md#import-time-scale.</param>
    internal sealed record RecordedSweepImport(
        MeasurementResult Result,
        int Channel,
        double? TimeScalePpm);
}

public readonly record struct SweepAverageProgress(
    int CurrentRun,
    int TotalRuns,
    int AcceptedRuns);
