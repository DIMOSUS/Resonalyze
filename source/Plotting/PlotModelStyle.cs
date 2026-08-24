using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

internal static class PlotModelStyle
{
    // OxyPlot's own defaults are a LIGHT theme: black tick lines, a black-alpha
    // grid, a black plot-area border, and axis text that follows PlotModel.TextColor
    // — itself black. Nothing in the app ever said otherwise, so every plot built
    // here drew its numbers in black on the dark plot surface, at 1.9:1 (#116): a
    // reader who does not already know what the axis says cannot read it.
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

    /// <summary>
    /// Gives a model the app's own axis furniture in place of OxyPlot's light-theme
    /// defaults, and the one place a second theme would swap them.
    /// </summary>
    /// <remarks>
    /// Only OxyPlot's DEFAULTS are replaced: an axis the caller coloured itself —
    /// the EQ wizard's gain axis, Virtual DSP's sum-loss axis, both of which say
    /// which curve they belong to by their colour — keeps what it was given, so
    /// this can be applied to any model without overriding a deliberate choice.
    /// LABEL colour needs no per-axis pass and no ordering care: an axis's text
    /// colour is Automatic, so it follows the model's however late the axis joins.
    /// Tick lines and gridlines are per-axis, and OxyPlot has deprecated both hooks
    /// that would let a model style axes as they arrive (the collection's change
    /// event and PlotModel.Updating), so an axis built by hand asks for
    /// <see cref="StyleAxis"/> itself. The axis helpers below already do.
    /// </remarks>
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

    /// <summary>
    /// Adds an axis to a model with the app's chrome on it. Use this rather than
    /// <c>model.Axes.Add</c> for any axis on a dark plot — an axis added raw keeps
    /// OxyPlot's black tick lines and black-alpha grid.
    /// </summary>
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

    /// <summary>
    /// Replaces OxyPlot's default tick and gridline colours on one axis. A colour
    /// the caller chose is left alone, so this is safe to call on any axis.
    /// </summary>
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

    // The audio band is the DEFAULT view and the hard fence for panning, but not a
    // fixed scale: zoom is what lets a 40 Hz mode or a crossover region be read at
    // the same resolution REW gives it. The absolute limits stay at the band the
    // curves are computed over, so a pan cannot wander off the data.
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

    // Default view and hard clamps for the loopback-referenced (dBr/dBc) axis.
    // Nothing pins this axis to 0: it is a RATIO to the reference, so the whole
    // curve rises by however much the loopback is attenuated relative to what
    // reaches the microphone — and attenuating the loopback is exactly what the
    // readme recommends when its input is being overdriven. A padded loopback
    // lifts a perfectly normal response to +10..+30 dBr, so the ceiling has to
    // clear realistic pads with margin rather than clip the curve out of the
    // view (the default VIEW still opens at -90..0;
    // FitDecibelViewToPrimaryCurves raises it when the data actually sits above).
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
