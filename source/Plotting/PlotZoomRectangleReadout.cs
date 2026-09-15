using System.Globalization;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;

namespace Resonalyze;

/// <summary>Zoom box area in axis values, so it survives pans/redraws before the zooming click; frames locked axes too (it measures).
/// See docs/tech/plot-interaction.md#zoom-box.</summary>
internal readonly record struct PlotZoomBox(
    Axis? HorizontalAxis,
    double HorizontalFrom,
    double HorizontalTo,
    Axis? VerticalAxis,
    double VerticalFrom,
    double VerticalTo)
{
    public bool IsEmpty => HorizontalAxis == null && VerticalAxis == null;

    public static PlotZoomBox Frame(
        Axis? xAxis,
        Axis? yAxis,
        ScreenPoint start,
        ScreenPoint current)
    {
        Axis? x = Measurable(xAxis);
        Axis? y = Measurable(yAxis);
        (double left, double right) = Values(x, start.X, current.X);
        (double low, double high) = Values(y, start.Y, current.Y);
        return new PlotZoomBox(x, left, right, y, low, high);
    }

    public OxyRect Screen(OxyRect plotArea)
    {
        (double left, double right) =
            Pixels(HorizontalAxis, HorizontalFrom, HorizontalTo, plotArea.Left, plotArea.Right);
        (double top, double bottom) =
            Pixels(VerticalAxis, VerticalFrom, VerticalTo, plotArea.Top, plotArea.Bottom);
        return new OxyRect(left, top, right - left, bottom - top);
    }

    public bool Contains(OxyRect plotArea, ScreenPoint point) =>
        !IsEmpty && Screen(plotArea).Contains(point.X, point.Y);

    /// <summary>A box over locked scales still measures but offers no zoom.</summary>
    public bool CanZoom =>
        HorizontalAxis is { IsZoomEnabled: true } || VerticalAxis is { IsZoomEnabled: true };

    /// <summary>Zooms only the axes that allow it.</summary>
    public void Zoom()
    {
        if (HorizontalAxis is { IsZoomEnabled: true } horizontal)
        {
            horizontal.Zoom(HorizontalFrom, HorizontalTo);
        }

        if (VerticalAxis is { IsZoomEnabled: true } vertical)
        {
            vertical.Zoom(VerticalFrom, VerticalTo);
        }
    }

    /// <summary>"1.53 kHz × 12.4 dB"; states every measurable direction, zoomable or not.</summary>
    public string Describe(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string? width = HorizontalAxis == null
            ? null
            : PlotZoomRectangleReadout.FormatSpan(
                HorizontalAxis,
                HorizontalTo - HorizontalFrom,
                culture);
        string? height = VerticalAxis == null
            ? null
            : PlotZoomRectangleReadout.FormatSpan(
                VerticalAxis,
                VerticalTo - VerticalFrom,
                culture);

        if (width != null && height != null)
        {
            return $"{width} \u00D7 {height}";
        }

        return width ?? height ?? string.Empty;
    }

    /// <summary>Visible, non-colour axes only (the waterfall's hidden ±1 placeholder is not readable).</summary>
    private static Axis? Measurable(Axis? axis) =>
        axis is { IsAxisVisible: true } and not IColorAxis ? axis : null;

    private static (double From, double To) Values(Axis? axis, double first, double second)
    {
        if (axis == null)
        {
            return (double.NaN, double.NaN);
        }

        double a = axis.InverseTransform(first);
        double b = axis.InverseTransform(second);
        return (Math.Min(a, b), Math.Max(a, b));
    }

    private static (double Low, double High) Pixels(
        Axis? axis,
        double from,
        double to,
        double areaLow,
        double areaHigh)
    {
        if (axis == null)
        {
            return (areaLow, areaHigh);
        }

        double a = axis.Transform(from);
        double b = axis.Transform(to);
        return (Math.Min(a, b), Math.Max(a, b));
    }
}

/// <summary>REW-style zoom box readout arithmetic, kept view-free for tests. See docs/tech/plot-interaction.md#zoom-box.</summary>
internal static class PlotZoomRectangleReadout
{
    private const double KilohertzThreshold = 1000;

    /// <summary>An explicit list: short titles like "step" or "r" are quantity names, not units.</summary>
    private static readonly string[] KnownUnits =
        ["dB", "ms", "s", "Hz", "kHz", "deg", "\u00B0", "%", "samples"];

    private const string Hertz = "Hz";
    private const string Kilohertz = "kHz";
    private const string Degrees = "\u00B0";

    private const double Gap = 10;

    public const double PaddingX = 6;
    public const double PaddingY = 3;

    /// <summary>Below this the box is a slip; the refusal names the too-small side, as REW does.</summary>
    private const double MinimumZoomSize = 10;

    /// <summary>Below this it was a click, not a box: silently ignored.</summary>
    private const double MinimumDragSize = 3;

    public static bool WasDrawn(ScreenPoint start, ScreenPoint current) =>
        Math.Abs(current.X - start.X) >= MinimumDragSize ||
        Math.Abs(current.Y - start.Y) >= MinimumDragSize;

    /// <summary>Hint line only while pending and zoomable; none over locked scales, where the box is just a ruler.</summary>
    public static string HintFor(PlotZoomBox box, bool pending) =>
        pending && box.CanZoom && !box.IsEmpty ? "click inside to zoom" : string.Empty;

    /// <summary>Null when zoomable; judged at click time on the box's current screen rectangle, along zoomable sides.</summary>
    public static string? RefusalFor(PlotZoomBox box, OxyRect screen)
    {
        bool tooNarrow = box.HorizontalAxis is { IsZoomEnabled: true } &&
                         screen.Width < MinimumZoomSize;
        bool tooShort = box.VerticalAxis is { IsZoomEnabled: true } &&
                        screen.Height < MinimumZoomSize;

        if (tooNarrow && tooShort)
        {
            return "That box is too small to zoom in to";
        }

        if (tooNarrow)
        {
            return "That box is too narrow to zoom in to";
        }

        return tooShort ? "That box is too short to zoom in to" : null;
    }

    public static string FormatSpan(Axis axis, double span, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentNullException.ThrowIfNull(culture);

        string unit = UnitOf(axis);
        if (unit == Hertz && Math.Abs(span) >= KilohertzThreshold)
        {
            span /= KilohertzThreshold;
            unit = Kilohertz;
        }

        string number = FormatNumber(span, culture);
        if (unit.Length == 0)
        {
            return number;
        }

        return unit == Degrees ? number + unit : $"{number} {unit}";
    }

    private static string FormatNumber(double value, CultureInfo culture) =>
        value.ToString(
            Math.Abs(value) switch
            {
                >= 100 => "0",
                >= 10 => "0.0",
                >= 1 => "0.00",
                _ => "0.000",
            },
            culture);

    /// <summary>Unit from the title (whole, first word, or trailing parentheses); keyless frequency/phase axes by key.</summary>
    private static string UnitOf(Axis axis)
    {
        string? title = axis.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            return axis.Key switch
            {
                PlotModelFactory.FrequencyAxisKey => Hertz,
                PlotModelFactory.PhaseAxisKey => Degrees,
                // Keyless bottom log axes (Time Alignment previews) are frequency.
                _ => axis is LogarithmicAxis && axis.IsHorizontal() ? Hertz : string.Empty,
            };
        }

        title = title.Trim();
        int opening = title.LastIndexOf('(');
        if (opening >= 0 && title.EndsWith(')'))
        {
            return title[(opening + 1)..^1].Trim();
        }

        string candidate = title.Split(' ', '\t')[0];
        return KnownUnits.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            ? candidate
            : string.Empty;
    }

    /// <summary>Outside the box at the drag-end corner, clamped into the plot area.</summary>
    public static OxyRect PlaceLabel(
        OxyRect plotArea,
        OxyRect rectangle,
        ScreenPoint corner,
        OxySize text)
    {
        double width = text.Width + (2 * PaddingX);
        double height = text.Height + (2 * PaddingY);
        double left = corner.X >= rectangle.Left + (rectangle.Width / 2)
            ? corner.X + Gap
            : corner.X - Gap - width;
        double top = corner.Y >= rectangle.Top + (rectangle.Height / 2)
            ? corner.Y + Gap
            : corner.Y - Gap - height;

        return new OxyRect(
            Math.Clamp(left, plotArea.Left, Math.Max(plotArea.Left, plotArea.Right - width)),
            Math.Clamp(top, plotArea.Top, Math.Max(plotArea.Top, plotArea.Bottom - height)),
            width,
            height);
    }
}

/// <summary>Zoom box and readout. Owned by the model, so a rebuild drops it; hides itself when the axes' identity changes
/// (a VDSP view switch re-arms axes without a mouse event).</summary>
internal sealed class PlotZoomRectangleAnnotation : Annotation
{
    private static readonly OxyColor Fill = OxyColor.FromAColor(60, OxyColors.Gold);
    private static readonly OxyColor Stroke = OxyColor.FromAColor(220, OxyColors.Gold);
    private static readonly OxyColor LabelFill = OxyColor.FromAColor(220, OxyColors.Black);
    private static readonly OxyColor LabelStroke = OxyColor.FromAColor(140, OxyColors.White);

    private const double LineGap = 2;

    private const double HintFontStep = 1;

    private static readonly byte HintOpacity = 170;

    public PlotZoomRectangleAnnotation()
    {
        Layer = AnnotationLayer.AboveSeries;
    }

    public PlotZoomBox? Box { get; set; }

    public IReadOnlyList<PlotAxisIdentity> Axes { get; set; } = Array.Empty<PlotAxisIdentity>();

    public string Text { get; set; } = string.Empty;

    public string Hint { get; set; } = string.Empty;

    /// <summary>Drag-end corner, so the label keeps its side after a pan.</summary>
    public bool AnchorRight { get; set; }

    public bool AnchorBottom { get; set; }

    public override void Render(IRenderContext rc)
    {
        if (Box is not PlotZoomBox box || box.IsEmpty || !StillDescribesItsPlot())
        {
            return;
        }

        OxyRect plotArea = PlotModel.PlotArea;
        OxyRect rectangle = box.Screen(plotArea);
        if (!TryIntersect(rectangle, plotArea, out OxyRect drawn))
        {
            // Off the graph; still remembered and returns with the data.
            return;
        }

        rc.DrawRectangle(drawn, Fill, OxyColors.Undefined, 0, EdgeRenderingMode.PreferSpeed);
        rc.DrawLine(
            [
                new ScreenPoint(drawn.Left, drawn.Top),
                new ScreenPoint(drawn.Right, drawn.Top),
                new ScreenPoint(drawn.Right, drawn.Bottom),
                new ScreenPoint(drawn.Left, drawn.Bottom),
                new ScreenPoint(drawn.Left, drawn.Top)
            ],
            Stroke,
            1,
            EdgeRenderingMode.PreferGeometricAccuracy,
            LineStyle.Dash.GetDashArray(),
            LineJoin.Miter);

        if (Text.Length == 0)
        {
            return;
        }

        double hintFontSize = ActualFontSize - HintFontStep;
        OxySize size = rc.MeasureText(Text, ActualFont, ActualFontSize, ActualFontWeight);
        OxySize hint = Hint.Length == 0
            ? new OxySize(0, 0)
            : rc.MeasureText(Hint, ActualFont, hintFontSize, ActualFontWeight);
        var block = new OxySize(
            Math.Max(size.Width, hint.Width),
            size.Height + (Hint.Length == 0 ? 0 : hint.Height + LineGap));

        var corner = new ScreenPoint(
            AnchorRight ? rectangle.Right : rectangle.Left,
            AnchorBottom ? rectangle.Bottom : rectangle.Top);
        OxyRect label = PlotZoomRectangleReadout.PlaceLabel(plotArea, rectangle, corner, block);
        rc.DrawRectangle(label, LabelFill, LabelStroke, 1, EdgeRenderingMode.PreferGeometricAccuracy);

        double left = label.Left + PlotZoomRectangleReadout.PaddingX;
        rc.DrawText(
            new ScreenPoint(left, label.Top + PlotZoomRectangleReadout.PaddingY),
            Text,
            ActualTextColor,
            ActualFont,
            ActualFontSize,
            ActualFontWeight);
        if (Hint.Length == 0)
        {
            return;
        }

        rc.DrawText(
            new ScreenPoint(
                left,
                label.Top + PlotZoomRectangleReadout.PaddingY + size.Height + LineGap),
            Hint,
            OxyColor.FromAColor(HintOpacity, ActualTextColor),
            ActualFont,
            hintFontSize,
            ActualFontWeight);
    }

    /// <summary>Checked every paint: a re-arm repaints and the pointer may never move again.</summary>
    internal bool StillDescribesItsPlot() =>
        PlotModel != null && PlotAxisIdentities.Describe(PlotModel).SequenceEqual(Axes);

    private static bool TryIntersect(OxyRect rectangle, OxyRect area, out OxyRect intersection)
    {
        double left = Math.Max(rectangle.Left, area.Left);
        double top = Math.Max(rectangle.Top, area.Top);
        double width = Math.Min(rectangle.Right, area.Right) - left;
        double height = Math.Min(rectangle.Bottom, area.Bottom) - top;
        if (width <= 0 || height <= 0)
        {
            intersection = default;
            return false;
        }

        intersection = new OxyRect(left, top, width, height);
        return true;
    }
}
