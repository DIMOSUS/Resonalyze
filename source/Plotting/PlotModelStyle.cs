using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

internal static class PlotModelStyle
{
    public static PlotModel CreateTitledModel(string title) =>
        new()
        {
            Title = title,
            TitleFontSize = 14
        };

    public static void AddFrequencyAxis(PlotModel model)
    {
        model.Axes.Add(new LogarithmicAxis
        {
            Key = PlotModelFactory.FrequencyAxisKey,
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = 20,
            AbsoluteMaximum = 20000,
            Minimum = 20,
            Maximum = 20000,
            IsZoomEnabled = false,
            MajorGridlineStyle = LineStyle.Solid,
        });
    }

    // Default view and hard clamps for the loopback-referenced (dBr/dBc) axis.
    // Nothing pins this axis to 0: it is a RATIO to the reference, so the whole
    // curve rises by however much the loopback is attenuated relative to what
    // reaches the microphone — and attenuating the loopback is exactly what the
    // readme recommends when its input is being overdriven. A padded loopback
    // lifts a perfectly normal response to +10..+30 dBr, so the ceiling has to
    // clear realistic pads with margin rather than clip the curve out of the
    // view (the default VIEW still opens at -90..0; FitDecibelViewToSeries
    // raises it when the data actually sits above).
    public const double RelativeDecibelMinimum = -90;
    public const double RelativeDecibelMaximum = 0;
    public const double RelativeDecibelAbsoluteMinimum = -120;
    public const double RelativeDecibelAbsoluteMaximum = 60;

    // Default view and hard clamps for the absolute dB SPL axis. The window frames
    // a typical in-cabin response (noise floor to peaks); the clamp ceiling sits at
    // loud/painful levels (car audio can get there) but well short of anything
    // physically absurd — 120 dB is already the threshold of pain.
    public const double SplDecibelMinimum = 0;
    public const double SplDecibelMaximum = 120;
    public const double SplDecibelAbsoluteMinimum = -20;
    public const double SplDecibelAbsoluteMaximum = 150;

    public static void AddDecibelAxis(
        PlotModel model,
        string title = "dB",
        double minimum = RelativeDecibelMinimum,
        double maximum = RelativeDecibelMaximum,
        double absoluteMinimum = RelativeDecibelAbsoluteMinimum,
        double absoluteMaximum = RelativeDecibelAbsoluteMaximum)
    {
        model.Axes.Insert(0, new LinearAxis
        {
            Key = PlotModelFactory.DecibelAxisKey,
            Position = AxisPosition.Left,
            AbsoluteMinimum = absoluteMinimum,
            AbsoluteMaximum = absoluteMaximum,
            MajorStep = 10,
            Minimum = minimum,
            Maximum = maximum,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = title,
        });
    }

    // Least headroom left above the loudest sample when the default view is
    // raised; snapping to the 10 dB grid then puts the actual headroom in the
    // 5..15 dB band instead of doubling the step for a peak just past a line.
    private const double ViewFitMinimumHeadroomDb = 5;

    // Data must exceed the view's top by more than this before the view moves:
    // a unity response's fraction-of-a-dB window ripple over 0 dBr must not
    // rescale the familiar window.
    private const double ViewFitToleranceDb = 1.0;

    /// <summary>
    /// Raises the decibel axis's DEFAULT view ceiling just enough to show data
    /// that sits above it. A loopback-referenced curve rises by however much
    /// the reference is attenuated, and a padded measurement would otherwise
    /// open on an empty plot with its curve above the frame — pannable since
    /// the clamp was lifted, but invisible until the user goes looking.
    /// Expand-only (a normal curve keeps the familiar window), snapped to the
    /// 10 dB grid with headroom, clamped to the axis's hard ceiling, and a
    /// no-op on the SPL axis, whose default window already spans its data.
    /// </summary>
    public static void RaiseDecibelViewCeiling(PlotModel model, double dataMaxDb)
    {
        if (!double.IsFinite(dataMaxDb) ||
            model.Axes.FirstOrDefault(axis => axis.Key == PlotModelFactory.DecibelAxisKey)
                is not LinearAxis decibelAxis ||
            dataMaxDb <= decibelAxis.Maximum + ViewFitToleranceDb)
        {
            return;
        }

        double raised = Math.Ceiling(dataMaxDb / 10.0) * 10.0;
        if (raised - dataMaxDb < ViewFitMinimumHeadroomDb)
        {
            raised += 10.0;
        }
        raised = Math.Min(raised, decibelAxis.AbsoluteMaximum);
        if (raised > decibelAxis.Maximum)
        {
            decibelAxis.Maximum = raised;
        }
    }

    /// <summary>
    /// <see cref="RaiseDecibelViewCeiling"/> fitted to the model's PRIMARY
    /// magnitude curves (main and Compare). Only the primary: the harmonic,
    /// THD+N and noise traces are their own quantities, and the view exists to
    /// show the response the user measured.
    /// </summary>
    public static void FitDecibelViewToPrimaryCurves(PlotModel model)
    {
        double maxDb = double.NegativeInfinity;
        foreach (LineSeries series in model.Series.OfType<LineSeries>())
        {
            if (series.Tag is not CurveTag { Kind: AnalysisCurveKind.Primary })
            {
                continue;
            }
            foreach (DataPoint point in series.Points)
            {
                if (double.IsFinite(point.Y))
                {
                    maxDb = Math.Max(maxDb, point.Y);
                }
            }
        }
        RaiseDecibelViewCeiling(model, maxDb);
    }

    public static PlotModel CreateWaterfallModel(
        string title,
        WaterfallGenerateOptions options)
    {
        PlotModel model = CreateTitledModel(title);

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
}
