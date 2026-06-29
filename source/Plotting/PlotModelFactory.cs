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

    public PlotModel CreateFrequencyResponse(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Frequency Response"));

        if (measurementContext.CanIncludeCurves(includeCurves))
        {
            IReadOnlyList<AnalysisCurve> curves = measurementContext.CreateFrequencyResponseCurves(
                frequencyResponseOptions,
                calibration);
            foreach (AnalysisCurve curve in curves)
            {
                AddLineSeries(model, curve, "{0}\n{2:0.0} Hz\n{4:0.00} dB");
            }
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
                AddLineSeries(model, curve, phaseTrackerFormat).Tag =
                    new PhaseCurveTag(phaseResponseOptions.Unwrap);
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
                AddLineSeries(model, minimumPhaseCurve, phaseTrackerFormat).Tag =
                    new PhaseCurveTag(Unwrapped: true);
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
                AddLineSeries(model, excessPhaseCurve, phaseTrackerFormat).Tag =
                    new PhaseCurveTag(Unwrapped: true);
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

        if (measurementContext.CanIncludeCurves(includeCurves))
        {
            var waterfall = new WaterfallSeries()
            {
                BackgroundColor = OxyColor.FromRgb(30, 0, 50),
                GenerateOptions = waterfallGenOptions,
            };

            waterfall.FillFourierWaterfallData(measurementContext.CreatePrimaryMeasurement());
            model.Series.Add(waterfall);
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
            AddLineSeries(model, curve, "{0}\n{2:0.0} Hz\n{4:0.000} ms");

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

        if (measurementContext.CanIncludeCurves(includeCurves))
        {
            var waterfall = new WaterfallSeries()
            {
                BackgroundColor = OxyColor.FromRgb(30, 0, 50),
                GenerateOptions = burstDecayGenOptions,
            };

            waterfall.FillFourierWaterfallData(measurementContext.CreatePrimaryMeasurement());
            model.Series.Add(waterfall);
        }

        return model;
    }

    public PlotModel CreateImpulseResponse(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Impulse Response"));

        AnalysisCurve? curve = null;
        if (measurementContext.CanIncludeCurves(includeCurves) &&
            impulseResponseOptions.ShowImpulse)
        {
            curve = DataHelper.GetImpulse(
                measurementContext.CreatePrimaryMeasurement(),
                impulseResponseOptions);
            AddLineSeries(model, curve, "{0}\n{2:0} sample\n{4:0.00000000}");
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
        // Lock both axes to the drawn curve (which already reflects the logarithmic toggle) so
        // they are re-fit on every settings change and cannot be panned/zoomed away from the
        // data.
        ApplyCurveRange(timeAxis, curve, point => point.X);
        ApplyCurveRange(valueAxis, curve, point => point.Y);
        model.Axes.Add(timeAxis);
        model.Axes.Add(valueAxis);
        return model;
    }

    // Fixes an axis to the curve's own min/max for the selected coordinate (both the visible
    // Minimum/Maximum and the AbsoluteMinimum/Maximum that bound zoom and pan). A flat range is
    // given a small margin so the axis stays valid.
    private static void ApplyCurveRange(
        LinearAxis axis,
        AnalysisCurve? curve,
        Func<SignalPoint, double> selector)
    {
        if (curve == null)
        {
            return;
        }

        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
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
            impulseResponseOptions.ShowAutocorrelation)
        {
            AnalysisCurve curve =
                DataHelper.GetAutocorrelation(
                    measurementContext.CreatePrimaryMeasurement(),
                    impulseResponseOptions);
            AddLineSeries(model, curve, "{0}\n{2:0.000} ms\n{4:0.000}");
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
        PlotModel model = PlotModelStyle.CreateTitledModel(
            liveSpectrumOptions.Mode == LiveSpectrumMode.TransferFunction
                ? "Live Transfer Function"
                : "Live Spectrum");

        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        if (liveSpectrumOptions.Mode == LiveSpectrumMode.TransferFunction &&
            liveSpectrumOptions.ShowCoherence)
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
            Title = liveSpectrumOptions.Mode == LiveSpectrumMode.TransferFunction
                ? "Live Transfer Function"
                : "Live Spectrum",
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
            if (liveSpectrumOptions.Mode == LiveSpectrumMode.InputSpectrum)
            {
                decibels -= 27.0;
            }
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
        string trackerFormat)
    {
        LineSeries series = OxyPlotAdapter.ToLineSeries(curve);
        series.TrackerFormatString = trackerFormat;
        model.Series.Add(series);
        return series;
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

/// <summary>
/// Carried on a phase <c>LineSeries.Tag</c> so a captured overlay records whether the
/// curve is an unwrapped (continuous) or wrapped (-180..180) phase representation. The
/// overlay difference operations need this to decide between a raw subtraction (keeps the
/// slope/delay of unwrapped curves) and the wrapped formula (shortest angular distance).
/// </summary>
internal sealed record PhaseCurveTag(bool Unwrapped);
