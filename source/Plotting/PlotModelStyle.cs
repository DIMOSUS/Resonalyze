using OxyPlot;
using OxyPlot.Axes;

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
    public const double RelativeDecibelMinimum = -90;
    public const double RelativeDecibelMaximum = 0;
    public const double RelativeDecibelAbsoluteMinimum = -120;
    public const double RelativeDecibelAbsoluteMaximum = 10;

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
