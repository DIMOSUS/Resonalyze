using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

internal static class PlotModelStyle
{
    // OxyPlot's defaults are a light theme (black ticks, grid, border, text), unreadable on the dark surface.
    private static readonly OxyColor DefaultTicklineColor = OxyColors.Black;
    private static readonly OxyColor DefaultMajorGridlineColor = OxyColor.FromArgb(0x40, 0, 0, 0);
    private static readonly OxyColor DefaultMinorGridlineColor = OxyColor.FromArgb(0x20, 0, 0, 0);

    public static PlotModel CreateTitledModel(string title)
    {
        var model = new PlotModel
        {
            Title = title,
            TitleFontSize = 14
        };
        ApplyChrome(model);
        return model;
    }

    /// <summary>Replaces OxyPlot's light-theme defaults; colours a caller set explicitly are kept.</summary>
    /// <remarks>Label colour follows the model automatically; ticks/grid are per-axis and OxyPlot deprecated the add-axis hooks,
    /// so hand-built axes call <see cref="StyleAxis"/> (the helpers below do).</remarks>
    public static void ApplyChrome(PlotModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        model.TextColor = ToOxyColor(Ui.UiPalette.GraphAxisText);
        model.PlotAreaBorderColor = ToOxyColor(Ui.UiPalette.GraphAreaBorder);

        foreach (Axis axis in model.Axes)
        {
            StyleAxis(axis);
        }
    }

    /// <summary>Use instead of <c>model.Axes.Add</c>: a raw axis keeps OxyPlot's black ticks and grid.</summary>
    public static void AddAxis(PlotModel model, Axis axis)
    {
        ArgumentNullException.ThrowIfNull(model);

        StyleAxis(axis);
        model.Axes.Add(axis);
    }

    /// <inheritdoc cref="AddAxis"/>
    public static void InsertAxis(PlotModel model, int index, Axis axis)
    {
        ArgumentNullException.ThrowIfNull(model);

        StyleAxis(axis);
        model.Axes.Insert(index, axis);
    }

    public static void StyleAxis(Axis axis)
    {
        ArgumentNullException.ThrowIfNull(axis);

        if (axis.TicklineColor == DefaultTicklineColor)
        {
            axis.TicklineColor = ToOxyColor(Ui.UiPalette.GraphTickline);
        }

        if (axis.AxislineColor == DefaultTicklineColor)
        {
            axis.AxislineColor = ToOxyColor(Ui.UiPalette.GraphTickline);
        }

        if (axis.MajorGridlineColor == DefaultMajorGridlineColor)
        {
            axis.MajorGridlineColor = ToOxyColor(Ui.UiPalette.GraphGridlineMajor);
        }

        if (axis.MinorGridlineColor == DefaultMinorGridlineColor)
        {
            axis.MinorGridlineColor = ToOxyColor(Ui.UiPalette.GraphGridlineMinor);
        }
    }

    private static OxyColor ToOxyColor(Color color) =>
        OxyColor.FromArgb(color.A, color.R, color.G, color.B);

    // Audio band is the default view and pan fence, not a fixed scale.
    public static void AddFrequencyAxis(PlotModel model)
    {
        AddAxis(model, new LogarithmicAxis
        {
            Key = PlotModelFactory.FrequencyAxisKey,
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = 20,
            AbsoluteMaximum = 20000,
            Minimum = 20,
            Maximum = 20000,
            MajorGridlineStyle = LineStyle.Solid,
        });
    }

    // dBr is a ratio to the loopback: a padded loopback lifts a normal response to +10..+30 dBr, so the ceiling clears that.
    // The default view still opens at -90..0 (FitDecibelViewToPrimaryCurves raises it).
    public const double RelativeDecibelMinimum = -90;
    public const double RelativeDecibelMaximum = 0;
    public const double RelativeDecibelAbsoluteMinimum = -120;
    public const double RelativeDecibelAbsoluteMaximum = 60;

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
        InsertAxis(model, 0, new LinearAxis
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

    // Snapping to the 10 dB grid then leaves 5..15 dB headroom.
    private const double ViewFitMinimumHeadroomDb = 5;

    // Window ripple over 0 dBr must not rescale the familiar window.
    private const double ViewFitToleranceDb = 1.0;

    /// <summary>Expand-only raise of the default dB view ceiling for data above it (padded loopback), snapped with headroom; no-op on SPL.</summary>
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

    /// <summary>Primary curves only (main and Compare); harmonic/noise traces are other quantities.</summary>
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

        AddAxis(model, new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = -1.0,
            Maximum = 1.0,
            IsAxisVisible = false,
            IsPanEnabled = false,
            IsZoomEnabled = false,
        });
        AddAxis(model, new LogarithmicClipAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 20,
            Maximum = 60000,
            ClipValue = 20000,
            IsPanEnabled = false,
            IsZoomEnabled = false,
        });

        AddAxis(model, new LinearColorAxis
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
