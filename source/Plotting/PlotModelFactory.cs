using System.Numerics;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed class PlotModelFactory
{
    internal const double GroupDelayMagnitudeGateDb = -40.0;

    public const string CoherenceAxisKey = "coherence";
    public const string DecibelAxisKey = "decibel";
    public const string FrequencyAxisKey = "frequency";
    public const string PhaseAxisKey = "phase";
    public const string GroupDelayAxisKey = "groupDelay";
    public const string ImpulseAxisKey = "impulse";
    public const string ImpulseStepAxisKey = "impulseStep";
    public const string TimeAxisKey = "time";
    public const string AutocorrelationAxisKey = "autocorrelation";

    // The next run's configuration: the live SPL anchor, and the rate a plot takes when nothing is open.
    private readonly ExpSweepMeasurement expSweepMeasurement;
    private readonly NoiseMeasurement noiseMeasurement;
    private readonly Func<string?, CalibrationFile?> getCalibration;
    private readonly MeasurementPlotContext measurementContext;
    private readonly FrequencyResponseOptions frequencyResponseOptions;
    private readonly FrequencyResponseOptions phaseResponseOptions;
    private readonly FrequencyResponseOptions groupDelayOptions;
    private readonly CurveVisibilityOptions frequencyResponseVisibility;
    private readonly CurveVisibilityOptions phaseResponseVisibility;
    private readonly CurveVisibilityOptions groupDelayVisibility;
    private readonly ImpulseResponseOptions impulseResponseOptions;
    private readonly LiveSpectrumOptions liveSpectrumOptions;
    private readonly WaterfallGenerateOptions waterfallGenOptions;
    private readonly WaterfallGenerateOptions burstDecayGenOptions;
    private Func<CompareAnalysisSource?>? getCompareSource;

    public PlotModelFactory(
        AnalyzerDocument document,
        ExpSweepMeasurement expSweepMeasurement,
        NoiseMeasurement noiseMeasurement,
        Func<string?, CalibrationFile?> getCalibration,
        PlotPresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.expSweepMeasurement = expSweepMeasurement;
        this.noiseMeasurement = noiseMeasurement;
        this.getCalibration = getCalibration;
        measurementContext = new MeasurementPlotContext(document);
        frequencyResponseOptions = options.FrequencyResponse;
        phaseResponseOptions = options.PhaseResponse;
        groupDelayOptions = options.GroupDelay;
        frequencyResponseVisibility = options.FrequencyResponseVisibility;
        phaseResponseVisibility = options.PhaseResponseVisibility;
        groupDelayVisibility = options.GroupDelayVisibility;
        impulseResponseOptions = options.ImpulseResponse;
        liveSpectrumOptions = options.LiveSpectrum;
        waterfallGenOptions = options.Waterfall;
        burstDecayGenOptions = options.BurstDecay;
    }

    public string? ImpulseResponseFileName => measurementContext.ImpulseResponseFileName;

    private int AnalysisSampleRate => measurementContext.Result?.SampleRate ?? expSweepMeasurement.SampleRate;

    // Compare overlay uses the SAME analysis settings as the main measurement.
    public void SetCompareSourceProvider(Func<CompareAnalysisSource?> provider) =>
        getCompareSource = provider;

    /// <summary>Without a valid SPL anchor the plot stays on a view-only dB SPL axis rather than falling back to dBr.</summary>
    public MagnitudeScale EffectiveFrequencyResponseScale =>
        frequencyResponseOptions.MagnitudeScale;

    /// <summary>Offset turning raw mic dBFS into dB SPL for the live RTA (no loopback term). Null without a calibration captured on the live input.</summary>
    public double? LiveSplOffsetDb
    {
        get
        {
            if (expSweepMeasurement.SplCalibration is not { } calibration)
            {
                return null;
            }

            // Validate against the live input, not the saved sweep input.
            if (!calibration.MatchesInput(noiseMeasurement.CurrentInputIdentity()))
            {
                return null;
            }

            return calibration.OffsetDb;
        }
    }

    /// <summary>Selected mode, forced to RTA without a loopback reference. Single source for plot and controller.</summary>
    public LiveAnalysisMode EffectiveLiveAnalysisMode =>
        noiseMeasurement.IsMicOnly &&
        liveSpectrumOptions.AnalysisMode == LiveAnalysisMode.TransferFunction
            ? LiveAnalysisMode.Rta
            : liveSpectrumOptions.AnalysisMode;

    /// <summary>RTA: the selection (view-only SPL without calibration). Transfer: always relative (dimensionless ratio).</summary>
    public MagnitudeScale EffectiveLiveSpectrumScale
    {
        get
        {
            LiveAnalysisMode mode = EffectiveLiveAnalysisMode;
            // Spatial average is absolute only with an anchor: relative band levels on the SPL axis fall below its -20 floor.
            // Tested by trait, not identity, so band power and axis scale stay consistent.
            if (mode.IsSpatialAverageCapture())
            {
                return LiveSplOffsetDb.HasValue
                    ? MagnitudeScale.SoundPressureLevel
                    : MagnitudeScale.Relative;
            }

            return mode.IsReferenceFree()
                ? liveSpectrumOptions.MagnitudeScale
                : MagnitudeScale.Relative;
        }
    }

    /// <summary>Rendering pipeline (band power vs per-bin), deliberately separate from the axis scale: a spatial average needs band levels but no absolute reference.</summary>
    public bool LiveUsesBandPower =>
        EffectiveLiveAnalysisMode.IsSpatialAverageCapture() ||
        EffectiveLiveSpectrumScale == MagnitudeScale.SoundPressureLevel;

    /// <summary>Excitation model the RTA display compensates; null when off (MMM forces it on), not reference-free, or Silent. Flat is a real value. Part of the peak-hold key.</summary>
    public NoiseSpectralModel? LiveTiltModel =>
        EffectiveLiveAnalysisMode.IsReferenceFree() &&
        (liveSpectrumOptions.CompensateNoiseTilt ||
            EffectiveLiveAnalysisMode.IsSpatialAverageCapture())
            ? NoiseColorTilt.SpectralModel(liveSpectrumOptions.EffectiveNoiseColor)
            : null;

    /// <summary>MMM pins smoothing Off: the SPL path already integrates a fixed 1/12-octave band, and a capture recipe must not vary across a set.</summary>
    public int EffectiveLiveSmoothingCode =>
        EffectiveLiveAnalysisMode.IsSpatialAverageCapture()
            ? 0
            : liveSpectrumOptions.SmoothingInverseOctaves;

    private double LiveSplRenderOffset =>
        EffectiveLiveSpectrumScale == MagnitudeScale.SoundPressureLevel
            ? LiveSplOffsetDb ?? 0.0
            : 0.0;

    private CalibrationFile? GetCalibration(FrequencyResponseOptions options) =>
        getCalibration(options.CalibrationId);

    /// <summary>Calibration frozen when the run began, not the rig's current choice: a changed setting must not re-render or mislabel the saved walk.</summary>
    private CalibrationFile? LiveCaptureCalibration =>
        noiseMeasurement.CaptureMicrophoneCalibration;

    /// <summary>Raw samples plus smoothing code for the overlay layer; only the primary FR magnitude has a raw form, others return null (drawn-curve fallback).</summary>
    public RawCurveCapture? BuildRawCurve(CurveTag tag)
    {
        if (tag.Kind != AnalysisCurveKind.Primary || tag.Mode != Mode.FrequencyResponse)
        {
            return null;
        }

        // SPL adds an absolute offset the stored spectrum lacks, so keep the drawn-curve fallback (but record the rate).
        if (EffectiveFrequencyResponseScale != MagnitudeScale.Relative)
        {
            return DescribeWithoutRawForm(
                (int)Math.Round(frequencyResponseOptions.SmoothingInverseOctaves),
                AnalysisSampleRate,
                frequencyResponseOptions.UseCalibration
                    ? GetCalibration(frequencyResponseOptions)
                    : null);
        }

        CalibrationFile? calibration = GetCalibration(frequencyResponseOptions);
        IReadOnlyList<SignalPoint>? spectrum = tag.Source == CurveSource.Compare
            ? TryCreateCompareMeasurement() is { } compare
                ? MeasurementPlotContext.BuildRawPrimarySpectrum(
                    compare.Measurement, frequencyResponseOptions)
                : null
            : measurementContext.CreateRawPrimarySpectrum(frequencyResponseOptions);
        return spectrum is { Count: > 1 }
            ? new RawCurveCapture(
                spectrum,
                RawCurveRenderer.CaptureCalibrationCorrection(
                    frequencyResponseOptions.UseCalibration ? calibration : null),
                (int)Math.Round(frequencyResponseOptions.SmoothingInverseOctaves),
                // Compare is only offered at the main measurement's rate.
                AnalysisSampleRate > 0 ? AnalysisSampleRate : null,
                PointsCalibration: null,
                Band: tag.Source == CurveSource.Compare
                    ? getCompareSource?.Invoke()?.Band ?? default
                    : measurementContext.MeasuredBand)
            : null;
    }

    /// <summary>Impulse trace re-rendered under a canonical framing (absolute sample indices, raw linear values). Null when not an impulse trace.</summary>
    public ImpulseOverlayCapture? BuildImpulseCapture(CurveTag tag)
    {
        if (tag.Mode != Mode.ImpulseResponse ||
            !measurementContext.HasTransferImpulseResponse)
        {
            return null;
        }

        IImpulseMeasurement? source = tag.Source == CurveSource.Compare
            ? TryCreateCompareMeasurement()?.Measurement
            : measurementContext.CreatePrimaryMeasurement();
        if (source == null)
        {
            return null;
        }

        // Band and envelope smoothing are part of the values; everything re-frameable is neutralized.
        var canonical = new ImpulseResponseOptions
        {
            ShowImpulse = tag.Kind == AnalysisCurveKind.Primary,
            ShowEnvelope = tag.Kind == AnalysisCurveKind.ImpulseEnvelope,
            ShowStep = tag.Kind == AnalysisCurveKind.ImpulseStep,
            EnvelopeSmoothingMs = impulseResponseOptions.EnvelopeSmoothingMs,
            // Normalize to the record's own peak so the raw integral can be recovered below; a normalized snapshot would freeze the toggle and Compare's main-peak normalization.
            NormalizeStepToImpulsePeak = true,
            BandFilterOctaves = impulseResponseOptions.BandFilterOctaves,
            BandCenterHz = impulseResponseOptions.BandCenterHz,
            AmplitudeScale = ImpulseAmplitudeScale.Linear,
            TimeUnit = ImpulseTimeUnit.Samples,
            TimeOrigin = ImpulseTimeOrigin.RecordStart,
            Invert = false,
        };

        ImpulseCurveSet set = DataHelper.GetImpulseCurves(
            source, canonical, new ImpulseRenderFrame());
        AnalysisCurve? curve = tag.Kind switch
        {
            AnalysisCurveKind.ImpulseEnvelope => set.Envelope,
            AnalysisCurveKind.ImpulseStep => set.Step,
            _ => set.Impulse
        };
        if (curve is not { Points.Count: > 1 })
        {
            return null;
        }

        IReadOnlyList<SignalPoint> samples = tag.Kind == AnalysisCurveKind.ImpulseStep
            ? curve.Points
                .Select(point => new SignalPoint(point.X, point.Y * set.PeakReference))
                .ToArray()
            : curve.Points;

        return new ImpulseOverlayCapture(
            ImpulseOverlayThinning.Thin(samples),
            tag.Kind,
            set.PeakReference,
            source.SampleRate);
    }

    public RawCurveCapture? BuildRawRtaCurve(IReadOnlyList<double>? inputMagnitude)
    {
        if (inputMagnitude is not { Count: > 1 })
        {
            return null;
        }

        int smoothingCode = EffectiveLiveSmoothingCode;
        if (LiveUsesBandPower)
        {
            // The band trace applies calibration additively per band, so a consumer can swap it exactly later.
            return DescribeWithoutRawForm(
                smoothingCode,
                noiseMeasurement.SampleRate,
                LiveCaptureCalibration);
        }

        List<SignalPoint> spectrum = LiveRtaRawCapture.BuildRelativeRaw(
            inputMagnitude,
            noiseMeasurement.SequenceLength,
            noiseMeasurement.SampleRate,
            LiveTiltModel);
        if (spectrum.Count < 2)
        {
            return DescribeWithoutRawForm(
                smoothingCode,
                noiseMeasurement.SampleRate,
                LiveCaptureCalibration);
        }

        return new RawCurveCapture(
            spectrum,
            RawCurveRenderer.CaptureCalibrationCorrection(
                LiveCaptureCalibration),
            smoothingCode,
            noiseMeasurement.SampleRate > 0 ? noiseMeasurement.SampleRate : null);
    }

    /// <summary>Snapshot of the reference-free capture as a document; null unless on the band-power path.</summary>
    /// <remarks>Built here so the recipe describes what this pipeline drew, not what the options asked for.</remarks>
    /// <param name="frameCount">Taken from the same snapshot as the bins; the analyzer may have advanced since.</param>
    public LiveCaptureDocument? BuildLiveCaptureDocument(
        double[]? inputMagnitude,
        int frameCount,
        string title,
        int clippedFrameCount = 0)
    {
        // From the accumulation, the same field the render divides out.
        ProtectiveHighPassConfiguration protectiveHighPass =
            noiseMeasurement.CaptureProtectiveHighPass;
        int sampleRate = noiseMeasurement.SampleRate;
        int sequenceLength = noiseMeasurement.SequenceLength;
        if (inputMagnitude is not { Length: > 1 } ||
            sampleRate < 1 ||
            sequenceLength < 2 ||
            !LiveUsesBandPower)
        {
            return null;
        }

        var applied = new LiveRtaApplied();
        List<SignalPoint> curve = ResampleLiveRta(inputMagnitude, applied);
        if (curve.Count != LiveCaptureDocument.CurvePointCount)
        {
            return null;
        }

        CalibrationFile? calibration = LiveCaptureCalibration;

        int hop = Math.Max(1, noiseMeasurement.AnalysisHopSize);
        int frames = frameCount;
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Title = title ?? string.Empty,
            Method = SpatialAverageMethod.MovingMic,
            CaptureSessionId = noiseMeasurement.CaptureSessionId,
            SpectrumDb = LiveCaptureDocument.StoreSpectrumBins(
                inputMagnitude, sequenceLength, sampleRate),
            CurveDb = curve.Select(point => point.Y).ToArray(),
            GridStartHz = curve[0].X,
            GridStopHz = curve[^1].X,
            TiltCompensationDb = applied.TiltDb,
            CalibrationCorrectionDb = applied.CalibrationDb,
            ProtectiveHighPassCorrectionDb = applied.ProtectiveHighPassDb,
            // Name frozen beside the curve: ids past the 0 deg slot are GUIDs. The name is only a hint.
            Calibration = calibration != null
                ? VirtualCrossoverCalibrationSettings.From(
                    calibration,
                    noiseMeasurement.CaptureMicrophoneCalibrationName,
                    fileName: null)
                : null,
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = EffectiveLiveAnalysisMode,
                SampleRateHz = sampleRate,
                SequenceLength = sequenceLength,
                FrameMilliseconds = 1000.0 * sequenceLength / sampleRate,
                WindowType = noiseMeasurement.AnalysisWindowType,
                WindowEnbwBins = noiseMeasurement.AnalysisWindowEnbwBins,
                WindowMainLobeBins = noiseMeasurement.AnalysisWindowMainLobeBins,
                OverlapPercent = 100 - 100 * hop / sequenceLength,
                AveragingSpeed = liveSpectrumOptions.EffectiveAveragingSpeed,
                AveragedFrameCount = frames,
                ClippedFrameCount = clippedFrameCount,
                IntegratedSeconds = (double)frames * hop / sampleRate,
                NoiseColor = liveSpectrumOptions.EffectiveNoiseColor,
                // What the curve received: the render skips a misaligned compensation.
                SlopeCompensation = applied.TiltDb.Length > 0,
                // Null offset: relative levels, consistent across the set.
                MagnitudeScale = EffectiveLiveSpectrumScale,
                SplAnchorOffsetDb = LiveSplOffsetDb,
                SmoothingCode = EffectiveLiveSmoothingCode,
                ProtectiveHighPassKind = protectiveHighPass.Kind,
                ProtectiveHighPassFrequencyHz = protectiveHighPass.FrequencyHz,
                ProtectiveHighPassSlopeDbPerOctave = protectiveHighPass.SlopeDbPerOctave
            }
        };
    }

    // Drawn curve plus rate, baked smoothing and calibration, so a consumer can undo the additive correction.
    private static RawCurveCapture DescribeWithoutRawForm(
        int smoothingCode,
        int sampleRate,
        CalibrationFile? calibration) =>
        new(Array.Empty<SignalPoint>(),
            Array.Empty<double>(),
            smoothingCode,
            sampleRate > 0 ? sampleRate : null,
            calibration);

    public PlotModel CreateFrequencyResponse(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Frequency Response"));

        // dB SPL follows the selection; without calibration and loopback level the axis is view-only: own dBr curves omitted,
        // SPL overlays and an anchored Compare stay. Starting a run drops the display back to dBr (Form1).
        bool splRequested =
            frequencyResponseOptions.MagnitudeScale == MagnitudeScale.SoundPressureLevel;
        double? splOffset = splRequested ? measurementContext.SplOffsetDb : null;
        bool renderSpl = splOffset.HasValue;
        bool splViewOnly = splRequested && !renderSpl;

        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            IReadOnlyList<AnalysisCurve> curves = Array.Empty<AnalysisCurve>();
            if (splViewOnly)
            {
                // The Compare curve is judged on its own anchor and may stay visible.
                AddSplViewOnlyAnnotation(model);
            }
            else
            {
                SpectrumCurves requested = frequencyResponseVisibility.ToSpectrumCurves();
                // HDn/THD/noise are lifted by the primary's level on the SPL axis, so compute the primary even when hidden, then remove it.
                bool anchorsHiddenPrimary = renderSpl &&
                    (requested & SpectrumCurves.Primary) == 0 &&
                    (requested & SpectrumCurves.Distortion) != 0;
                curves = measurementContext.CreateFrequencyResponseCurves(
                    frequencyResponseOptions,
                    GetCalibration(frequencyResponseOptions),
                    anchorsHiddenPrimary ? requested | SpectrumCurves.Primary : requested);
                if (renderSpl)
                {
                    curves = SplConversion.ToSoundPressureLevel(curves, splOffset!.Value);
                }

                if (anchorsHiddenPrimary)
                {
                    curves = curves
                        .Where(curve => curve.Kind != AnalysisCurveKind.Primary)
                        .ToList();
                }

                foreach (AnalysisCurve curve in curves)
                {
                    AddLineSeries(
                        model,
                        curve,
                        renderSpl ? SplTrackerFormat : DistortionTrackerFormat(curve.Kind),
                        Mode.FrequencyResponse,
                        DecibelAxisKey);
                }
            }

            AddCompareFrequencyResponse(model, splRequested, splViewOnly);

            if (!splViewOnly)
            {
                AddMeasurementCoherenceIfAvailable(
                    model,
                    frequencyResponseOptions,
                    frequencyResponseVisibility.ShowCoherence);

                AddArrayMicrophones(model, splOffset);

                AddHiddenHarmonicAnnotation(model, curves);
            }
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        PlotModelStyle.AddFrequencyAxis(model);
        // SPL needs its own default window and clamps (curves near 40-110 dB). Otherwise primary is dBr, distortion curves dBc.
        if (splRequested)
        {
            PlotModelStyle.AddDecibelAxis(
                model,
                "dB SPL",
                PlotModelStyle.SplDecibelMinimum,
                PlotModelStyle.SplDecibelMaximum,
                PlotModelStyle.SplDecibelAbsoluteMinimum,
                PlotModelStyle.SplDecibelAbsoluteMaximum);
        }
        else
        {
            PlotModelStyle.AddDecibelAxis(model, "dBr/dBc");
        }
        // A padded loopback puts the response above 0 dBr; open the view on it.
        PlotModelStyle.FitDecibelViewToPrimaryCurves(model);

        return model;
    }

    private const string SplTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00} dB SPL";

    // Primary is dBr (loopback reference); distortion curves are dBc (fundamental).
    private static string DistortionTrackerFormat(AnalysisCurveKind kind) =>
        kind == AnalysisCurveKind.Primary
            ? "{0}\n{2:0.0} Hz\n{4:0.00} dBr (vs reference)"
            : "{0}\n{2:0.0} Hz\n{4:0.00} dBc (vs fundamental)";

    /// <summary>Compare magnitude on the FR plot (primary only). On the SPL axis it uses its own loopback level; without an anchor it is omitted with a notice.</summary>
    private void AddCompareFrequencyResponse(
        PlotModel model,
        bool splRequested,
        bool splViewOnly)
    {
        if (TryCreateCompareMeasurement() is not { } compare)
        {
            return;
        }

        double? compareOffset = splRequested ? compare.SplOffsetDb : null;
        if (splRequested && compareOffset is null)
        {
            model.Annotations.Add(CreateCompareWithoutSplAnnotation(
                compare.DisplayName, splViewOnly));
            return;
        }

        IReadOnlyList<AnalysisCurve> compareCurves = DataHelper.GetSpectrum(
            compare.Measurement,
            frequencyResponseOptions,
            GetCalibration(frequencyResponseOptions),
            frequencyResponseVisibility.ToSpectrumCurves() & SpectrumCurves.Primary);
        if (compareOffset is { } offsetDb)
        {
            compareCurves = SplConversion.ToSoundPressureLevel(compareCurves, offsetDb);
        }

        foreach (AnalysisCurve curve in compareCurves)
        {
            AddCompareLineSeries(
                model,
                curve,
                compareOffset.HasValue ? SplTrackerFormat : DistortionTrackerFormat(curve.Kind),
                compare.DisplayName,
                Mode.FrequencyResponse);
        }
    }

    // A line below the main view-only notice so the two do not overprint.
    private static OverlayTextAnnotation CreateCompareWithoutSplAnnotation(
        string compareName,
        bool splViewOnly)
    {
        OverlayTextAnnotation note = CreateSplViewOnlyAnnotation(
            $"No SPL calibration for {compareName} — the compared curve is hidden in dB SPL");
        if (splViewOnly)
        {
            note.TextPosition = new DataPoint(note.TextPosition.X, note.TextPosition.Y + 1);
        }

        return note;
    }

    public PlotModel CreatePhaseResponse(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Phase Response"));

        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            const string phaseTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.0}\u00B0";
            IImpulseMeasurement primaryMeasurement =
                measurementContext.CreatePrimaryMeasurement();
            var compare = TryCreateCompareMeasurement();
            // Auto gate: re-snap to the current IR start in the shared options, so dialog and settings show what the plot used.
            if (phaseResponseOptions.PhaseGateAutoFit &&
                measurementContext.ResolveAutoGateOffsetMs() is { } phaseStartMs)
            {
                phaseResponseOptions.PhaseGateOffsetMs = phaseStartMs;
            }
            PhaseAnalysisSettings phaseSettings =
                phaseResponseOptions.CreatePhaseAnalysisSettings();
            if (phaseSettings.DetrendMode == PhaseDetrendMode.Auto && compare != null)
            {
                // One common time reference: resolve Auto from Main once, reuse as Manual for all curves so relative delay survives.
                double commonDetrend = DataHelper.ResolvePhaseDetrendMilliseconds(
                    primaryMeasurement,
                    phaseSettings);
                phaseSettings = phaseSettings with
                {
                    DetrendMode = PhaseDetrendMode.Manual,
                    ManualDetrendMilliseconds = commonDetrend
                };
            }

            if (phaseResponseVisibility.ShowMeasuredPhase)
            {
                AnalysisCurve curve = DataHelper.GetPhase(
                    primaryMeasurement,
                    phaseSettings,
                    measurementContext.Result?.TransferCoherence);

                // Tag representation so overlay math knows whether a difference must use the wrapped formula.
                AddLineSeries(
                    model,
                    curve,
                    phaseTrackerFormat,
                    Mode.PhaseResponse,
                    PhaseAxisKey,
                    phaseResponseOptions.Unwrap);
            }

            if (phaseResponseVisibility.ShowMinimumPhase)
            {
                AnalysisCurve minimumPhaseCurve = DataHelper.GetMinimumPhase(
                    primaryMeasurement,
                    phaseSettings);

                AddLineSeries(
                    model,
                    minimumPhaseCurve,
                    phaseTrackerFormat,
                    Mode.PhaseResponse,
                    PhaseAxisKey,
                    phaseUnwrapped: true);
            }

            if (phaseResponseVisibility.ShowExcessPhase)
            {
                AnalysisCurve excessPhaseCurve = DataHelper.GetExcessPhase(
                    primaryMeasurement,
                    phaseSettings,
                    measurementContext.Result?.TransferCoherence);

                AddLineSeries(
                    model,
                    excessPhaseCurve,
                    phaseTrackerFormat,
                    Mode.PhaseResponse,
                    PhaseAxisKey,
                    phaseUnwrapped: true);
            }

            // Compare shares gate length, window, detrend and smoothing; gate placement is per-curve under Auto (fronts differ).
            // Measured and excess phase need both records on one clock; minimum phase is magnitude-only (Bode) and always comparable.
            if (compare is { } compareData)
            {
                bool sharesTimeReference = CompareSharesATimeReference();
                PhaseAnalysisSettings comparePhaseSettings =
                    phaseResponseOptions.PhaseGateAutoFit &&
                    TransferIrStartCache.ResolveStartMs(compareData.Measurement)
                        is { } compareStartMs
                        ? phaseSettings with { GateOffsetMs = compareStartMs }
                        : phaseSettings;
                if (phaseResponseVisibility.ShowMeasuredPhase && sharesTimeReference)
                {
                    AnalysisCurve compareCurve = DataHelper.GetPhase(
                        compareData.Measurement,
                        comparePhaseSettings,
                        compareData.Coherence);
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        phaseTrackerFormat,
                        compareData.DisplayName,
                        Mode.PhaseResponse,
                        phaseResponseOptions.Unwrap);
                }

                if (phaseResponseVisibility.ShowMinimumPhase)
                {
                    AnalysisCurve compareCurve = DataHelper.GetMinimumPhase(
                        compareData.Measurement,
                        comparePhaseSettings);
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        phaseTrackerFormat,
                        compareData.DisplayName,
                        Mode.PhaseResponse,
                        phaseUnwrapped: true);
                }

                if (phaseResponseVisibility.ShowExcessPhase && sharesTimeReference)
                {
                    AnalysisCurve compareCurve = DataHelper.GetExcessPhase(
                        compareData.Measurement,
                        comparePhaseSettings,
                        compareData.Coherence);
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        phaseTrackerFormat,
                        compareData.DisplayName,
                        Mode.PhaseResponse,
                        phaseUnwrapped: true);
                }
            }

            AddMeasurementCoherenceIfAvailable(
                model,
                phaseResponseOptions,
                phaseResponseVisibility.ShowCoherence);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse &&
                 (phaseResponseVisibility.ShowMeasuredPhase ||
                  phaseResponseVisibility.ShowMinimumPhase ||
                  phaseResponseVisibility.ShowExcessPhase))
        {
            AddRequiresTransferIrAnnotation(model);
        }

        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.InsertAxis(model, 0, new LinearAxis
        {
            Key = PhaseAxisKey,
            Position = AxisPosition.Left,
            AbsoluteMinimum = -2880,
            AbsoluteMaximum = 2880,
            Minimum = -180,
            Maximum = 180,
            MajorStep = 45,
            MajorGridlineStyle = LineStyle.Solid,
            MinorStep = 15,
            MinorGridlineStyle = LineStyle.Dot,
        });
        return model;
    }

    public PlotModel CreateWaterfall(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateWaterfallModel(
            measurementContext.CreateTitle("Fourier Waterfall"),
            waterfallGenOptions);

        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            var waterfall = new WaterfallSeries()
            {
                BackgroundColor = UiPalette.WaterfallSurface.ToOxy(),
                GenerateOptions = waterfallGenOptions,
            };

            waterfall.FillFourierWaterfallData(measurementContext.CreatePrimaryMeasurement());
            model.Series.Add(waterfall);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        return model;
    }

    public PlotModel CreateGroupDelay(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Group Delay"));

        double minimum = +1000;
        double maximum = -1000;
        bool hasValidData = false;
        bool showAnyGroupDelayCurve =
            groupDelayVisibility.ShowGroupDelay ||
            groupDelayVisibility.ShowMinimumPhaseGroupDelay ||
            groupDelayVisibility.ShowExcessGroupDelay;
        bool includeMinimumPhase =
            groupDelayVisibility.ShowMinimumPhaseGroupDelay ||
            groupDelayVisibility.ShowExcessGroupDelay;
        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse &&
            (showAnyGroupDelayCurve || groupDelayVisibility.ShowCoherence))
        {
            const string groupDelayTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.000} ms";
            if (groupDelayOptions.GroupDelayGateAutoFit &&
                measurementContext.ResolveAutoGateOffsetMs() is { } gdStartMs)
            {
                groupDelayOptions.GroupDelayGateOffsetMs = gdStartMs;
            }
            if (showAnyGroupDelayCurve)
            {
                // Group delay reads absolute from the IR start; under FDW the gate is the outer limit, cycles counted after the left shoulder.
                IImpulseMeasurement measurement = measurementContext.CreatePrimaryMeasurement();
                PhaseAnalysisSettings windowSettings =
                    groupDelayOptions.CreateGroupDelayAnalysisSettings();
                GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
                    measurement,
                    windowSettings,
                    groupDelayOptions.SmoothingInverseOctaves,
                    GroupDelayMagnitudeGateDb,
                    includeMinimumPhase);
                if (groupDelayVisibility.ShowGroupDelay)
                {
                    AddLineSeries(
                        model,
                        curves.Measured,
                        groupDelayTrackerFormat,
                        Mode.GroupDelay,
                        GroupDelayAxisKey);
                }
                // Y fit ignores minimum/excess values: near band edges the cepstral reconstruction yields tens of ms that would flatten a 2-5 ms curve.
                // Measured range is the spike-free proxy for excess; showing minimum extends the range to zero.
                if (groupDelayVisibility.ShowGroupDelay ||
                    groupDelayVisibility.ShowExcessGroupDelay)
                {
                    UpdateGroupDelayRange(
                        curves.Measured, ref minimum, ref maximum, ref hasValidData);
                }
                if (groupDelayVisibility.ShowMinimumPhaseGroupDelay &&
                    curves.Minimum is { } minimumCurve)
                {
                    AddLineSeries(
                        model,
                        minimumCurve,
                        groupDelayTrackerFormat,
                        Mode.GroupDelay,
                        GroupDelayAxisKey);
                    if (hasValidData)
                    {
                        minimum = Math.Min(minimum, 0.0);
                        maximum = Math.Max(maximum, 0.0);
                    }
                }
                if (groupDelayVisibility.ShowExcessGroupDelay &&
                    curves.Excess is { } excessCurve)
                {
                    AddLineSeries(
                        model,
                        excessCurve,
                        groupDelayTrackerFormat,
                        Mode.GroupDelay,
                        GroupDelayAxisKey);
                }

                // Measured and excess read absolute from the IR start, so they need a shared clock; minimum phase carries no bulk delay.
                bool sharesTimeReference = CompareSharesATimeReference();
                if ((sharesTimeReference ||
                        groupDelayVisibility.ShowMinimumPhaseGroupDelay) &&
                    TryCreateCompareMeasurement() is { } compare)
                {
                    double compareGateOffsetMs =
                        groupDelayOptions.GroupDelayGateAutoFit &&
                        TransferIrStartCache.ResolveStartMs(compare.Measurement)
                            is { } compareStartMs
                            ? compareStartMs
                            : groupDelayOptions.GroupDelayGateOffsetMs;
                    GroupDelayCurveSet compareCurves = DataHelper.GetGroupDelayCurves(
                        compare.Measurement,
                        windowSettings with { GateOffsetMs = compareGateOffsetMs },
                        groupDelayOptions.SmoothingInverseOctaves,
                        GroupDelayMagnitudeGateDb,
                        includeMinimumPhase);
                    // Y fit driven by Main only, so Compare extremes do not make the scale jump on every gate edit.
                    if (groupDelayVisibility.ShowGroupDelay && sharesTimeReference)
                    {
                        AddCompareLineSeries(
                            model,
                            compareCurves.Measured,
                            groupDelayTrackerFormat,
                            compare.DisplayName,
                            Mode.GroupDelay);
                    }
                    if (groupDelayVisibility.ShowMinimumPhaseGroupDelay &&
                        compareCurves.Minimum is { } compareMinimumCurve)
                    {
                        AddCompareLineSeries(
                            model,
                            compareMinimumCurve,
                            groupDelayTrackerFormat,
                            compare.DisplayName,
                            Mode.GroupDelay);
                    }
                    if (groupDelayVisibility.ShowExcessGroupDelay &&
                        sharesTimeReference &&
                        compareCurves.Excess is { } compareExcessCurve)
                    {
                        AddCompareLineSeries(
                            model,
                            compareExcessCurve,
                            groupDelayTrackerFormat,
                            compare.DisplayName,
                            Mode.GroupDelay);
                    }
                }
            }

            AddMeasurementCoherenceIfAvailable(
                model,
                groupDelayOptions,
                groupDelayVisibility.ShowCoherence);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse &&
                 showAnyGroupDelayCurve)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        PlotModelStyle.AddFrequencyAxis(model);
        var msAxis = new LinearAxis
        {
            Key = GroupDelayAxisKey,
            Position = AxisPosition.Left,
            AbsoluteMinimum = -30,
            AbsoluteMaximum = 30,
            Minimum = -5,
            Maximum = 5,
            MajorStep = 1,
            MajorGridlineStyle = LineStyle.Solid,
            Title = "ms"
        };
        if (hasValidData)
        {
            msAxis.Minimum = minimum - 2;
            msAxis.Maximum = maximum + 2;
        }
        PlotModelStyle.InsertAxis(model, 0, msAxis);
        return model;
    }

    public PlotModel CreateBurstDecay(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateWaterfallModel(
            measurementContext.CreateTitle("Burst Decay"),
            burstDecayGenOptions);

        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            var waterfall = new WaterfallSeries()
            {
                BackgroundColor = UiPalette.WaterfallSurface.ToOxy(),
                GenerateOptions = burstDecayGenOptions,
            };

            waterfall.FillFourierWaterfallData(measurementContext.CreatePrimaryMeasurement());
            model.Series.Add(waterfall);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        return model;
    }

    /// <summary>Framing of this build, so stored overlays redraw under the framing on screen now.</summary>
    public ImpulseOverlayFrame ImpulseFrame { get; private set; }

    public PlotModel CreateImpulseResponse(bool includeCurves)
    {
        ImpulseResponseOptions opt = impulseResponseOptions;
        ImpulseFrame = new ImpulseOverlayFrame(
            opt, 0.0, null, AnalysisSampleRate);
        // A band-limited view is not the record; the title says so.
        string band = ImpulseBandLabel(opt, AnalysisSampleRate);
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Impulse Response" + band));

        bool anyTrace = opt.ShowImpulse || opt.ShowEnvelope || opt.ShowStep;
        var drawn = new List<AnalysisCurve?>();
        var stepCurves = new List<AnalysisCurve?>();
        (double Start, double End)? defaultSpan = null;
        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            // Time zero and level normalization are resolved once from Main for both sets (per-curve would cancel the compared difference),
            // and regardless of visibility, since an overlay may be the only thing on the plot.
            IImpulseMeasurement main = measurementContext.CreatePrimaryMeasurement();
            double origin = ResolveImpulseOriginSamples(main);
            defaultSpan = ResolveImpulseDefaultSpan(main, opt, origin);
            ImpulseCurveSet mainSet = DataHelper.GetImpulseCurves(
                main, opt, new ImpulseRenderFrame(origin));
            ImpulseFrame = new ImpulseOverlayFrame(
                opt, origin, mainSet.PeakReference, main.SampleRate);

            if (anyTrace)
            {
                AddImpulseSeries(
                    model, mainSet, main.SampleRate, origin, null, drawn, stepCurves);

                if (CompareSharesATimeReference() &&
                    TryCreateCompareMeasurement() is { } compare)
                {
                    ImpulseCurveSet compareSet = DataHelper.GetImpulseCurves(
                        compare.Measurement,
                        opt,
                        new ImpulseRenderFrame(origin, mainSet.PeakReference));
                    AddImpulseSeries(
                        model,
                        compareSet,
                        compare.Measurement.SampleRate,
                        origin,
                        compare.DisplayName,
                        drawn,
                        stepCurves);
                }

                AddImpulseMarkers(model, main, mainSet, origin);
            }
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse &&
                 anyTrace)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        var timeAxis = new LinearAxis
        {
            Key = TimeAxisKey,
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Solid,
            Title = opt.TimeUnit == ImpulseTimeUnit.Milliseconds ? "ms" : "samples",
        };
        // The step never shares the level axis, so when alone it takes that axis.
        bool stepOnLeft = ImpulseStepIsAlone(opt);
        var valueAxis = new LinearAxis
        {
            Key = stepOnLeft ? ImpulseStepAxisKey : ImpulseAxisKey,
            Position = AxisPosition.Left,
            Title = stepOnLeft ? "step" : ImpulseValueUnit(opt.AmplitudeScale),
        };
        // Bounded by the record; opens on Length of tail past the peak. Compare included so it stays on screen.
        ApplyCurveRange(
            timeAxis, point => point.X, drawn.Concat(stepCurves).ToArray());
        ApplyDefaultImpulseSpan(timeAxis, opt, defaultSpan);
        // Level is not framed: overlays attach after build and explicit bounds would put a louder snapshot off screen.
        ApplyDecibelFloor(valueAxis, opt, stepOnLeft, drawn);
        PlotModelStyle.AddAxis(model, timeAxis);
        PlotModelStyle.AddAxis(model, valueAxis);

        // Counterpart axis always exists: an overlay binding by axis key would otherwise fail the redraw.
        var counterpartAxis = new LinearAxis
        {
            Key = stepOnLeft ? ImpulseAxisKey : ImpulseStepAxisKey,
            Position = AxisPosition.Right,
            Title = stepOnLeft ? ImpulseValueUnit(opt.AmplitudeScale) : "step",
            IsAxisVisible = !stepOnLeft && stepCurves.Count > 0,
        };
        PlotModelStyle.AddAxis(model, counterpartAxis);
        return model;
    }

    private static bool ImpulseStepIsAlone(ImpulseResponseOptions opt) =>
        opt.ShowStep && !opt.ShowImpulse && !opt.ShowEnvelope;

    /// <summary>Opening span: record start to peak plus Length. A deconvolved record runs for seconds of noise.</summary>
    private static (double Start, double End)? ResolveImpulseDefaultSpan(
        IImpulseMeasurement measurement,
        ImpulseResponseOptions opt,
        double origin)
    {
        int available = measurement.ImpulseResponse?.Length ?? 0;
        if (available <= 0)
        {
            return null;
        }

        double end = Math.Min(available - 1, measurement.PeakIndex + (double)opt.Length);
        double ToAxis(double sample) =>
            opt.TimeUnit == ImpulseTimeUnit.Milliseconds && measurement.SampleRate > 0
                ? (sample - origin) * 1000.0 / measurement.SampleRate
                : sample - origin;
        return (ToAxis(0), ToAxis(end));
    }

    private static void ApplyDefaultImpulseSpan(
        LinearAxis axis,
        ImpulseResponseOptions opt,
        (double Start, double End)? span)
    {
        if (span is not { } bounds || bounds.End <= bounds.Start)
        {
            return;
        }

        axis.Minimum = Math.Max(axis.AbsoluteMinimum, bounds.Start);
        axis.Maximum = Math.Min(axis.AbsoluteMaximum, bounds.End);
    }

    private static string ImpulseBandLabel(ImpulseResponseOptions opt, int sampleRate)
    {
        if (!opt.HasBandFilter(sampleRate))
        {
            return string.Empty;
        }

        string centre = opt.BandCenterHz >= 1_000.0
            ? $"{opt.BandCenterHz / 1_000.0:0.###} kHz"
            : $"{opt.BandCenterHz:0.#} Hz";
        string width = Math.Abs(opt.BandFilterOctaves - 1.0) < 1e-9
            ? "1 octave"
            : $"1/{1.0 / opt.BandFilterOctaves:0.#} octave";
        return $" — {centre} {width}";
    }

    // Opens 100 dB under the loudest point; zero crossings dive to -160 dB. Absolute range still covers the floor.
    private const double ImpulseDecibelWindow = 100.0;

    // Only the dB floor is pinned; the top follows data so a louder overlay lifts it.
    private static void ApplyDecibelFloor(
        LinearAxis axis,
        ImpulseResponseOptions opt,
        bool stepOnLeft,
        IReadOnlyList<AnalysisCurve?> curves)
    {
        if (stepOnLeft || opt.AmplitudeScale != ImpulseAmplitudeScale.Decibels)
        {
            return;
        }

        double maximum = double.NegativeInfinity;
        foreach (AnalysisCurve? curve in curves)
        {
            if (curve == null)
            {
                continue;
            }

            foreach (SignalPoint point in curve.Points)
            {
                if (double.IsFinite(point.Y))
                {
                    maximum = Math.Max(maximum, point.Y);
                }
            }
        }

        if (double.IsFinite(maximum))
        {
            axis.Minimum = maximum - ImpulseDecibelWindow;
        }
    }

    private static string ImpulseValueUnit(ImpulseAmplitudeScale scale) =>
        scale switch
        {
            ImpulseAmplitudeScale.Decibels => "dB",
            ImpulseAmplitudeScale.PercentOfPeak => "%",
            _ => string.Empty
        };

    /// <summary>View zero in samples from record start; first-arrival uses the same estimate as the Auto gate offsets.</summary>
    private double ResolveImpulseOriginSamples(IImpulseMeasurement measurement) =>
        impulseResponseOptions.TimeOrigin switch
        {
            ImpulseTimeOrigin.Peak => measurement.PeakIndex,
            ImpulseTimeOrigin.FirstArrival =>
                TransferIrStartCache.ResolveStartMs(measurement) is { } startMs &&
                measurement.SampleRate > 0
                    ? startMs * measurement.SampleRate / 1000.0
                    : measurement.PeakIndex,
            _ => 0.0
        };

    private void AddImpulseSeries(
        PlotModel model,
        ImpulseCurveSet set,
        int sampleRate,
        double origin,
        string? compareName,
        List<AnalysisCurve?> drawn,
        List<AnalysisCurve?> stepCurves)
    {
        bool relative = impulseResponseOptions.TimeOrigin != ImpulseTimeOrigin.RecordStart;
        string unit = ImpulseValueUnit(impulseResponseOptions.AmplitudeScale);

        foreach (AnalysisCurve? curve in new[] { set.Impulse, set.Envelope, set.Step })
        {
            if (curve == null)
            {
                continue;
            }

            bool isStep = curve.Kind == AnalysisCurveKind.ImpulseStep;
            var series = new ImpulseLineSeries
            {
                SampleRate = sampleRate,
                TimeUnit = impulseResponseOptions.TimeUnit,
                TimeIsRelative = relative,
                ValueUnit = isStep ? string.Empty : unit,
                Color = OxyPlotAdapter.GetCurveColor(curve.Kind),
                Title = compareName == null ? curve.Name : $"{curve.Name} · {compareName}",
                YAxisKey = isStep ? ImpulseStepAxisKey : ImpulseAxisKey,
                Tag = new CurveTag(
                    Mode.ImpulseResponse,
                    curve.Kind,
                    compareName == null ? CurveSource.Main : CurveSource.Compare),
            };
            series.Points.AddRange(OxyPlotAdapter.ToDataPoints(curve.Points));
            if (compareName != null)
            {
                series.LineStyle = LineStyle.Dash;
                series.StrokeThickness = 1.5;
                OxyColor color = series.Color;
                series.Color = OxyColor.FromArgb(150, color.R, color.G, color.B);
            }

            model.Series.Add(series);
            (isStep ? stepCurves : drawn).Add(curve);
        }
    }

    // Marks the first arrival (anchor of every Auto gate offset) and the strongest peak: the instants the engine acts on.
    private void AddImpulseMarkers(
        PlotModel model,
        IImpulseMeasurement measurement,
        ImpulseCurveSet set,
        double origin)
    {
        double ToAxis(double sample) =>
            impulseResponseOptions.TimeUnit == ImpulseTimeUnit.Milliseconds &&
            measurement.SampleRate > 0
                ? (sample - origin) * 1000.0 / measurement.SampleRate
                : sample - origin;

        // Captions stacked along their lines, not by opposite anchors: arrival and peak are often the same pixel, and a bottom anchor clipped at the top edge.
        string valueAxisKey = ImpulseStepIsAlone(impulseResponseOptions)
            ? ImpulseStepAxisKey
            : ImpulseAxisKey;
        if (TransferIrStartCache.ResolveStartMs(measurement) is { } startMs &&
            measurement.SampleRate > 0)
        {
            AddImpulseMarker(
                model,
                ToAxis(startMs * measurement.SampleRate / 1000.0),
                "arrival",
                UiPalette.CurveExcessPhase.ToOxy(),
                ArrivalLabelPosition,
                valueAxisKey);
        }

        // With a band selected the caption names the band peak and states its offset from the arrival.
        string peakName = impulseResponseOptions.HasBandFilter(measurement.SampleRate)
            ? "band peak"
            : "peak";
        string peakLabel = ResolveBandArrivalOffset(measurement, set) is { } offset
            ? $"{peakName} · {offset:+0.00;-0.00} ms after arrival"
            : peakName;
        if (set.SnrDb is { } snr)
        {
            peakLabel += $" · SNR {snr:0} dB";
        }

        AddImpulseMarker(
            model,
            ToAxis(set.PeakSample),
            peakLabel,
            UiPalette.MarkerPeak.ToOxy(),
            PeakLabelPosition,
            valueAxisKey);
    }

    /// <summary>Band peak delay after the record's arrival, ms. Null without band or arrival, or when the centre lies outside the record's dominant band:
    /// a tweeter's leakage "peak" at 63 Hz landed 1.3-23.6 s late; level and SNR did not separate those. Not a distance: it is build-up and GD, not air path.</summary>
    private double? ResolveBandArrivalOffset(
        IImpulseMeasurement measurement,
        ImpulseCurveSet set)
    {
        if (!impulseResponseOptions.HasBandFilter(measurement.SampleRate) ||
            TransferIrStartCache.ResolveStartMs(measurement) is not { } startMs ||
            !TransferIrDominantBandCache.Covers(
                measurement, impulseResponseOptions.BandCenterHz))
        {
            return null;
        }

        return set.PeakSample * 1000.0 / measurement.SampleRate - startMs;
    }

    // Fractions from the plot bottom; both captions hang downwards so the top edge cannot cut them.
    private const double ArrivalLabelPosition = 1.0;
    private const double PeakLabelPosition = 0.955;

    private static void AddImpulseMarker(
        PlotModel model,
        double x,
        string text,
        OxyColor color,
        double textLinePosition,
        string valueAxisKey)
    {
        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            X = x,
            Color = OxyColor.FromAColor(140, color),
            LineStyle = LineStyle.Dash,
            StrokeThickness = 1.0,
            Text = text,
            TextColor = color,
            TextOrientation = AnnotationTextOrientation.Horizontal,
            TextLinePosition = textLinePosition,
            TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
            TextPadding = 4,
            XAxisKey = TimeAxisKey,
            YAxisKey = valueAxisKey,
        });
    }

    // Sets both visible and absolute bounds to the curve's range; a flat range gets a margin.
    private static void ApplyCurveRange(
        LinearAxis axis,
        Func<SignalPoint, double> selector,
        params AnalysisCurve?[] curves)
    {
        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        foreach (AnalysisCurve? curve in curves)
        {
            if (curve == null)
            {
                continue;
            }

            foreach (SignalPoint point in curve.Points)
            {
                double value = selector(point);
                if (!double.IsFinite(value))
                {
                    continue;
                }

                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }

        if (!double.IsFinite(minimum) || !double.IsFinite(maximum))
        {
            return;
        }

        if (maximum - minimum < 1e-9)
        {
            minimum -= 0.5;
            maximum += 0.5;
        }

        axis.Minimum = minimum;
        axis.Maximum = maximum;
        axis.AbsoluteMinimum = minimum;
        axis.AbsoluteMaximum = maximum;
    }

    public PlotModel CreateAutocorrelation(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Autocorrelation"));

        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse &&
            impulseResponseOptions.ShowAutocorrelation)
        {
            AnalysisCurve curve =
                DataHelper.GetAutocorrelation(
                    measurementContext.CreatePrimaryMeasurement(),
                    impulseResponseOptions);
            AddLineSeries(
                model,
                curve,
                "{0}\n{2:0.000} ms\n{4:0.000}",
                Mode.Autocorrelation,
                AutocorrelationAxisKey);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse &&
                 impulseResponseOptions.ShowAutocorrelation)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Key = TimeAxisKey,
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Solid,
            Title = "ms"
        });
        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Key = AutocorrelationAxisKey,
            Position = AxisPosition.Left,
        });
        return model;
    }

    private bool LiveRtaOnly => EffectiveLiveAnalysisMode.IsReferenceFree();

    /// <param name="scaleOverride">Axis for a STORED capture, whose levels follow the anchor at capture time. Null follows the live state.</param>
    public PlotModel CreateLiveSpectrum(MagnitudeScale? scaleOverride = null)
    {
        // An active tilt compensation is named in the title: the level is reshaped by the excitation spectrum.
        bool renderSpl =
            (scaleOverride ?? EffectiveLiveSpectrumScale) == MagnitudeScale.SoundPressureLevel;
        bool rtaOnly = LiveRtaOnly;
        bool mmm = EffectiveLiveAnalysisMode.IsSpatialAverageCapture();
        string tiltSuffix = LiveTiltModel != null ? " (noise-compensated)" : "";
        // An unanchored MMM capture is a valid spatial average but must not pass for absolute.
        PlotModel model = PlotModelStyle.CreateTitledModel(
            mmm
                ? (renderSpl
                    ? "Live Spectrum — MMM, dB SPL"
                    : "Live Spectrum — MMM, relative (no SPL anchor)") + tiltSuffix
                : renderSpl
                    ? "Live Spectrum — dB SPL" + tiltSuffix
                    : rtaOnly
                        ? "Live Spectrum (RTA)" + tiltSuffix
                        : "Live Transfer Function");

        PlotModelStyle.AddFrequencyAxis(model);
        if (renderSpl)
        {
            PlotModelStyle.AddDecibelAxis(
                model,
                "dB SPL",
                PlotModelStyle.SplDecibelMinimum,
                PlotModelStyle.SplDecibelMaximum,
                PlotModelStyle.SplDecibelAbsoluteMinimum,
                PlotModelStyle.SplDecibelAbsoluteMaximum);
        }
        else
        {
            PlotModelStyle.AddDecibelAxis(model);
            if (!rtaOnly && liveSpectrumOptions.ShowCoherence)
            {
                AddCoherenceAxis(model);
            }
        }

        return model;
    }

    private string LiveMagnitudeTracker() =>
        EffectiveLiveSpectrumScale == MagnitudeScale.SoundPressureLevel
            ? "{0}\n{2:0.0} Hz\n{4:0.00} dB SPL"
            : "{0}\n{2:0.0} Hz\n{4:0.00} dB";

    public LineSeries BuildNoiseSeries(double[] accumulatedData)
    {
        var series = new LineSeries
        {
            Color = UiPalette.CurveLiveTransfer.ToOxy(),
            Title = "Live Transfer Function",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        UpdateNoiseSeries(series, accumulatedData);
        return series;
    }

    // Refill in place at ~30 fps to avoid re-allocating plot objects.
    public void UpdateNoiseSeries(LineSeries series, double[] magnitude) =>
        FillPoints(series, ResampleLiveSpectrumMagnitude(magnitude));

    /// <summary>A stored capture drawn as captured, not re-rendered from its bins: viewing must show what the author saw.</summary>
    public LineSeries BuildLoadedCaptureSeries(LiveCaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var series = new LineSeries
        {
            Color = UiPalette.CurveLiveInput.ToOxy(),
            Title = string.IsNullOrWhiteSpace(document.Title)
                ? "Loaded capture"
                : document.Title,
            // The document's unit: an unanchored capture is relative regardless of this machine's calibration.
            TrackerFormatString =
                document.Recipe.MagnitudeScale == MagnitudeScale.SoundPressureLevel
                    ? "{0}\n{2:0.0} Hz\n{4:0.00} dB SPL"
                    : "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };

        FillPoints(series, document.ToCurvePoints());
        return series;
    }

    public LineSeries BuildInputMagnitudeSeries(double[] inputMagnitude)
    {
        var series = new LineSeries
        {
            Color = UiPalette.CurveLiveInput.ToOxy(),
            Title = "Input Spectrum (RTA)"
        };
        UpdateInputMagnitudeSeries(series, inputMagnitude);
        return series;
    }

    // The RTA is the one live curve with an honest absolute level: in SPL it is band-power integrated (FFT-size independent) and offset.
    public void UpdateInputMagnitudeSeries(LineSeries series, double[] inputMagnitude)
    {
        series.TrackerFormatString = LiveMagnitudeTracker();
        FillPoints(series, ResampleLiveRta(inputMagnitude));
    }

    // Peak-hold accumulates over display points: per-bin maxima summed per band would overstate peak band power.
    public List<SignalPoint> BuildMainDisplayPoints(double[] magnitude, bool rtaOnly) =>
        rtaOnly
            ? ResampleLiveRta(magnitude)
            : ResampleLiveSpectrumMagnitude(magnitude, 0.0);

    public LineSeries BuildPeakHoldSeries(List<SignalPoint> peakHoldPoints)
    {
        var series = new LineSeries
        {
            Color = OxyColor.FromAColor(170, UiPalette.CurvePeakHold.ToOxy()),
            LineStyle = LineStyle.Solid,
            StrokeThickness = 1.0,
            Title = "Peak Hold"
        };
        UpdatePeakHoldSeries(series, peakHoldPoints);
        return series;
    }

    public void UpdatePeakHoldSeries(LineSeries series, List<SignalPoint> peakHoldPoints)
    {
        series.TrackerFormatString = LiveMagnitudeTracker();
        FillPoints(series, peakHoldPoints);
    }

    // SPL: band-integrated power with per-band calibration and offset; native: amplitude-averaged dB.
    // Tilt compensation applies per bin (native) or per band (SPL), whose band laws differ (see NoiseTiltCompensation).
    /// <summary>What the band render baked in, reported by the render itself (compensation may be skipped on length mismatch).</summary>
    private sealed class LiveRtaApplied
    {
        public double[] TiltDb { get; set; } = [];

        /// <summary>Protective high-pass divided out, dB per point; NaN where unrecoverable.</summary>
        public double[] ProtectiveHighPassDb { get; set; } = [];

        /// <summary>Mic correction per point, sign convention of <see cref="CalibrationFile.GetDecibelCorrection"/>; the render subtracts it.</summary>
        public double[] CalibrationDb { get; set; } = [];
    }

    private List<SignalPoint> ResampleLiveRta(
        double[] amplitudeSpectrum,
        LiveRtaApplied? applied = null)
    {
        NoiseSpectralModel? tiltModel = LiveTiltModel;
        if (!LiveUsesBandPower)
        {
            return ResampleLiveSpectrumMagnitude(amplitudeSpectrum, 0.0, tiltModel);
        }

        int smoothingCode = EffectiveLiveSmoothingCode;
        double smoothingOctaves = SpectrumSmoothing.SmoothingOctaves(smoothingCode);
        bool psychoacoustic = SpectrumSmoothing.IsPsychoacoustic(smoothingCode);
        List<SignalPoint> bands = DataHelper.LogarithmicPowerBandResample(
            amplitudeSpectrum,
            noiseMeasurement.SequenceLength,
            noiseMeasurement.SampleRate,
            noiseMeasurement.AnalysisWindowEnbwBins,
            noiseMeasurement.AnalysisWindowMainLobeBins,
            20,
            20000,
            1024,
            smoothingOctaves,
            psychoacoustic);

        double offsetDb = LiveSplRenderOffset;
        CalibrationFile? calibration = LiveCaptureCalibration;
        double[]? recordedCorrection =
            applied != null && calibration != null ? new double[bands.Count] : null;
        for (int i = 0; i < bands.Count; i++)
        {
            double correction = calibration?.GetDecibelCorrection(bands[i].X) ?? 0.0;
            if (recordedCorrection != null)
            {
                recordedCorrection[i] = correction;
            }

            bands[i] = new SignalPoint(bands[i].X, bands[i].Y - correction + offsetDb);
        }

        if (applied != null && recordedCorrection != null)
        {
            applied.CalibrationDb = recordedCorrection;
        }

        // The protective HP sits ahead of the speaker, so an MMM capture carries it; divide it out to match swept IRs (plain RTA keeps it).
        // Read from the accumulation: the filter in force during the walk, same field as the saved recipe.
        ProtectiveHighPassConfiguration captureFilter =
            noiseMeasurement.CaptureProtectiveHighPass;
        if (EffectiveLiveAnalysisMode.IsSpatialAverageCapture() && captureFilter.Enabled)
        {
            double[] filter = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
                captureFilter.ToEdge(),
                noiseMeasurement.SampleRate,
                ProtectiveHighPassConfiguration.MaximumCompensationBoostDb,
                bands.Select(band => band.X).ToArray());
            for (int i = 0; i < bands.Count; i++)
            {
                bands[i] = new SignalPoint(bands[i].X, bands[i].Y + filter[i]);
            }

            if (applied != null)
            {
                applied.ProtectiveHighPassDb = filter;
            }
        }

        if (tiltModel is { } bandModel)
        {
            // Same resampler and parameters, so grids align by index; a length mismatch means divergence, so skip.
            double[] compensation = LiveTiltBandCompensation(
                bandModel, amplitudeSpectrum.Length, smoothingOctaves, psychoacoustic);
            if (compensation.Length == bands.Count)
            {
                for (int i = 0; i < bands.Count; i++)
                {
                    bands[i] = new SignalPoint(bands[i].X, bands[i].Y + compensation[i]);
                }

                if (applied != null)
                {
                    applied.TiltDb = compensation;
                }
            }
        }

        return bands;
    }

    // One full render of the analytic noise spectrum per call, too heavy per tick; memoized on its parameters.
    private double[]? liveTiltBandCompensation;
    private (NoiseSpectralModel Model, int BinCount, int FftLength, int SampleRate,
        double EnbwBins, double MainLobeBins, double SmoothingOctaves, bool Psycho)
        liveTiltBandKey;

    private double[] LiveTiltBandCompensation(
        NoiseSpectralModel model,
        int binCount,
        double smoothingOctaves,
        bool psychoacoustic)
    {
        var key = (model, binCount, noiseMeasurement.SequenceLength,
            noiseMeasurement.SampleRate, noiseMeasurement.AnalysisWindowEnbwBins,
            noiseMeasurement.AnalysisWindowMainLobeBins, smoothingOctaves, psychoacoustic);
        if (liveTiltBandCompensation == null || !key.Equals(liveTiltBandKey))
        {
            liveTiltBandCompensation = NoiseTiltCompensation.BandCompensationDb(
                model,
                binCount,
                noiseMeasurement.SequenceLength,
                noiseMeasurement.SampleRate,
                noiseMeasurement.AnalysisWindowEnbwBins,
                noiseMeasurement.AnalysisWindowMainLobeBins,
                20,
                20000,
                1024,
                smoothingOctaves,
                psychoacoustic);
            liveTiltBandKey = key;
        }

        return liveTiltBandCompensation;
    }

    private static void FillPoints(LineSeries series, List<SignalPoint> points)
    {
        series.Points.Clear();
        foreach (SignalPoint point in points)
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
        }
    }

    private List<SignalPoint> ResampleLiveSpectrumMagnitude(
        double[] magnitude,
        double offsetDb = 0.0,
        NoiseSpectralModel? tiltCompensationModel = null)
    {
        List<SignalPoint> bins = DataHelper.MagnitudeBinsToDecibels(
            magnitude, noiseMeasurement.SequenceLength, noiseMeasurement.SampleRate, offsetDb);

        // Per bin before resample, where LiveRtaRawCapture bakes it, so re-smoothing a raw capture reproduces this trace.
        if (tiltCompensationModel is { } model)
        {
            for (int i = 0; i < bins.Count; i++)
            {
                bins[i] = new SignalPoint(
                    bins[i].X,
                    bins[i].Y + NoiseTiltCompensation.BinCompensationDb(
                        model, bins[i].X, noiseMeasurement.SampleRate));
            }
        }

        return DataHelper.LogarithmicResample(
            bins,
            20,
            20000,
            1024,
            LiveCaptureCalibration,
            SpectrumSmoothing.SmoothingOctaves(EffectiveLiveSmoothingCode),
            psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(
                EffectiveLiveSmoothingCode));
    }

    public LineSeries BuildCoherenceSeries(double[] coherence)
    {
        return BuildCoherenceSeries(
            coherence,
            noiseMeasurement.SampleRate,
            noiseMeasurement.SequenceLength,
            liveSpectrumOptions.SmoothingInverseOctaves);
    }

    public void UpdateCoherenceSeries(LineSeries series, double[] coherence) =>
        FillPoints(series, ResampleCoherence(
            coherence,
            noiseMeasurement.SampleRate,
            noiseMeasurement.SequenceLength,
            liveSpectrumOptions.SmoothingInverseOctaves));

    private LineSeries BuildCoherenceSeries(
        double[] coherence,
        int sampleRate,
        int fftLength,
        double smoothingInverseOctaves)
    {
        var series = new LineSeries
        {
            Color = OxyColor.FromAColor(150, UiPalette.CurveCoherence.ToOxy()),
            Title = "Coherence",
            XAxisKey = FrequencyAxisKey,
            YAxisKey = CoherenceAxisKey,
            StrokeThickness = 1,
            LineStyle = LineStyle.Dash,
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} \u03B3\u00B2"
        };
        FillPoints(series, ResampleCoherence(
            coherence,
            sampleRate,
            fftLength,
            smoothingInverseOctaves));
        return series;
    }

    /// <summary>Trusted and low-coherence (dimmed, dashed) segments sharing boundary points.</summary>
    public (LineSeries Trusted, LineSeries Untrusted) BuildNoiseSeriesSegmented(
        double[] magnitude,
        double[] coherence,
        int thresholdPercent)
    {
        var trusted = new LineSeries
        {
            Color = UiPalette.CurveLiveTransfer.ToOxy(),
            Title = "Live Transfer Function",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        var untrusted = new LineSeries
        {
            Color = OxyColor.FromAColor(140, UiPalette.CurveMuted.ToOxy()),
            LineStyle = LineStyle.Dash,
            StrokeThickness = 1.0,
            Title = "Low coherence",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        UpdateNoiseSeriesSegmented(trusted, untrusted, magnitude, coherence, thresholdPercent);
        return (trusted, untrusted);
    }

    public void UpdateNoiseSeriesSegmented(
        LineSeries trusted,
        LineSeries untrusted,
        double[] magnitude,
        double[] coherence,
        int thresholdPercent)
    {
        List<SignalPoint> magnitudePoints = ResampleLiveSpectrumMagnitude(magnitude);
        List<SignalPoint> coherencePoints = ResampleCoherence(
            coherence,
            noiseMeasurement.SampleRate,
            noiseMeasurement.SequenceLength,
            liveSpectrumOptions.SmoothingInverseOctaves);
        int count = magnitudePoints.Count;
        double threshold = thresholdPercent / 100.0;

        // Different grids, so match coherence by frequency, not index. Missing coverage counts as trusted.
        var trustedFlags = new bool[count];
        int cursor = 0;
        for (int i = 0; i < count; i++)
        {
            trustedFlags[i] =
                NearestCoherence(coherencePoints, magnitudePoints[i].X, ref cursor) >=
                threshold;
        }

        bool IsTrusted(int index) => trustedFlags[index];

        trusted.Points.Clear();
        untrusted.Points.Clear();
        for (int i = 0; i < count; i++)
        {
            bool trustedHere = IsTrusted(i);
            bool boundary =
                (i > 0 && IsTrusted(i - 1) != trustedHere) ||
                (i < count - 1 && IsTrusted(i + 1) != trustedHere);
            double frequency = magnitudePoints[i].X;
            double decibels = magnitudePoints[i].Y;

            trusted.Points.Add(new DataPoint(
                frequency,
                trustedHere || boundary ? decibels : double.NaN));
            untrusted.Points.Add(new DataPoint(
                frequency,
                !trustedHere || boundary ? decibels : double.NaN));
        }
    }

    // Both lists sorted by X; a forward cursor keeps pairing linear.
    private static double NearestCoherence(
        List<SignalPoint> coherencePoints,
        double frequency,
        ref int cursor)
    {
        if (coherencePoints.Count == 0)
        {
            return 1.0;
        }

        while (cursor + 1 < coherencePoints.Count &&
            coherencePoints[cursor + 1].X <= frequency)
        {
            cursor++;
        }

        double value = coherencePoints[cursor].Y;
        if (cursor + 1 < coherencePoints.Count &&
            coherencePoints[cursor + 1].X - frequency < frequency - coherencePoints[cursor].X)
        {
            value = coherencePoints[cursor + 1].Y;
        }

        return value;
    }

    private List<SignalPoint> ResampleCoherence(
        double[] coherence,
        int sampleRate,
        int fftLength,
        double smoothingInverseOctaves)
    {
        if (sampleRate <= 0 || fftLength <= 0)
        {
            return [];
        }

        int binCount = Math.Min(fftLength / 2 + 1, coherence.Length);
        double binWidth = (double)sampleRate / fftLength;
        double nyquist = sampleRate / 2.0;
        double minFrequency = Math.Max(20.0, binWidth);
        double maxFrequency = Math.Min(20000.0, nyquist);
        if (binCount <= 1 || maxFrequency <= minFrequency)
        {
            return [];
        }

        const int targetCount = 1024;
        double logMin = Math.Log10(minFrequency);
        double logMax = Math.Log10(maxFrequency);
        double logStep = (logMax - logMin) / (targetCount - 1);
        double halfStepScale = Math.Pow(10.0, logStep * 0.5) - 1.0;
        List<SignalPoint> points = new(targetCount);

        for (int i = 0; i < targetCount; i++)
        {
            double frequency = Math.Pow(10.0, logMin + logStep * i);
            double halfStep = frequency * halfStepScale;
            int startBin = Math.Max(1, (int)Math.Floor((frequency - halfStep) / binWidth));
            int endBin = Math.Min(binCount - 1, (int)Math.Ceiling((frequency + halfStep) / binWidth));
            if (endBin < startBin)
            {
                int nearestBin = Math.Clamp((int)Math.Round(frequency / binWidth), 1, binCount - 1);
                double value = Math.Clamp(coherence[nearestBin], 0.0, 1.0);
                points.Add(new SignalPoint(frequency, value));
                continue;
            }

            double sum = 0.0;
            int count = 0;
            for (int bin = startBin; bin <= endBin; bin++)
            {
                double value = coherence[bin];
                if (double.IsFinite(value))
                {
                    sum += value;
                    count++;
                }
            }

            if (count > 0)
            {
                points.Add(new SignalPoint(
                    frequency,
                    Math.Clamp(sum / count, 0.0, 1.0)));
            }
        }

        return SmoothCoherencePoints(points, smoothingInverseOctaves);
    }

    private static List<SignalPoint> SmoothCoherencePoints(
        List<SignalPoint> points,
        double smoothingInverseOctaves)
    {
        // Coherence is a 0..1 confidence: decode psychoacoustic code to its plain width (cubic mean is meaningless here).
        double smoothingOctaves =
            SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves);
        if (smoothingOctaves <= 0 || points.Count < 3)
        {
            return points;
        }

        double halfWindowOctaves = 0.5 * smoothingOctaves;
        double lowerFactor = Math.Pow(2.0, -halfWindowOctaves);
        double upperFactor = Math.Pow(2.0, halfWindowOctaves);
        var smoothed = new List<SignalPoint>(points.Count);

        int start = 0;
        int end = 0;
        double sum = 0.0;

        for (int i = 0; i < points.Count; i++)
        {
            double lower = points[i].X * lowerFactor;
            double upper = points[i].X * upperFactor;

            while (end < points.Count && points[end].X <= upper)
            {
                sum += points[end].Y;
                end++;
            }

            while (start < end && points[start].X < lower)
            {
                sum -= points[start].Y;
                start++;
            }

            int count = end - start;
            smoothed.Add(new SignalPoint(
                points[i].X,
                count > 0 ? Math.Clamp(sum / count, 0.0, 1.0) : points[i].Y));
        }

        return smoothed;
    }

    private void AddMeasurementCoherenceIfAvailable(
        PlotModel model,
        FrequencyResponseOptions options,
        bool showCoherence)
    {
        if (!showCoherence ||
            measurementContext.Result is not { TransferCoherence: { Length: > 1 } coherence, SampleRate: > 0 } result)
        {
            return;
        }

        int fftLength = (coherence.Length - 1) * 2;
        AddCoherenceAxis(model);
        model.Series.Add(BuildCoherenceSeries(
            coherence,
            result.SampleRate,
            fftLength,
            options.SmoothingInverseOctaves));
    }

    /// <summary>Array spatial average, positions and spread. Steady-state, ungated by design, so they do not follow the time window.</summary>
    private void AddArrayMicrophones(PlotModel model, double? splOffsetDb)
    {
        CurveVisibilityOptions visibility = frequencyResponseVisibility;
        if (!visibility.ShowArrayAverage &&
            !visibility.ShowArrayMicrophones &&
            !visibility.ShowArraySpread)
        {
            return;
        }

        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            measurementContext.Result?.ArrayMicrophones ?? [],
            frequencyResponseOptions.UseCalibration,
            frequencyResponseOptions.SmoothingInverseOctaves);

        // Without an SPL offset the level curves are omitted, as the measured magnitude is.
        if (splOffsetDb is { } offset)
        {
            display = display with
            {
                Average = display.Average == null
                    ? null
                    : SplConversion.ToSoundPressureLevel([display.Average], offset)[0],
                Microphones = SplConversion.ToSoundPressureLevel(display.Microphones, offset)
            };
        }

        if (visibility.ShowArrayMicrophones)
        {
            foreach (AnalysisCurve microphone in display.Microphones)
            {
                LineSeries series = AddLineSeries(
                    model,
                    microphone,
                    ArrayTrackerFormat,
                    Mode.FrequencyResponse,
                    DecibelAxisKey);
                series.StrokeThickness = 1;
            }
        }

        if (visibility.ShowArrayAverage && display.Average is { } average)
        {
            AddLineSeries(
                model,
                average,
                ArrayTrackerFormat,
                Mode.FrequencyResponse,
                DecibelAxisKey).StrokeThickness = 2.5;
        }

        if (visibility.ShowArraySpread && display.Spread is { } spread)
        {
            AddArraySpreadAxis(model);
            AddLineSeries(
                model,
                spread,
                "{0}\n{2:0.# Hz}: {4:0.0} dB",
                Mode.FrequencyResponse,
                ArraySpreadAxisKey);
        }
    }

    // A range, not a level: on the magnitude axis it would invite reading as a response.
    private static void AddArraySpreadAxis(PlotModel model)
    {
        if (model.Axes.Any(axis => axis.Key == ArraySpreadAxisKey))
        {
            return;
        }

        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Key = ArraySpreadAxisKey,
            Position = AxisPosition.Right,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 60,
            Minimum = 0,
            Maximum = 30,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            Title = "Array spread (dB)",
            IsPanEnabled = false,
            IsZoomEnabled = false
        });
    }

    internal static void AddCoherenceAxis(PlotModel model)
    {
        if (model.Axes.Any(axis => axis.Key == CoherenceAxisKey))
        {
            return;
        }

        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Key = CoherenceAxisKey,
            Position = AxisPosition.Right,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 1,
            Minimum = 0,
            Maximum = 1,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            Title = "Coherence \u03B3\u00B2",
            IsPanEnabled = false,
            IsZoomEnabled = false
        });
    }

    private const string ArraySpreadAxisKey = "array-spread";

    private const string ArrayTrackerFormat = "{0}\n{2:0.# Hz}: {4:0.0} dB";

    private static LineSeries AddLineSeries(
        PlotModel model,
        AnalysisCurve curve,
        string trackerFormat,
        Mode mode,
        string yAxisKey,
        bool? phaseUnwrapped = null)
    {
        LineSeries series = OxyPlotAdapter.ToLineSeries(curve);
        series.YAxisKey = yAxisKey;
        series.TrackerFormatString = trackerFormat;
        series.Tag = new CurveTag(mode, curve.Kind, CurveSource.Main, phaseUnwrapped);
        model.Series.Add(series);
        return series;
    }

    private static LineSeries AddCompareLineSeries(
        PlotModel model,
        AnalysisCurve curve,
        string trackerFormat,
        string compareName,
        Mode mode,
        bool? phaseUnwrapped = null)
    {
        LineSeries series = OxyPlotAdapter.ToLineSeries(curve);
        series.TrackerFormatString = trackerFormat;
        series.LineStyle = LineStyle.Dash;
        series.StrokeThickness = 1.5;
        OxyColor color = series.Color;
        series.Color = OxyColor.FromArgb(150, color.R, color.G, color.B);
        series.Title = $"{curve.Name} · {compareName}";
        series.Tag = new CurveTag(mode, curve.Kind, CurveSource.Compare, phaseUnwrapped);
        model.Series.Add(series);
        return series;
    }

    private static void UpdateGroupDelayRange(
        AnalysisCurve curve,
        ref double minimum,
        ref double maximum,
        ref bool hasValidData)
    {
        for (int i = 0; i < curve.Points.Count; i++)
        {
            double y = curve.Points[i].Y;
            if (double.IsFinite(y))
            {
                minimum = Math.Min(minimum, y);
                maximum = Math.Max(maximum, y);
                hasValidData = true;
            }
        }
    }

    /// <summary>Whether Main and Compare share one clock (both loopback-synchronized). Imported recordings are referenced to their own arrival,
    /// so time-carrying curves (phase, GD, impulse, vector sum) would show an unmeasured delay.</summary>
    private bool CompareSharesATimeReference() =>
        (measurementContext.Result?.TimingReference ?? TimingReference.SynchronizedLoopback) ==
            TimingReference.SynchronizedLoopback &&
        getCompareSource?.Invoke() is { TimingReference: TimingReference.SynchronizedLoopback };

    // Compare view over its transfer IR; requires a matching sample rate so the ms gate and frequency axis align.
    private (IImpulseMeasurement Measurement,
        string DisplayName,
        double[]? Coherence,
        double? SplOffsetDb)?
        TryCreateCompareMeasurement()
    {
        if (getCompareSource?.Invoke() is not { } compare)
        {
            return null;
        }

        if (compare.TransferImpulseResponse is not { Length: > 0 } transferIr ||
            compare.SampleRate != AnalysisSampleRate)
        {
            return null;
        }

        int peakIndex = Math.Clamp(compare.TransferPeakIndex, 0, transferIr.Length - 1);
        return (
            new ImpulseMeasurementView(transferIr, peakIndex, compare.SampleRate)
            {
                LowestMeasuredFrequencyHz = compare.Band.LowEdgeHz,
                HighestMeasuredFrequencyHz = compare.Band.HighEdgeHz
            },
            compare.DisplayName,
            compare.TransferCoherence,
            compare.SplOffsetDb);
    }

    // FFT(h1 + h2): both IRs share the loopback reference, so the sample-wise sum is what the mic would capture together.
    // Delay and polarity mirror a DSP's Compare channel settings; options overrides the plot's (sum loss wants unsmoothed operands).
    internal AnalysisCurve? TryBuildComplexSumCurve(
        double compareDelayMs = 0,
        bool invertComparePolarity = false,
        FrequencyResponseOptions? options = null)
    {
        if (measurementContext.Result is not { Transfer.ImpulseResponse.Length: > 0 } main)
        {
            return null;
        }

        Complex[] mainIr = main.Transfer.ImpulseResponse;

        if (!CompareSharesATimeReference() ||
            getCompareSource?.Invoke() is not { } compare ||
            compare.TransferImpulseResponse is not { Length: > 0 } compareIr ||
            compare.SampleRate != main.SampleRate)
        {
            return null;
        }

        // Linear interpolation for fractional delays; slight HF droop near half-sample offsets is negligible at crossovers.
        double delaySamples =
            compareDelayMs / 1_000.0 * main.SampleRate;
        int wholeDelay = (int)Math.Floor(delaySamples);
        double fraction = delaySamples - wholeDelay;
        double sign = invertComparePolarity ? -1.0 : 1.0;

        int length = Math.Max(
            mainIr.Length,
            compareIr.Length + Math.Max(0, wholeDelay + 1));
        var sum = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            Complex value = i < mainIr.Length ? mainIr[i] : Complex.Zero;
            Complex shifted =
                SampleAt(compareIr, i - wholeDelay) * (1.0 - fraction) +
                SampleAt(compareIr, i - wholeDelay - 1) * fraction;
            sum[i] = value + sign * shifted;
        }

        // Window anchored explicitly at the earlier record's own start (as ProcessedChannels.SharedStartAnchorIndex): estimated on the sum,
        // the start could belong to a later, louder driver and cut the earlier one out. The sum's envelope peak is never used.
        int mainStart = Math.Clamp(
            TransferIrStartCache.ResolveStartIndex(
                mainIr,
                main.SampleRate,
                main.Transfer.PeakIndex),
            0,
            length - 1);
        int compareStart = Math.Clamp(
            TransferIrStartCache.ResolveStartIndex(
                compareIr,
                compare.SampleRate,
                compare.TransferPeakIndex) + (int)Math.Round(delaySamples),
            0,
            length - 1);
        int anchorIndex = Math.Min(mainStart, compareStart);

        // PeakIndex stays a real peak (the earlier record's), not the anchor in disguise.
        int peakIndex = Math.Min(
            Math.Clamp(main.Transfer.PeakIndex, 0, length - 1),
            Math.Clamp(
                compare.TransferPeakIndex + (int)Math.Round(delaySamples),
                0,
                length - 1));

        FrequencyResponseOptions curveOptions = options ?? frequencyResponseOptions;
        AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(sum, peakIndex, main.SampleRate),
            curveOptions,
            GetCalibration(curveOptions),
            anchorIndex);
        // The sum's view has no band, so break it where neither sweep measured; otherwise the window draws a fake rolloff.
        return curve with
        {
            Points = MeasuredBand.MaskUnmeasured(
                curve.Points,
                [main.MeasuredBand, compare.Band])
        };
    }

    private static Complex SampleAt(Complex[] source, int index) =>
        (uint)index < (uint)source.Length ? source[index] : Complex.Zero;

    // |H1+H2| relative to |H1|+|H2| in dB, always <= 0: summation loss from phase misalignment.
    // smoothingInverseOctaves defaults to the plot's; an overlay slot passes its own to avoid a second smoothing pass.
    internal AnalysisCurve? TryBuildComplexSumLossCurve(
        double compareDelayMs = 0,
        bool invertComparePolarity = false,
        double? smoothingInverseOctaves = null)
    {
        // Operands unsmoothed, gap smoothed: smoothing across a steep skirt fakes loss (see VirtualCrossoverAnalysis.SumLossCurve).
        FrequencyResponseOptions rawOptions = frequencyResponseOptions.WithSmoothing(0);
        if (TryBuildComplexSumCurve(compareDelayMs, invertComparePolarity, rawOptions)
            is not { } complexCurve)
        {
            return null;
        }

        if (measurementContext.Result is not { Transfer.ImpulseResponse.Length: > 0 } main ||
            getCompareSource?.Invoke() is not { } compare ||
            compare.TransferImpulseResponse is not { Length: > 0 } compareIr)
        {
            return null;
        }

        Complex[] mainIr = main.Transfer.ImpulseResponse;
        // Each windowed at its own start, resampled onto the sum's grid so all three align by index.
        AnalysisCurve mainMagnitude = DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(
                mainIr,
                Math.Clamp(main.Transfer.PeakIndex, 0, mainIr.Length - 1),
                main.SampleRate)
            {
                LowestMeasuredFrequencyHz = main.MeasuredBand.LowEdgeHz,
                HighestMeasuredFrequencyHz = main.MeasuredBand.HighEdgeHz
            },
            rawOptions,
            GetCalibration(rawOptions));
        AnalysisCurve compareMagnitude = DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(
                compareIr,
                Math.Clamp(compare.TransferPeakIndex, 0, compareIr.Length - 1),
                compare.SampleRate)
            {
                LowestMeasuredFrequencyHz = compare.Band.LowEdgeHz,
                HighestMeasuredFrequencyHz = compare.Band.HighEdgeHz
            },
            rawOptions,
            GetCalibration(rawOptions));

        int count = Math.Min(
            complexCurve.Points.Count,
            Math.Min(mainMagnitude.Points.Count, compareMagnitude.Points.Count));
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            // A NaN contributor is skipped, as in VirtualCrossoverAnalysis.SumLossCurve; where neither measured the point is a break.
            double magnitudeSum = 0.0;
            bool measured = false;
            foreach (SignalPoint operand in
                new[] { mainMagnitude.Points[i], compareMagnitude.Points[i] })
            {
                if (double.IsFinite(operand.Y))
                {
                    magnitudeSum += DataHelper.DecibelsToAmplitude(operand.Y);
                    measured = true;
                }
            }

            points.Add(new SignalPoint(
                complexCurve.Points[i].X,
                measured
                    ? complexCurve.Points[i].Y -
                        DataHelper.AmplitudeToDecibels(magnitudeSum)
                    : double.NaN));
        }

        double smoothing = smoothingInverseOctaves
            ?? frequencyResponseOptions.SmoothingInverseOctaves;
        return new AnalysisCurve(
            "Complex Sum Loss",
            smoothing != 0
                ? DataHelper.SmoothRatioLevels(
                    points,
                    SpectrumSmoothing.SmoothingOctaves(smoothing),
                    SpectrumSmoothing.IsPsychoacoustic(smoothing))
                : points);
    }

    // Names requested harmonics missing from the plot (overlap, below noise, no sweep). Below-noise is a clean capture: gray note, not amber.
    private void AddHiddenHarmonicAnnotation(
        PlotModel model, IReadOnlyList<AnalysisCurve> curves)
    {
        var present = new HashSet<AnalysisCurveKind>();
        foreach (AnalysisCurve curve in curves)
        {
            present.Add(curve.Kind);
        }

        var missing = new List<(int Order, string Label)>();
        void Check(bool requested, AnalysisCurveKind kind, int order, string label)
        {
            if (requested && !present.Contains(kind))
            {
                missing.Add((order, label));
            }
        }

        Check(frequencyResponseVisibility.ShowHd2, AnalysisCurveKind.SecondHarmonic, 2, "HD2");
        Check(frequencyResponseVisibility.ShowHd3, AnalysisCurveKind.ThirdHarmonic, 3, "HD3");
        Check(frequencyResponseVisibility.ShowHd4, AnalysisCurveKind.FourthHarmonic, 4, "HD4");
        if (missing.Count == 0)
        {
            return;
        }

        var belowNoiseOrders = new HashSet<int>(
            measurementContext.DistortionPacketValidity
                .Where(packet => packet.IsBelowNoiseFloor)
                .Select(packet => packet.Order));
        List<string> problem = missing
            .Where(m => !belowNoiseOrders.Contains(m.Order))
            .Select(m => m.Label)
            .ToList();
        List<string> belowNoise = missing
            .Where(m => belowNoiseOrders.Contains(m.Order))
            .Select(m => m.Label)
            .ToList();

        int nextLine = 0;
        if (problem.Count > 0)
        {
            IReadOnlyList<string> warnings = measurementContext.DistortionWarnings;
            string reason = warnings.Count > 0
                ? string.Join("\n", warnings.Select(w => w.Replace("; ", ";\n")))
                : "no sweep distortion data — record a sweep,\n"
                    + "or use a longer one so the harmonic packet clears its neighbour";
            string plural = problem.Count > 1 ? "curves" : "curve";
            string text = $"{string.Join(", ", problem)} {plural} not shown\n{reason}";
            model.Annotations.Add(new OverlayTextAnnotation
            {
                Text = text,
                TextPosition = new DataPoint(0.5, 0),
                TextFlowDirection = TextFlowDirection.TopDown,
                FontSize = 12,
                TextColor = UiPalette.Warning.ToOxy(),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
            });
            nextLine = text.Split('\n').Length;
        }

        if (belowNoise.Count > 0)
        {
            model.Annotations.Add(new OverlayTextAnnotation
            {
                Text = $"{string.Join(", ", belowNoise)} below the measurement noise floor\n"
                    + "distortion too low to resolve — a clean capture, not a fault",
                TextPosition = new DataPoint(0.5, nextLine),
                TextFlowDirection = TextFlowDirection.TopDown,
                FontSize = 12,
                TextColor = UiPalette.CurveMuted.ToOxy(),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
            });
        }
    }

    private static void AddRequiresTransferIrAnnotation(PlotModel model)
    {
        model.Annotations.Add(new OverlayTextAnnotation
        {
            Text = "Requires loopback transfer IR",
            TextPosition = new DataPoint(0.5, 3),
            TextFlowDirection = TextFlowDirection.TopDown,
            FontSize = 13,
            TextColor = UiPalette.CurveMuted.ToOxy(),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
        });
    }

    private static void AddSplViewOnlyAnnotation(PlotModel model)
    {
        model.Annotations.Add(CreateSplViewOnlyAnnotation(
            "No SPL calibration for this measurement — showing dB SPL overlays only"));
    }

    /// <summary>Shared with the live controller, which adds/removes its own instance across ticks.</summary>
    internal static OverlayTextAnnotation CreateSplViewOnlyAnnotation(string text) => new()
    {
        Text = text,
        TextPosition = new DataPoint(0.5, 3),
        TextFlowDirection = TextFlowDirection.TopDown,
        FontSize = 12,
        TextColor = UiPalette.Warning.ToOxy(),
        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
    };
}
