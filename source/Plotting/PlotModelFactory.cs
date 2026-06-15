using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed class PlotModelFactory
{
    private readonly ExpSweepMeasurement expSweepMeasurement;
    private readonly NoiseMeasurement noiseMeasurement;
    private readonly CalibrationFile calibration;
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
        this.frequencyResponseOptions = frequencyResponseOptions;
        this.phaseResponseOptions = phaseResponseOptions;
        this.groupDelayOptions = groupDelayOptions;
        this.impulseResponseOptions = impulseResponseOptions;
        this.waterfallGenOptions = waterfallGenOptions;
        this.burstDecayGenOptions = burstDecayGenOptions;
    }

    public PlotModel CreateFrequencyResponse(bool includeCurves)
    {
        var model = new PlotModel { Title = "Frequency Response" };

        if (CanIncludeImpulseCurves(includeCurves))
        {
            IReadOnlyList<AnalysisCurve> curves =
                DataHelper.GetSpectrum(
                    expSweepMeasurement,
                    frequencyResponseOptions,
                    calibration);
            foreach (AnalysisCurve curve in curves)
            {
                var series = OxyPlotAdapter.ToLineSeries(curve);
                series.TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB";
                model.Series.Add(series);
            }
        }

        AddFrequencyAxis(model);
        AddDecibelAxis(model);
        return model;
    }

    public PlotModel CreatePhaseResponse(bool includeCurves)
    {
        var model = new PlotModel { Title = "Phase Response" };

        if (CanIncludeImpulseCurves(includeCurves))
        {
            AnalysisCurve curve = DataHelper.GetPhase(
                expSweepMeasurement,
                phaseResponseOptions.Window,
                phaseResponseOptions.LeftTukeyWindow,
                phaseResponseOptions.RightTukeyWindow,
                phaseResponseOptions.Offset,
                phaseResponseOptions.SmoothingInverseOctaves,
                phaseResponseOptions.Unwrap);

            var series = OxyPlotAdapter.ToLineSeries(curve);
            series.TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.0}°";
            model.Series.Add(series);
        }

        AddFrequencyAxis(model);
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
        var model = CreateWaterfallBase("Fourier Waterfall", waterfallGenOptions);

        if (CanIncludeImpulseCurves(includeCurves))
        {
            var waterfall = new WaterfallSeries()
            {
                BackgroundColor = OxyColor.FromRgb(30, 0, 50),
                GenerateOptions = waterfallGenOptions,
            };

            waterfall.FillFourierWaterfallData(expSweepMeasurement);
            model.Series.Add(waterfall);
        }

        return model;
    }

    public PlotModel CreateGroupDelay(bool includeCurves)
    {
        var model = new PlotModel { Title = "Group Delay" };

        if (CanIncludeImpulseCurves(includeCurves))
        {
            AnalysisCurve curve = DataHelper.GetGroupDelay(
                expSweepMeasurement,
                groupDelayOptions.Window,
                groupDelayOptions.LeftTukeyWindow,
                groupDelayOptions.RightTukeyWindow,
                groupDelayOptions.Offset,
                groupDelayOptions.SmoothingInverseOctaves);
            var series = OxyPlotAdapter.ToLineSeries(curve);
            series.TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.000} ms";
            model.Series.Add(series);
        }

        AddFrequencyAxis(model);
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            AbsoluteMinimum = -20,
            AbsoluteMaximum = 20,
            Minimum = -10,
            Maximum = 10,
            MajorStep = 1,
            MajorGridlineStyle = LineStyle.Solid,
            Title = "ms"
        });
        return model;
    }

    public PlotModel CreateBurstDecay(bool includeCurves)
    {
        var model = CreateWaterfallBase("Burst Decay", burstDecayGenOptions);

        if (CanIncludeImpulseCurves(includeCurves))
        {
            var waterfall = new WaterfallSeries()
            {
                BackgroundColor = OxyColor.FromRgb(30, 0, 50),
                GenerateOptions = burstDecayGenOptions,
            };

            waterfall.FillFourierWaterfallData(expSweepMeasurement);
            model.Series.Add(waterfall);
        }

        return model;
    }

    public PlotModel CreateImpulseResponse(bool includeCurves)
    {
        var model = new PlotModel { Title = "Impulse Response" };

        if (CanIncludeImpulseCurves(includeCurves))
        {
            AnalysisCurve curve =
                DataHelper.GetImpulse(expSweepMeasurement, impulseResponseOptions);
            var series = OxyPlotAdapter.ToLineSeries(curve);
            series.TrackerFormatString = "{0}\n{2:0} sample\n{4:0.00000000}";
            model.Series.Add(series);
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
        var model = new PlotModel { Title = "Autocorrelation" };

        if (CanIncludeImpulseCurves(includeCurves))
        {
            AnalysisCurve curve =
                DataHelper.GetAutocorrelation(
                    expSweepMeasurement,
                    impulseResponseOptions);
            var series = OxyPlotAdapter.ToLineSeries(curve);
            series.TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}";
            model.Series.Add(series);
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
        var model = new PlotModel { Title = "Live Spectrum" };

        AddFrequencyAxis(model);
        AddDecibelAxis(model);
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

    private PlotModel CreateWaterfallBase(
        string title,
        WaterfallGenerateOptions options)
    {
        var model = new PlotModel { Title = title };

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = -1.0,
            Maximum = 1.0,
            IsAxisVisible = false,
            IsPanEnabled = false,
            IsZoomEnabled = false,
        });
        model.Axes.Add(new LogarithmicClipAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 20,
            Maximum = 60000,
            ClipValue = 20000,
            IsPanEnabled = false,
            IsZoomEnabled = false,
        });

        model.Axes.Add(new LinearColorAxis
        {
            Position = AxisPosition.Left,
            Minimum = options.DbRange,
            Maximum = -options.DbRange,
            Palette = OxyPalette.Interpolate(
                512,
                OxyColors.DarkBlue,
                OxyColors.Cyan,
                OxyColors.Yellow,
                OxyColors.Orange,
                OxyColors.DarkRed,
                OxyColors.White,
                OxyColors.White,
                OxyColors.White,
                OxyColors.White),
            HighColor = OxyColors.Black
        });

        return model;
    }

    private bool CanIncludeImpulseCurves(bool includeCurves) =>
        includeCurves &&
        expSweepMeasurement.ImpulseResponse != null &&
        !expSweepMeasurement.InProgress;

    private static void AddFrequencyAxis(PlotModel model)
    {
        model.Axes.Add(new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = 20,
            AbsoluteMaximum = 20000,
            Minimum = 20,
            Maximum = 20000,
            IsZoomEnabled = false,
            MajorGridlineStyle = LineStyle.Solid,
        });
    }

    private static void AddDecibelAxis(PlotModel model)
    {
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            AbsoluteMinimum = -120,
            AbsoluteMaximum = 10,
            MajorStep = 10,
            Minimum = -90,
            Maximum = 0,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "dB",
        });
    }
}
