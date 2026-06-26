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

        if (measurementContext.CanIncludeCurves(includeCurves))
        {
            AnalysisCurve curve = DataHelper.GetPhase(
                measurementContext.CreatePrimaryMeasurement(),
                phaseResponseOptions.Window,
                phaseResponseOptions.LeftTukeyWindow,
                phaseResponseOptions.RightTukeyWindow,
                phaseResponseOptions.Offset,
                phaseResponseOptions.SmoothingInverseOctaves,
                phaseResponseOptions.Unwrap);

            AddLineSeries(model, curve, "{0}\n{2:0.0} Hz\n{4:0.0}\u00B0");
        }

        PlotModelStyle.AddFrequencyAxis(model);
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            AbsoluteMinimum = -720,
            AbsoluteMaximum = 720,
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
        if (measurementContext.CanIncludeCurves(includeCurves))
        {
            bool useTransfer = measurementContext.HasTransferImpulseResponse;
            IImpulseMeasurement measurement = useTransfer
                ? new ImpulseMeasurementView(
                    expSweepMeasurement.TransferImpulseResponse!,
                    0,
                    expSweepMeasurement.SampleRate)
                : measurementContext.CreateSweepDeconvolutionMeasurement();
            AnalysisCurve curve = DataHelper.GetGroupDelay(
                measurement,
                groupDelayOptions.Window,
                groupDelayOptions.LeftTukeyWindow,
                groupDelayOptions.RightTukeyWindow,
                useTransfer ? 0 : groupDelayOptions.Offset,
                groupDelayOptions.SmoothingInverseOctaves,
                GroupDelayMagnitudeGateDb,
                wrapWindow: useTransfer);
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

        if (measurementContext.CanIncludeCurves(includeCurves))
        {
            AnalysisCurve curve =
                DataHelper.GetImpulse(
                    measurementContext.CreatePrimaryMeasurement(),
                    impulseResponseOptions);
            AddLineSeries(model, curve, "{0}\n{2:0} sample\n{4:0.00000000}");
        }

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Solid,
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
        });
        return model;
    }

    public PlotModel CreateAutocorrelation(bool includeCurves)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(
            measurementContext.CreateTitle("Autocorrelation"));

        if (measurementContext.CanIncludeCurves(includeCurves))
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
                Title = "Coherence γ²",
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
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} γ²"
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

    private static void AddLineSeries(
        PlotModel model,
        AnalysisCurve curve,
        string trackerFormat)
    {
        LineSeries series = OxyPlotAdapter.ToLineSeries(curve);
        series.TrackerFormatString = trackerFormat;
        model.Series.Add(series);
    }
}
