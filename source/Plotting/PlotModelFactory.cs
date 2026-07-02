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

    private readonly ExpSweepMeasurement expSweepMeasurement;
    private readonly NoiseMeasurement noiseMeasurement;
    private readonly CalibrationFile calibration;
    private readonly MeasurementPlotContext measurementContext;
    private readonly FrequencyResponseOptions frequencyResponseOptions;
    private readonly FrequencyResponseOptions phaseResponseOptions;
    private readonly FrequencyResponseOptions groupDelayOptions;
    private readonly ImpulseResponseOptions impulseResponseOptions;
    private readonly LiveSpectrumOptions liveSpectrumOptions;
    private readonly WaterfallGenerateOptions waterfallGenOptions;
    private readonly WaterfallGenerateOptions burstDecayGenOptions;
    private Func<CompareAnalysisSource?>? getCompareSource;

    public PlotModelFactory(
        ExpSweepMeasurement expSweepMeasurement,
        NoiseMeasurement noiseMeasurement,
        CalibrationFile calibration,
        FrequencyResponseOptions frequencyResponseOptions,
        FrequencyResponseOptions phaseResponseOptions,
        FrequencyResponseOptions groupDelayOptions,
        ImpulseResponseOptions impulseResponseOptions,
        LiveSpectrumOptions liveSpectrumOptions,
        WaterfallGenerateOptions waterfallGenOptions,
        WaterfallGenerateOptions burstDecayGenOptions)
    {
        this.expSweepMeasurement = expSweepMeasurement;
        this.noiseMeasurement = noiseMeasurement;
        this.calibration = calibration;
        measurementContext = new MeasurementPlotContext(expSweepMeasurement);
        this.frequencyResponseOptions = frequencyResponseOptions;
        this.phaseResponseOptions = phaseResponseOptions;
        this.groupDelayOptions = groupDelayOptions;
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
                calibration);
            foreach (AnalysisCurve curve in curves)
            {
                AddLineSeries(
                    model,
                    curve,
                    "{0}\n{2:0.0} Hz\n{4:0.00} dB",
                    Mode.FrequencyResponse);
            }

            // Overlay the Compare magnitude (primary only; harmonics stay Main-only to
            // keep the plot readable), computed with the identical options/calibration.
            if (TryCreateCompareSweepMeasurement() is { } compare)
            {
                IReadOnlyList<AnalysisCurve> compareCurves = DataHelper.GetSpectrum(
                    compare.Measurement,
                    frequencyResponseOptions,
                    calibration,
                    includePrimary: true,
                    includeHarmonics: false);
                foreach (AnalysisCurve curve in compareCurves)
                {
                    AddCompareLineSeries(
                        model,
                        curve,
                        "{0}\n{2:0.0} Hz\n{4:0.00} dB",
                        compare.DisplayName,
                        Mode.FrequencyResponse);
                }
            }
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        return model;
    }

    public PlotModel CreatePhaseResponse(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Phase Response"));

        // Phase analysis is only meaningful with a transfer IR (loopback timing).
        if (measurementContext.CanIncludeCurves(includeCurves) &&
            measurementContext.HasTransferImpulseResponse)
        {
            const string phaseTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.0}\u00B0";

            if (phaseResponseOptions.ShowMeasuredPhase)
            {
                AnalysisCurve curve = DataHelper.GetPhase(
                    measurementContext.CreatePrimaryMeasurement(),
                    phaseResponseOptions.PhaseGateOffsetMs,
                    phaseResponseOptions.PhaseLeftMs,
                    phaseResponseOptions.PhasePlateauMs,
                    phaseResponseOptions.PhaseRightMs,
                    phaseResponseOptions.PhaseDetrendMs,
                    phaseResponseOptions.SmoothingInverseOctaves,
                    phaseResponseOptions.Unwrap);

                // Measured phase can be either representation; tag it so overlay
                // math knows whether a difference must use the wrapped formula.
                AddLineSeries(
                    model,
                    curve,
                    phaseTrackerFormat,
                    Mode.PhaseResponse,
                    phaseResponseOptions.Unwrap);
            }

            if (phaseResponseOptions.ShowMinimumPhase)
            {
                AnalysisCurve minimumPhaseCurve = DataHelper.GetMinimumPhase(
                    measurementContext.CreatePrimaryMeasurement(),
                    phaseResponseOptions.PhaseGateOffsetMs,
                    phaseResponseOptions.PhaseLeftMs,
                    phaseResponseOptions.PhasePlateauMs,
                    phaseResponseOptions.PhaseRightMs,
                    phaseResponseOptions.SmoothingInverseOctaves);

                // Minimum phase is continuous (unwrapped) by construction.
                AddLineSeries(
                    model,
                    minimumPhaseCurve,
                    phaseTrackerFormat,
                    Mode.PhaseResponse,
                    phaseUnwrapped: true);
            }

            if (phaseResponseOptions.ShowExcessPhase)
            {
                AnalysisCurve excessPhaseCurve = DataHelper.GetExcessPhase(
                    measurementContext.CreatePrimaryMeasurement(),
                    phaseResponseOptions.PhaseGateOffsetMs,
                    phaseResponseOptions.PhaseLeftMs,
                    phaseResponseOptions.PhasePlateauMs,
                    phaseResponseOptions.PhaseRightMs,
                    phaseResponseOptions.PhaseDetrendMs,
                    phaseResponseOptions.SmoothingInverseOctaves);

                // Excess phase stays continuous (unwrapped) regardless of the detrend
                // choice; a residual slope is still an unwrapped representation.
                AddLineSeries(
                    model,
                    excessPhaseCurve,
                    phaseTrackerFormat,
                    Mode.PhaseResponse,
                    phaseUnwrapped: true);
            }

            // Overlay the Compare measurement with the identical gate / detrend /
            // smoothing so the two responses can be read on the same terms.
            if (TryCreateCompareMeasurement() is { } compare)
            {
                if (phaseResponseOptions.ShowMeasuredPhase)
                {
                    AnalysisCurve compareCurve = DataHelper.GetPhase(
                        compare.Measurement,
                        phaseResponseOptions.PhaseGateOffsetMs,
                        phaseResponseOptions.PhaseLeftMs,
                        phaseResponseOptions.PhasePlateauMs,
                        phaseResponseOptions.PhaseRightMs,
                        phaseResponseOptions.PhaseDetrendMs,
                        phaseResponseOptions.SmoothingInverseOctaves,
                        phaseResponseOptions.Unwrap);
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        phaseTrackerFormat,
                        compare.DisplayName,
                        Mode.PhaseResponse,
                        phaseResponseOptions.Unwrap);
                }

                if (phaseResponseOptions.ShowMinimumPhase)
                {
                    AnalysisCurve compareCurve = DataHelper.GetMinimumPhase(
                        compare.Measurement,
                        phaseResponseOptions.PhaseGateOffsetMs,
                        phaseResponseOptions.PhaseLeftMs,
                        phaseResponseOptions.PhasePlateauMs,
                        phaseResponseOptions.PhaseRightMs,
                        phaseResponseOptions.SmoothingInverseOctaves);
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        phaseTrackerFormat,
                        compare.DisplayName,
                        Mode.PhaseResponse,
                        phaseUnwrapped: true);
                }

                if (phaseResponseOptions.ShowExcessPhase)
                {
                    AnalysisCurve compareCurve = DataHelper.GetExcessPhase(
                        compare.Measurement,
                        phaseResponseOptions.PhaseGateOffsetMs,
                        phaseResponseOptions.PhaseLeftMs,
                        phaseResponseOptions.PhasePlateauMs,
                        phaseResponseOptions.PhaseRightMs,
                        phaseResponseOptions.PhaseDetrendMs,
                        phaseResponseOptions.SmoothingInverseOctaves);
                    AddCompareLineSeries(
                        model,
                        compareCurve,
                        phaseTrackerFormat,
                        compare.DisplayName,
                        Mode.PhaseResponse,
                        phaseUnwrapped: true);
                }
            }
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse &&
                 (phaseResponseOptions.ShowMeasuredPhase ||
                  phaseResponseOptions.ShowMinimumPhase ||
                  phaseResponseOptions.ShowExcessPhase))
        {
            AddRequiresTransferIrAnnotation(model);
        }

        PlotModelStyle.AddFrequencyAxis(model);
        model.Axes.Add(new LinearAxis
        {
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
            groupDelayOptions.ShowGroupDelay)
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
            const string groupDelayTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.000} ms";
            AddLineSeries(model, curve, groupDelayTrackerFormat, Mode.GroupDelay);
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
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse &&
                 groupDelayOptions.ShowGroupDelay)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        PlotModelStyle.AddFrequencyAxis(model);
        var msAxis = new LinearAxis
        {
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
        model.Axes.Add(msAxis);
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
            AddLineSeries(model, curve, impulseTracker, Mode.ImpulseResponse);

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
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Solid,
        };
        var valueAxis = new LinearAxis
        {
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
                Mode.Autocorrelation);
        }
        else if (measurementContext.CanIncludeCurves(includeCurves) &&
                 !measurementContext.HasTransferImpulseResponse &&
                 impulseResponseOptions.ShowAutocorrelation)
        {
            AddRequiresTransferIrAnnotation(model);
        }

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Solid,
            Title = "ms"
        });
        model.Axes.Add(new LinearAxis
        {
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
            model.Axes.Add(new LinearAxis
            {
                Key = CoherenceAxisKey,
                Position = AxisPosition.Right,
                AbsoluteMinimum = 0,
                AbsoluteMaximum = 1,
                Minimum = 0,
                Maximum = 1,
                MajorStep = 0.25,
                MinorStep = 0.125,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                Title = "Coherence \u03B3\u00B2", // Y2
            });
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
        series.Points.AddRange(
            OxyPlotAdapter.ToDataPoints(ResampleLiveSpectrumMagnitude(accumulatedData)));
        return series;
    }

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
        series.Points.AddRange(
            OxyPlotAdapter.ToDataPoints(ResampleLiveSpectrumMagnitude(peakHoldData)));
        return series;
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
            liveSpectrumOptions.UseCalibration ? calibration : null,
            liveSpectrumOptions.SmoothingInverseOctaves > 0
                ? 1.0 / liveSpectrumOptions.SmoothingInverseOctaves
                : 0.0);
    }

    public LineSeries BuildCoherenceSeries(double[] coherence)
    {
        var series = new LineSeries
        {
            Color = OxyColor.FromAColor(150, OxyColor.FromRgb(90, 200, 140)),
            Title = "Coherence",
            YAxisKey = CoherenceAxisKey,
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} \u03B3\u00B2" // Y2
        };
        foreach (SignalPoint point in ResampleCoherence(coherence))
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
        }

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
        List<SignalPoint> magnitudePoints = ResampleLiveSpectrumMagnitude(magnitude);
        List<SignalPoint> coherencePoints = ResampleCoherence(coherence);
        int count = Math.Min(magnitudePoints.Count, coherencePoints.Count);
        double threshold = thresholdPercent / 100.0;

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

        bool IsTrusted(int index) => coherencePoints[index].Y >= threshold;

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

        return (trusted, untrusted);
    }

    private List<SignalPoint> ResampleCoherence(double[] coherence)
    {
        int length = noiseMeasurement.SequenceLength;
        int binCount = Math.Min(length / 2, coherence.Length);
        List<DataPoint> data = new(binCount);

        for (int i = 1; i < binCount; i++)
        {
            double frequency =
                i * ((double)noiseMeasurement.SampleRate / length);
            data.Add(new DataPoint(frequency, coherence[i]));
        }

        List<SignalPoint> resampled = DataHelper.LogarithmicResample(
            OxyPlotAdapter.ToSignalPoints(data),
            20,
            20000,
            1024,
            calibration: null,
            liveSpectrumOptions.SmoothingInverseOctaves > 0
                ? 1.0 / liveSpectrumOptions.SmoothingInverseOctaves
                : 0.0,
            dBUnpack: false);

        // The Lanczos resampling kernel has negative lobes, so it can overshoot
        // past the physical [0, 1] range of coherence at sharp transitions.
        for (int i = 0; i < resampled.Count; i++)
        {
            resampled[i] = new SignalPoint(
                resampled[i].X,
                Math.Clamp(resampled[i].Y, 0.0, 1.0));
        }

        return resampled;
    }

    private static LineSeries AddLineSeries(
        PlotModel model,
        AnalysisCurve curve,
        string trackerFormat,
        Mode mode,
        bool? phaseUnwrapped = null)
    {
        LineSeries series = OxyPlotAdapter.ToLineSeries(curve);
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

    // Builds a view over the Compare transfer IR so the gated phase / group-delay
    // math runs on it identically. Requires a matching sample rate, otherwise the
    // gate (in ms) and the frequency axis would not align with the main measurement.
    private (IImpulseMeasurement Measurement, string DisplayName)? TryCreateCompareMeasurement()
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
            compare.DisplayName);
    }

    // The Compare sweep-deconvolution measurement, used for the Frequency Response
    // magnitude (which is built from the sweep IR, like the main curve). Harmonics are
    // not offered for Compare, so no harmonic-offset function is needed.
    private (IImpulseMeasurement Measurement, string DisplayName)? TryCreateCompareSweepMeasurement()
    {
        if (getCompareSource?.Invoke() is not { } compare)
        {
            return null;
        }

        if (compare.SweepDeconvolutionImpulseResponse is not { Length: > 0 } sweepIr ||
            compare.SampleRate != expSweepMeasurement.SampleRate)
        {
            return null;
        }

        int peakIndex = Math.Clamp(
            compare.SweepDeconvolutionPeakIndex,
            0,
            sweepIr.Length - 1);
        return (
            new ImpulseMeasurementView(sweepIr, peakIndex, compare.SampleRate),
            compare.DisplayName);
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
            calibration);
    }

    private static Complex SampleAt(Complex[] source, int index) =>
        (uint)index < (uint)source.Length ? source[index] : Complex.Zero;

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
