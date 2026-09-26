using System.Numerics;
using System.Runtime.CompilerServices;
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

    // The next run's configuration: the rate a plot takes when nothing is open.
    private readonly ExpSweepMeasurement expSweepMeasurement;
    private readonly Func<string?, CalibrationFile?> getCalibration;
    private readonly MeasurementPlotContext measurementContext;
    private readonly FrequencyResponseOptions frequencyResponseOptions;
    private readonly FrequencyResponseOptions phaseResponseOptions;
    private readonly FrequencyResponseOptions groupDelayOptions;
    private readonly CurveVisibilityOptions frequencyResponseVisibility;
    private readonly CurveVisibilityOptions phaseResponseVisibility;
    private readonly CurveVisibilityOptions groupDelayVisibility;
    private readonly ImpulseResponseOptions impulseResponseOptions;
    private readonly WaterfallGenerateOptions waterfallGenOptions;
    private readonly WaterfallGenerateOptions burstDecayGenOptions;
    private readonly ConditionalWeakTable<PlotModel, StrongBox<ImpulseOverlayFrame>> impulseFrames;
    private Func<CompareAnalysisSource?>? getCompareSource;

    public PlotModelFactory(
        AnalyzerDocument document,
        ExpSweepMeasurement expSweepMeasurement,
        Func<string?, CalibrationFile?> getCalibration,
        AnalyzerViewSettings view)
    {
        ArgumentNullException.ThrowIfNull(view);
        this.expSweepMeasurement = expSweepMeasurement;
        this.getCalibration = getCalibration;
        measurementContext = new MeasurementPlotContext(document);
        frequencyResponseOptions = view.FrequencyResponse;
        phaseResponseOptions = view.PhaseResponse;
        groupDelayOptions = view.GroupDelay;
        frequencyResponseVisibility = view.FrequencyResponseVisibility;
        phaseResponseVisibility = view.PhaseResponseVisibility;
        groupDelayVisibility = view.GroupDelayVisibility;
        impulseResponseOptions = view.ImpulseResponse;
        waterfallGenOptions = view.Waterfall;
        burstDecayGenOptions = view.BurstDecay;
        impulseFrames = new();
    }

    private PlotModelFactory(
        PlotModelFactory live,
        MeasurementPlotContext measurement,
        Func<string?, CalibrationFile?> calibrations,
        CompareAnalysisSource? compare)
    {
        expSweepMeasurement = live.expSweepMeasurement;
        getCalibration = calibrations;
        measurementContext = measurement;
        frequencyResponseOptions = live.frequencyResponseOptions;
        phaseResponseOptions = live.phaseResponseOptions;
        groupDelayOptions = live.groupDelayOptions;
        frequencyResponseVisibility = live.frequencyResponseVisibility;
        phaseResponseVisibility = live.phaseResponseVisibility;
        groupDelayVisibility = live.groupDelayVisibility;
        impulseResponseOptions = live.impulseResponseOptions;
        waterfallGenOptions = live.waterfallGenOptions;
        burstDecayGenOptions = live.burstDecayGenOptions;
        impulseFrames = live.impulseFrames;
        getCompareSource = () => compare;
    }

    /// <summary>A factory over this moment of the open measurement, the compare selection and the calibration, for a
    /// build off the UI thread; taken on the UI thread. It frames impulse overlays through this factory.</summary>
    public PlotModelFactory Freeze()
    {
        // The Own calibration is the open result's; resolved here, it cannot come from another result.
        string? calibrationId = frequencyResponseOptions.CalibrationId;
        CalibrationFile? calibration = getCalibration(calibrationId);
        return new PlotModelFactory(
            this,
            measurementContext.Freeze(),
            id => id == calibrationId ? calibration : getCalibration(id),
            getCompareSource?.Invoke());
    }

    public string? ImpulseResponseFileName => measurementContext.ImpulseResponseFileName;

    private int AnalysisSampleRate => measurementContext.Result?.SampleRate ?? expSweepMeasurement.SampleRate;

    // Compare overlay uses the SAME analysis settings as the main measurement.
    public void SetCompareSourceProvider(Func<CompareAnalysisSource?> provider) =>
        getCompareSource = provider;

    /// <summary>Without a valid SPL anchor the plot stays on a view-only dB SPL axis rather than falling back to dBr.</summary>
    public MagnitudeScale EffectiveFrequencyResponseScale =>
        frequencyResponseOptions.MagnitudeScale;

    private CalibrationFile? GetCalibration(FrequencyResponseOptions options) =>
        getCalibration(options.CalibrationId);

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

    // Drawn curve plus rate, baked smoothing and calibration, so a consumer can undo the additive correction.
    internal static RawCurveCapture DescribeWithoutRawForm(
        int smoothingCode,
        int sampleRate,
        CalibrationFile? calibration) =>
        new(Array.Empty<SignalPoint>(),
            Array.Empty<double>(),
            smoothingCode,
            sampleRate > 0 ? sampleRate : null,
            calibration);

    /// <summary>The main plot of a plot mode but Live Spectrum, which draws its own (<see cref="LiveSpectrumController"/>).</summary>
    /// <remarks>Safe off the UI thread, several at once: a build keeps what it computes to itself.</remarks>
    public PlotModel Create(Mode mode, bool includeCurves, CancellationToken cancellationToken = default) => mode switch
    {
        Mode.ImpulseResponse => CreateImpulseResponse(includeCurves, cancellationToken),
        Mode.FrequencyResponse => CreateFrequencyResponse(includeCurves, cancellationToken),
        Mode.PhaseResponse => CreatePhaseResponse(includeCurves, cancellationToken),
        Mode.GroupDelay => CreateGroupDelay(includeCurves, cancellationToken),
        Mode.CumulativeSpectrumDecay => CreateWaterfall(includeCurves, cancellationToken),
        Mode.BurstDecay => CreateBurstDecay(includeCurves, cancellationToken),
        Mode.Autocorrelation => CreateAutocorrelation(includeCurves, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a mode this factory draws.")
    };

    /// <summary>The title a build of <paramref name="mode"/> carries; a rename changes nothing else.</summary>
    public string Title(Mode mode) => measurementContext.CreateTitle(mode switch
    {
        // A band-limited view is not the record; the title says so.
        Mode.ImpulseResponse => "Impulse Response" + ImpulseBandLabel(impulseResponseOptions, AnalysisSampleRate),
        Mode.FrequencyResponse => "Frequency Response",
        Mode.PhaseResponse => "Phase Response",
        Mode.GroupDelay => "Group Delay",
        Mode.CumulativeSpectrumDecay => "Fourier Waterfall",
        Mode.BurstDecay => "Burst Decay",
        Mode.Autocorrelation => "Autocorrelation",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a mode this factory draws.")
    });

    public PlotModel CreateFrequencyResponse(bool includeCurves, CancellationToken cancellationToken = default)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(Title(Mode.FrequencyResponse));

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
            FrequencyResponseCurves? built = null;
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
                built = measurementContext.CreateFrequencyResponseCurves(
                    frequencyResponseOptions,
                    GetCalibration(frequencyResponseOptions),
                    anchorsHiddenPrimary ? requested | SpectrumCurves.Primary : requested,
                    cancellationToken);
                curves = built.Curves;
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

            cancellationToken.ThrowIfCancellationRequested();
            AddCompareFrequencyResponse(model, splRequested, splViewOnly);
            cancellationToken.ThrowIfCancellationRequested();

            if (!splViewOnly)
            {
                AddMeasurementCoherenceIfAvailable(
                    model,
                    frequencyResponseOptions,
                    frequencyResponseVisibility.ShowCoherence);

                AddArrayMicrophones(model, splOffset);

                AddHiddenHarmonicAnnotation(model, curves, built);
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

    public PlotModel CreatePhaseResponse(bool includeCurves, CancellationToken cancellationToken = default)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(Title(Mode.PhaseResponse));

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
                    phaseSettings,
                    cancellationToken);
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
                    measurementContext.Result?.TransferCoherence,
                    cancellationToken);

                // Tag representation so overlay math knows whether a difference must use the wrapped formula.
                AddLineSeries(
                    model,
                    curve,
                    phaseTrackerFormat,
                    Mode.PhaseResponse,
                    PhaseAxisKey,
                    phaseResponseOptions.Unwrap);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (phaseResponseVisibility.ShowMinimumPhase)
            {
                AnalysisCurve minimumPhaseCurve = DataHelper.GetMinimumPhase(
                    primaryMeasurement,
                    phaseSettings,
                    cancellationToken);

                AddLineSeries(
                    model,
                    minimumPhaseCurve,
                    phaseTrackerFormat,
                    Mode.PhaseResponse,
                    PhaseAxisKey,
                    phaseUnwrapped: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (phaseResponseVisibility.ShowExcessPhase)
            {
                AnalysisCurve excessPhaseCurve = DataHelper.GetExcessPhase(
                    primaryMeasurement,
                    phaseSettings,
                    measurementContext.Result?.TransferCoherence,
                    cancellationToken);

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
            cancellationToken.ThrowIfCancellationRequested();
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
                        compareData.Coherence,
                        cancellationToken);
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        phaseTrackerFormat,
                        compareData.DisplayName,
                        Mode.PhaseResponse,
                        phaseResponseOptions.Unwrap);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (phaseResponseVisibility.ShowMinimumPhase)
                {
                    AnalysisCurve compareCurve = DataHelper.GetMinimumPhase(
                        compareData.Measurement,
                        comparePhaseSettings,
                        cancellationToken);
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        phaseTrackerFormat,
                        compareData.DisplayName,
                        Mode.PhaseResponse,
                        phaseUnwrapped: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (phaseResponseVisibility.ShowExcessPhase && sharesTimeReference)
                {
                    AnalysisCurve compareCurve = DataHelper.GetExcessPhase(
                        compareData.Measurement,
                        comparePhaseSettings,
                        compareData.Coherence,
                        cancellationToken);
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

    public PlotModel CreateWaterfall(bool includeCurves, CancellationToken cancellationToken = default)
    {
        PlotModel model = PlotModelStyle.CreateWaterfallModel(
            Title(Mode.CumulativeSpectrumDecay),
            waterfallGenOptions);

        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            var waterfall = new WaterfallSeries()
            {
                BackgroundColor = UiPalette.WaterfallSurface.ToOxy(),
                GenerateOptions = waterfallGenOptions,
            };

            waterfall.FillFourierWaterfallData(measurementContext.CreatePrimaryMeasurement(), cancellationToken);
            model.Series.Add(waterfall);
            AddSliceVerdict(model, waterfall);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        return model;
    }

    public PlotModel CreateGroupDelay(bool includeCurves, CancellationToken cancellationToken = default)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(Title(Mode.GroupDelay));

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
                    includeMinimumPhase,
                    cancellationToken);
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

                cancellationToken.ThrowIfCancellationRequested();
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
                        includeMinimumPhase,
                        cancellationToken);
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

    public PlotModel CreateBurstDecay(bool includeCurves, CancellationToken cancellationToken = default)
    {
        PlotModel model = PlotModelStyle.CreateWaterfallModel(
            Title(Mode.BurstDecay),
            burstDecayGenOptions);

        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            var waterfall = new WaterfallSeries()
            {
                BackgroundColor = UiPalette.WaterfallSurface.ToOxy(),
                GenerateOptions = burstDecayGenOptions,
            };

            waterfall.FillFourierWaterfallData(measurementContext.CreatePrimaryMeasurement(), cancellationToken);
            model.Series.Add(waterfall);
            AddSliceVerdict(model, waterfall);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        return model;
    }

    /// <summary>The framing <paramref name="model"/> was built under, so stored overlays redraw under the framing of the
    /// model on screen; null for a model that is not an impulse plot of this factory.</summary>
    public ImpulseOverlayFrame? ImpulseFrameOf(PlotModel? model) =>
        model != null && impulseFrames.TryGetValue(model, out StrongBox<ImpulseOverlayFrame>? frame)
            ? frame.Value
            : null;

    public PlotModel CreateImpulseResponse(bool includeCurves, CancellationToken cancellationToken = default)
    {
        ImpulseResponseOptions opt = impulseResponseOptions;
        var frame = new ImpulseOverlayFrame(
            opt, 0.0, null, AnalysisSampleRate);
        PlotModel model = PlotModelStyle.CreateTitledModel(Title(Mode.ImpulseResponse));

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
            frame = new ImpulseOverlayFrame(
                opt, origin, mainSet.PeakReference, main.SampleRate);
            cancellationToken.ThrowIfCancellationRequested();

            if (anyTrace)
            {
                AddImpulseSeries(
                    model, mainSet, main.SampleRate, origin, null, drawn, stepCurves);

                cancellationToken.ThrowIfCancellationRequested();
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
        impulseFrames.AddOrUpdate(model, new StrongBox<ImpulseOverlayFrame>(frame));
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

    public PlotModel CreateAutocorrelation(bool includeCurves, CancellationToken cancellationToken = default)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(Title(Mode.Autocorrelation));

        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse &&
            impulseResponseOptions.ShowAutocorrelation)
        {
            AnalysisCurve curve =
                DataHelper.GetAutocorrelation(
                    measurementContext.CreatePrimaryMeasurement(),
                    impulseResponseOptions);
            cancellationToken.ThrowIfCancellationRequested();
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

    internal static void FillPoints(LineSeries series, List<SignalPoint> points)
    {
        series.Points.Clear();
        foreach (SignalPoint point in points)
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
        }
    }

    internal static LineSeries BuildCoherenceSeries(
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

    internal static List<SignalPoint> ResampleCoherence(
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
        PlotModel model, IReadOnlyList<AnalysisCurve> curves, FrequencyResponseCurves? built)
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
            (built?.DistortionPacketValidity ?? [])
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
            IReadOnlyList<string> warnings = built?.DistortionWarnings ?? [];
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

    private static void AddSliceVerdict(PlotModel model, WaterfallSeries waterfall)
    {
        if (WaterfallSliceVerdict.Explain(waterfall.GenerateOptions.WaterfallMode, waterfall.RawSlices.Count) is not { } text)
        {
            return;
        }

        model.Annotations.Add(new OverlayTextAnnotation
        {
            Text = text,
            TextPosition = new DataPoint(0.5, 3),
            TextFlowDirection = TextFlowDirection.TopDown,
            FontSize = 13,
            TextColor = UiPalette.CurveMuted.ToOxy(),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
        });
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
