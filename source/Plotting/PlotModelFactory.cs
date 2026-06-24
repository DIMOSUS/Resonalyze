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

    private readonly ExpSweepMeasurement expSweepMeasurement;
    private readonly NoiseMeasurement noiseMeasurement;
    private readonly CalibrationFile calibration;
    private readonly MeasurementPlotContext measurementContext;
    private readonly FrequencyResponseOptions frequencyResponseOptions;
    private readonly FrequencyResponseOptions phaseResponseOptions;
    private readonly FrequencyResponseOptions groupDelayOptions;
    private readonly ImpulseResponseOptions impulseResponseOptions;
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
        PlotModel model = PlotModelStyle.CreateTitledModel("Live Spectrum");

        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        return model;
    }

    public LineSeries BuildNoiseSeries(double[] accumulatedData)
    {
        int length = noiseMeasurement.SequenceLength;
        int binCount = Math.Min(length / 2, accumulatedData.Length);
        List<DataPoint> data = new(binCount);

        for (int i = 1; i < binCount; i++)
        {
            double frequency =
                i * ((double)noiseMeasurement.SampleRate / length);
            double decibels =
                DataHelper.AmplitudeToDecibels(accumulatedData[i]) - 21.0;
            data.Add(new DataPoint(frequency, decibels));
        }

        List<SignalPoint> resampled = DataHelper.LogarithmicResample(
            OxyPlotAdapter.ToSignalPoints(data),
            20,
            20000,
            1024,
            calibration,
            1.0 / 6.0);
        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 0, 127),
            Title = "Live Spectrum"
        };
        series.Points.AddRange(OxyPlotAdapter.ToDataPoints(resampled));
        series.TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB";
        return series;
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
