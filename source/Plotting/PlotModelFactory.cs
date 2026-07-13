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
    private const double GroupDelayMagnitudeGateDb = -40.0;

    public const string CoherenceAxisKey = "coherence";
    public const string DecibelAxisKey = "decibel";
    public const string FrequencyAxisKey = "frequency";
    public const string PhaseAxisKey = "phase";
    public const string GroupDelayAxisKey = "groupDelay";
    public const string ImpulseAxisKey = "impulse";
    public const string TimeAxisKey = "time";
    public const string AutocorrelationAxisKey = "autocorrelation";

    private readonly ExpSweepMeasurement expSweepMeasurement;
    private readonly NoiseMeasurement noiseMeasurement;
    private readonly Func<MicrophoneCalibrationMode, CalibrationFile?> getCalibration;
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
        ExpSweepMeasurement expSweepMeasurement,
        NoiseMeasurement noiseMeasurement,
        Func<MicrophoneCalibrationMode, CalibrationFile?> getCalibration,
        FrequencyResponseOptions frequencyResponseOptions,
        FrequencyResponseOptions phaseResponseOptions,
        FrequencyResponseOptions groupDelayOptions,
        CurveVisibilityOptions frequencyResponseVisibility,
        CurveVisibilityOptions phaseResponseVisibility,
        CurveVisibilityOptions groupDelayVisibility,
        ImpulseResponseOptions impulseResponseOptions,
        LiveSpectrumOptions liveSpectrumOptions,
        WaterfallGenerateOptions waterfallGenOptions,
        WaterfallGenerateOptions burstDecayGenOptions)
    {
        this.expSweepMeasurement = expSweepMeasurement;
        this.noiseMeasurement = noiseMeasurement;
        this.getCalibration = getCalibration;
        measurementContext = new MeasurementPlotContext(expSweepMeasurement);
        this.frequencyResponseOptions = frequencyResponseOptions;
        this.phaseResponseOptions = phaseResponseOptions;
        this.groupDelayOptions = groupDelayOptions;
        this.frequencyResponseVisibility = frequencyResponseVisibility;
        this.phaseResponseVisibility = phaseResponseVisibility;
        this.groupDelayVisibility = groupDelayVisibility;
        this.impulseResponseOptions = impulseResponseOptions;
        this.liveSpectrumOptions = liveSpectrumOptions;
        this.waterfallGenOptions = waterfallGenOptions;
        this.burstDecayGenOptions = burstDecayGenOptions;
    }

    public void SetImpulseResponseFileName(string? fileName)
    {
        measurementContext.SetImpulseResponseFileName(fileName);
    }

    public string? ImpulseResponseFileName => measurementContext.ImpulseResponseFileName;

    // The Compare measurement (from the Compare picker) whose Phase / Group Delay is
    // overlaid with the SAME analysis settings as the main measurement.
    public void SetCompareSourceProvider(Func<CompareAnalysisSource?> provider) =>
        getCompareSource = provider;

    private CalibrationFile? GetCalibration(FrequencyResponseOptions options) =>
        options.CalibrationMode == MicrophoneCalibrationMode.Off
            ? null
            : getCalibration(options.CalibrationMode);

    private CalibrationFile? GetCalibration(LiveSpectrumOptions options) =>
        options.CalibrationMode == MicrophoneCalibrationMode.Off
            ? null
            : getCalibration(options.CalibrationMode);

    public PlotModel CreateFrequencyResponse(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Frequency Response"));

        // Magnitude is derived from the loopback transfer IR, which is required.
        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            IReadOnlyList<AnalysisCurve> curves = measurementContext.CreateFrequencyResponseCurves(
                frequencyResponseOptions,
                GetCalibration(frequencyResponseOptions),
                frequencyResponseVisibility.ToSpectrumCurves());
            foreach (AnalysisCurve curve in curves)
            {
                AddLineSeries(
                    model,
                    curve,
                    DistortionTrackerFormat(curve.Kind),
                    Mode.FrequencyResponse,
                    DecibelAxisKey);
            }

            // Overlay the Compare magnitude (primary only; harmonics stay Main-only to
            // keep the plot readable), computed with the identical options/calibration.
            if (TryCreateCompareMeasurement() is { } compare)
            {
                IReadOnlyList<AnalysisCurve> compareCurves = DataHelper.GetSpectrum(
                    compare.Measurement,
                    frequencyResponseOptions,
                    GetCalibration(frequencyResponseOptions),
                    frequencyResponseVisibility.ToSpectrumCurves() & SpectrumCurves.Primary);
                foreach (AnalysisCurve curve in compareCurves)
                {
                    AddCompareLineSeries(
                        model,
                        curve,
                        DistortionTrackerFormat(curve.Kind),
                        compare.DisplayName,
                        Mode.FrequencyResponse);
                }
            }

            AddMeasurementCoherenceIfAvailable(
                model,
                frequencyResponseOptions,
                frequencyResponseVisibility.ShowCoherence);

            AddHiddenHarmonicAnnotation(model, curves);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        PlotModelStyle.AddFrequencyAxis(model);
        // The primary response is the loopback-referenced transfer magnitude (dBr,
        // relative to the reference); the harmonic / THD / noise curves are ratios to
        // the fundamental (dBc, relative to H1). The axis names both.
        PlotModelStyle.AddDecibelAxis(model, "dBr/dBc");
        return model;
    }

    // The tracker unit for a frequency-response curve: the primary is the transfer
    // magnitude relative to the loopback reference (dBr), every distortion curve
    // (HDn / THD / noise floor) is a ratio to the fundamental (dBc). The parenthetical
    // spells the reference out so the two are not read on the same footing.
    private static string DistortionTrackerFormat(AnalysisCurveKind kind) =>
        kind == AnalysisCurveKind.Primary
            ? "{0}\n{2:0.0} Hz\n{4:0.00} dBr (vs reference)"
            : "{0}\n{2:0.0} Hz\n{4:0.00} dBc (vs fundamental)";

    public PlotModel CreatePhaseResponse(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Phase Response"));

        // Phase analysis is only meaningful with a transfer IR (loopback timing).
        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            const string phaseTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.0}\u00B0";
            IImpulseMeasurement primaryMeasurement =
                measurementContext.CreatePrimaryMeasurement();
            var compare = TryCreateCompareMeasurement();
            PhaseAnalysisSettings phaseSettings =
                phaseResponseOptions.CreatePhaseAnalysisSettings();
            if (phaseSettings.DetrendMode == PhaseDetrendMode.Auto && compare != null)
            {
                // A comparison is meaningful only with one common time reference.
                // Resolve Auto from Main once, then reuse it as Manual for Main,
                // Compare and both excess curves so their relative delay survives.
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
                    expSweepMeasurement.TransferCoherence);

                // Measured phase can be either representation; tag it so overlay
                // math knows whether a difference must use the wrapped formula.
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

                // Minimum phase is continuous (unwrapped) by construction.
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
                    expSweepMeasurement.TransferCoherence);

                // Excess phase stays continuous (unwrapped) regardless of the detrend
                // choice; a residual slope is still an unwrapped representation.
                AddLineSeries(
                    model,
                    excessPhaseCurve,
                    phaseTrackerFormat,
                    Mode.PhaseResponse,
                    PhaseAxisKey,
                    phaseUnwrapped: true);
            }

            // Overlay the Compare measurement with the identical gate / detrend /
            // smoothing so the two responses can be read on the same terms.
            if (compare is { } compareData)
            {
                if (phaseResponseVisibility.ShowMeasuredPhase)
                {
                    AnalysisCurve compareCurve = DataHelper.GetPhase(
                        compareData.Measurement,
                        phaseSettings,
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
                        phaseSettings);
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        phaseTrackerFormat,
                        compareData.DisplayName,
                        Mode.PhaseResponse,
                        phaseUnwrapped: true);
                }

                if (phaseResponseVisibility.ShowExcessPhase)
                {
                    AnalysisCurve compareCurve = DataHelper.GetExcessPhase(
                        compareData.Measurement,
                        phaseSettings,
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
        model.Axes.Insert(0, new LinearAxis
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
                BackgroundColor = OxyColor.FromRgb(30, 0, 50),
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
        // Group delay is only meaningful with a transfer IR (loopback timing).
        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse &&
            (groupDelayVisibility.ShowGroupDelay || groupDelayVisibility.ShowCoherence))
        {
            const string groupDelayTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.000} ms";
            if (groupDelayVisibility.ShowGroupDelay)
            {
                // The gate is positioned by its Gate offset (left-shoulder-end) within the
                // transfer IR; the group delay reads absolute, referenced to the IR start.
                IImpulseMeasurement measurement = measurementContext.CreatePrimaryMeasurement();
                AnalysisCurve curve = DataHelper.GetGroupDelay(
                    measurement,
                    groupDelayOptions.GroupDelayGateOffsetMs,
                    groupDelayOptions.GroupDelayLeftMs,
                    groupDelayOptions.GroupDelayPlateauMs,
                    groupDelayOptions.GroupDelayRightMs,
                    groupDelayOptions.SmoothingInverseOctaves,
                    GroupDelayMagnitudeGateDb);
                AddLineSeries(model, curve, groupDelayTrackerFormat, Mode.GroupDelay, GroupDelayAxisKey);
                UpdateGroupDelayRange(curve, ref minimum, ref maximum, ref hasValidData);

                // Overlay the Compare measurement with the identical gate / smoothing.
                if (TryCreateCompareMeasurement() is { } compare)
                {
                    AnalysisCurve compareCurve = DataHelper.GetGroupDelay(
                        compare.Measurement,
                        groupDelayOptions.GroupDelayGateOffsetMs,
                        groupDelayOptions.GroupDelayLeftMs,
                        groupDelayOptions.GroupDelayPlateauMs,
                        groupDelayOptions.GroupDelayRightMs,
                        groupDelayOptions.SmoothingInverseOctaves,
                        GroupDelayMagnitudeGateDb);
                    // Draw the Compare curve as an overlay but keep the Y-axis auto-fit
                    // driven by the main measurement only. The Compare group delay is
                    // gated at the same offset, so as the gate moves its extremes swing
                    // widely; folding them into the range makes the scale jump on every
                    // edit. Off-scale Compare points are simply clipped, like any overlay.
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        groupDelayTrackerFormat,
                        compare.DisplayName,
                        Mode.GroupDelay);
                }
            }

            AddMeasurementCoherenceIfAvailable(
                model,
                groupDelayOptions,
                groupDelayVisibility.ShowCoherence);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse &&
                 groupDelayVisibility.ShowGroupDelay)
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
        model.Axes.Insert(0, msAxis);
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
                BackgroundColor = OxyColor.FromRgb(30, 0, 50),
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

    public PlotModel CreateImpulseResponse(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Impulse Response"));

        const string impulseTracker = "{0}\n{2:0} sample\n{4:0.00000000}";
        AnalysisCurve? curve = null;
        AnalysisCurve? compareCurve = null;
        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse &&
            impulseResponseOptions.ShowImpulse)
        {
            // The transfer IR is shown whole on an absolute timeline from sample 0 to the
            // peak plus the Length tail, so arrival times can be compared directly.
            IImpulseMeasurement main = measurementContext.CreatePrimaryMeasurement();
            curve = DataHelper.GetImpulseFromStart(main, impulseResponseOptions);
            AddLineSeries(model, curve, impulseTracker, Mode.ImpulseResponse, ImpulseAxisKey);

            if (TryCreateCompareMeasurement() is { } compare)
            {
                compareCurve = DataHelper.GetImpulseFromStart(
                    compare.Measurement,
                    impulseResponseOptions);
                AddCompareLineSeries(
                    model,
                    compareCurve,
                    impulseTracker,
                    compare.DisplayName,
                    Mode.ImpulseResponse);
            }
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse &&
                 impulseResponseOptions.ShowImpulse)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        var timeAxis = new LinearAxis
        {
            Key = TimeAxisKey,
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Solid,
        };
        var valueAxis = new LinearAxis
        {
            Key = ImpulseAxisKey,
            Position = AxisPosition.Left,
        };
        // Lock both axes to the drawn curves (which already reflect the logarithmic
        // toggle) so they are re-fit on every settings change and cannot be panned/zoomed
        // away from the data. The Compare curve is included so it stays on-screen.
        ApplyCurveRange(timeAxis, point => point.X, curve, compareCurve);
        ApplyCurveRange(valueAxis, point => point.Y, curve, compareCurve);
        model.Axes.Add(timeAxis);
        model.Axes.Add(valueAxis);
        return model;
    }

    // Fixes an axis to the curve's own min/max for the selected coordinate (both the visible
    // Minimum/Maximum and the AbsoluteMinimum/Maximum that bound zoom and pan). A flat range is
    // given a small margin so the axis stays valid.
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

        model.Axes.Add(new LinearAxis
        {
            Key = TimeAxisKey,
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Solid,
            Title = "ms"
        });
        model.Axes.Add(new LinearAxis
        {
            Key = AutocorrelationAxisKey,
            Position = AxisPosition.Left,
        });
        return model;
    }

    public PlotModel CreateLiveSpectrum()
    {
        PlotModel model = PlotModelStyle.CreateTitledModel("Live Transfer Function");

        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        if (liveSpectrumOptions.ShowCoherence)
        {
            AddCoherenceAxis(model);
        }

        return model;
    }

    public LineSeries BuildNoiseSeries(double[] accumulatedData)
    {
        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 0, 127),
            Title = "Live Transfer Function",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        UpdateNoiseSeries(series, accumulatedData);
        return series;
    }

    // The ~30 fps live redraw refills the existing series in place; reusing the
    // series object (and its point list's capacity) avoids re-allocating plot
    // objects on every tick.
    public void UpdateNoiseSeries(LineSeries series, double[] magnitude) =>
        FillPoints(series, ResampleLiveSpectrumMagnitude(magnitude));

    public LineSeries BuildInputMagnitudeSeries(double[] inputMagnitude)
    {
        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(80, 170, 255),
            Title = "Input Spectrum (RTA)",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        UpdateInputMagnitudeSeries(series, inputMagnitude);
        return series;
    }

    public void UpdateInputMagnitudeSeries(LineSeries series, double[] inputMagnitude) =>
        FillPoints(series, ResampleLiveSpectrumMagnitude(inputMagnitude));

    public LineSeries BuildPeakHoldSeries(double[] peakHoldData)
    {
        var series = new LineSeries
        {
            Color = OxyColor.FromAColor(170, OxyColor.FromRgb(255, 196, 0)),
            LineStyle = LineStyle.Solid,
            StrokeThickness = 1.0,
            Title = "Peak Hold",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        UpdatePeakHoldSeries(series, peakHoldData);
        return series;
    }

    public void UpdatePeakHoldSeries(LineSeries series, double[] peakHoldData) =>
        FillPoints(series, ResampleLiveSpectrumMagnitude(peakHoldData));

    private static void FillPoints(LineSeries series, List<SignalPoint> points)
    {
        series.Points.Clear();
        foreach (SignalPoint point in points)
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
        }
    }

    private List<SignalPoint> ResampleLiveSpectrumMagnitude(double[] magnitude)
    {
        int length = noiseMeasurement.SequenceLength;
        int binCount = Math.Min(length / 2, magnitude.Length);
        List<DataPoint> data = new(binCount);

        for (int i = 1; i < binCount; i++)
        {
            double frequency =
                i * ((double)noiseMeasurement.SampleRate / length);
            double decibels = DataHelper.AmplitudeToDecibels(magnitude[i]);
            data.Add(new DataPoint(frequency, decibels));
        }

        return DataHelper.LogarithmicResample(
            OxyPlotAdapter.ToSignalPoints(data),
            20,
            20000,
            1024,
            GetCalibration(liveSpectrumOptions),
            liveSpectrumOptions.SmoothingInverseOctaves > 0
                ? 1.0 / liveSpectrumOptions.SmoothingInverseOctaves
                : 0.0);
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
            Color = OxyColor.FromAColor(150, OxyColor.FromRgb(90, 200, 140)),
            Title = "Coherence",
            XAxisKey = FrequencyAxisKey,
            YAxisKey = CoherenceAxisKey,
            StrokeThickness = 1,
            LineStyle = LineStyle.Dash,
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} \u03B3\u00B2" // γ²
        };
        FillPoints(series, ResampleCoherence(
            coherence,
            sampleRate,
            fftLength,
            smoothingInverseOctaves));
        return series;
    }

    /// <summary>
    /// Builds the transfer-function curve split into a trusted segment (drawn
    /// normally) and a low-coherence segment (dimmed and dashed) so the user can
    /// see which frequencies are not reliable. Segments share their boundary
    /// points so the two lines join seamlessly.
    /// </summary>
    public (LineSeries Trusted, LineSeries Untrusted) BuildNoiseSeriesSegmented(
        double[] magnitude,
        double[] coherence,
        int thresholdPercent)
    {
        var trusted = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 0, 127),
            Title = "Live Transfer Function",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        var untrusted = new LineSeries
        {
            Color = OxyColor.FromAColor(140, OxyColor.FromRgb(170, 170, 170)),
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

        // The two resamplers use different grids (fixed 20-20 kHz vs bin-width
        // and Nyquist bounded, with empty bands skipped), so coherence must be
        // matched to each magnitude point by frequency, not by index. Missing
        // coverage counts as trusted, so a degenerate coherence set cannot
        // blank the whole live trace.
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

    // Nearest coherence sample for a frequency; both point lists are sorted by
    // X, so a forward-moving cursor keeps the whole pairing pass linear.
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
        if (smoothingInverseOctaves <= 0 || points.Count < 3)
        {
            return points;
        }

        double halfWindowOctaves = 0.5 / smoothingInverseOctaves;
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
            expSweepMeasurement.TransferCoherence is not { Length: > 1 } coherence ||
            expSweepMeasurement.SampleRate <= 0)
        {
            return;
        }

        int fftLength = (coherence.Length - 1) * 2;
        AddCoherenceAxis(model);
        model.Series.Add(BuildCoherenceSeries(
            coherence,
            expSweepMeasurement.SampleRate,
            fftLength,
            options.SmoothingInverseOctaves));
    }

    internal static void AddCoherenceAxis(PlotModel model)
    {
        if (model.Axes.Any(axis => axis.Key == CoherenceAxisKey))
        {
            return;
        }

        model.Axes.Add(new LinearAxis
        {
            Key = CoherenceAxisKey,
            Position = AxisPosition.Right,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 1,
            Minimum = 0,
            Maximum = 1,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            Title = "Coherence \u03B3\u00B2", // Y2
            IsPanEnabled = false,
            IsZoomEnabled = false
        });
    }

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

    // Same hue as its main counterpart, but dashed and dimmed so the Compare curve
    // reads as "the other measurement" without a second colour to decode.
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

    // Builds a view over the Compare transfer IR so the gated magnitude / phase /
    // group-delay math runs on it identically to the main curve (which is also
    // built from the transfer IR — loopback is mandatory for every measurement).
    // Requires a matching sample rate, otherwise the gate (in ms) and the
    // frequency axis would not align with the main measurement.
    private (IImpulseMeasurement Measurement, string DisplayName, double[]? Coherence)?
        TryCreateCompareMeasurement()
    {
        if (getCompareSource?.Invoke() is not { } compare)
        {
            return null;
        }

        if (compare.TransferImpulseResponse is not { Length: > 0 } transferIr ||
            compare.SampleRate != expSweepMeasurement.SampleRate)
        {
            return null;
        }

        int peakIndex = Math.Clamp(compare.TransferPeakIndex, 0, transferIr.Length - 1);
        return (
            new ImpulseMeasurementView(transferIr, peakIndex, compare.SampleRate),
            compare.DisplayName,
            compare.TransferCoherence);
    }

    // The complex (vector) sum of the Main and Compare transfer responses, i.e.
    // FFT(h1 + h2). Both transfer IRs share the loopback time reference (sample 0),
    // so a sample-wise sum of the impulse responses is exactly the response the
    // microphone would capture if both sources played together — including their
    // relative delay, polarity, and phase. This is what predicts the summed output
    // of two drivers through a crossover; adding the two dB magnitudes cannot.
    // Requires a transfer IR on both sides at the same sample rate; the curve runs
    // through the same FR pipeline (window, calibration, smoothing) as the plot.
    //
    // compareDelayMs and invertComparePolarity mirror the per-channel delay and
    // polarity switch a DSP would apply to the Compare source, so the predicted sum
    // can be tuned before touching the hardware.
    internal AnalysisCurve? TryBuildComplexSumCurve(
        double compareDelayMs = 0,
        bool invertComparePolarity = false)
    {
        if (expSweepMeasurement.TransferImpulseResponse is not { Length: > 0 } mainIr)
        {
            return null;
        }

        if (getCompareSource?.Invoke() is not { } compare ||
            compare.TransferImpulseResponse is not { Length: > 0 } compareIr ||
            compare.SampleRate != expSweepMeasurement.SampleRate)
        {
            return null;
        }

        // The shift is applied with linear interpolation, so fractional-sample
        // delays (a 0.01 ms step is ~0.44 samples at 44.1 kHz) move the result
        // smoothly; first-order interpolation costs a slight HF droop near
        // half-sample offsets, negligible in the crossover regions this predicts.
        double delaySamples =
            compareDelayMs / 1_000.0 * expSweepMeasurement.SampleRate;
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

        // Anchor the analysis window at the earlier of the two arrivals so the later
        // impulse still falls inside the window plateau; the summed envelope peak
        // could sit between them or vanish entirely under cancellation.
        int mainPeak = Math.Clamp(expSweepMeasurement.TransferPeakIndex, 0, length - 1);
        int comparePeak = Math.Clamp(
            compare.TransferPeakIndex + (int)Math.Round(delaySamples),
            0,
            length - 1);
        int peakIndex = Math.Min(mainPeak, comparePeak);

        return DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(sum, peakIndex, expSweepMeasurement.SampleRate),
            frequencyResponseOptions,
            GetCalibration(frequencyResponseOptions));
    }

    private static Complex SampleAt(Complex[] source, int index) =>
        (uint)index < (uint)source.Length ? source[index] : Complex.Zero;

    // The signed dB gap of the complex sum |H1 + H2| relative to the phase-blind
    // amplitude-magnitude sum (|H1| + |H2|). By the triangle inequality it is always <= 0:
    // it shows how many dB the real (phase-aware) sum falls short of the naive addition,
    // i.e. the summation loss caused by phase misalignment (0 only where the two sources
    // are perfectly in phase, dropping toward deep cancellation). The magnitude sum ignores
    // delay/polarity, so as those are tuned only the complex sum moves and the gap closes
    // toward 0 as the sources come into phase.
    internal AnalysisCurve? TryBuildComplexSumLossCurve(
        double compareDelayMs = 0,
        bool invertComparePolarity = false)
    {
        if (TryBuildComplexSumCurve(compareDelayMs, invertComparePolarity) is not { } complexCurve)
        {
            return null;
        }

        if (expSweepMeasurement.TransferImpulseResponse is not { Length: > 0 } mainIr ||
            getCompareSource?.Invoke() is not { } compare ||
            compare.TransferImpulseResponse is not { Length: > 0 } compareIr)
        {
            return null;
        }

        // Individual magnitudes of the two transfer responses, each windowed around its own
        // peak but log-resampled onto the same fixed frequency grid as the complex sum, so
        // all three curves align index-by-index.
        AnalysisCurve mainMagnitude = DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(
                mainIr,
                Math.Clamp(expSweepMeasurement.TransferPeakIndex, 0, mainIr.Length - 1),
                expSweepMeasurement.SampleRate),
            frequencyResponseOptions,
            GetCalibration(frequencyResponseOptions));
        AnalysisCurve compareMagnitude = DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(
                compareIr,
                Math.Clamp(compare.TransferPeakIndex, 0, compareIr.Length - 1),
                compare.SampleRate),
            frequencyResponseOptions,
            GetCalibration(frequencyResponseOptions));

        int count = Math.Min(
            complexCurve.Points.Count,
            Math.Min(mainMagnitude.Points.Count, compareMagnitude.Points.Count));
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            double magnitudeSumDb = DataHelper.AmplitudeToDecibels(
                DataHelper.DecibelsToAmplitude(mainMagnitude.Points[i].Y) +
                DataHelper.DecibelsToAmplitude(compareMagnitude.Points[i].Y));
            points.Add(new SignalPoint(
                complexCurve.Points[i].X,
                complexCurve.Points[i].Y - magnitudeSumDb));
        }

        return new AnalysisCurve("Complex Sum Loss", points);
    }

    // A harmonic order the user asked for can be missing from the plot: its packet
    // overlapped a neighbour and was dropped (so it is also left out of THD), or the
    // measurement carries no sweep to derive harmonics from. Silently dropping the
    // curve leaves the ticked checkbox unexplained, so a short note at the top names
    // the missing curves and — where the DSP said why — the reason.
    private void AddHiddenHarmonicAnnotation(
        PlotModel model, IReadOnlyList<AnalysisCurve> curves)
    {
        var present = new HashSet<AnalysisCurveKind>();
        foreach (AnalysisCurve curve in curves)
        {
            present.Add(curve.Kind);
        }

        var missing = new List<string>();
        void Check(bool requested, AnalysisCurveKind kind, string label)
        {
            if (requested && !present.Contains(kind))
            {
                missing.Add(label);
            }
        }

        Check(frequencyResponseVisibility.ShowHd2, AnalysisCurveKind.SecondHarmonic, "HD2");
        Check(frequencyResponseVisibility.ShowHd3, AnalysisCurveKind.ThirdHarmonic, "HD3");
        Check(frequencyResponseVisibility.ShowHd4, AnalysisCurveKind.FourthHarmonic, "HD4");
        if (missing.Count == 0)
        {
            return;
        }

        IReadOnlyList<string> warnings = measurementContext.DistortionWarnings;
        string reason = warnings.Count > 0
            ? string.Join("\n", warnings)
            : "no sweep distortion data — record a sweep, or use a longer one so the "
                + "harmonic packet clears its neighbour";
        string plural = missing.Count > 1 ? "curves" : "curve";
        model.Annotations.Add(new OverlayTextAnnotation
        {
            Text = $"{string.Join(", ", missing)} {plural} not shown\n{reason}",
            TextPosition = new DataPoint(0.5, 0),
            TextFlowDirection = TextFlowDirection.TopDown,
            FontSize = 12,
            TextColor = OxyColors.Goldenrod,
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
        });
    }

    // Phase and group delay need loopback timing; without a transfer IR the plot would
    // otherwise be silently empty, so explain why.
    private static void AddRequiresTransferIrAnnotation(PlotModel model)
    {
        model.Annotations.Add(new OverlayTextAnnotation
        {
            Text = "Requires loopback transfer IR",
            TextPosition = new DataPoint(0.5, 3),
            TextFlowDirection = TextFlowDirection.TopDown,
            FontSize = 13,
            TextColor = OxyColors.Gray,
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
        });
    }
}
