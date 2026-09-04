using System.Globalization;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;

namespace Resonalyze;

/// <summary>
/// The area a zoom box frames, held in AXIS VALUES rather than in pixels.
///
/// REW's box outlives the drag that drew it: the graph is zoomed by a click INSIDE
/// the box, not by the button coming up, so between the two the plot can be panned,
/// wheeled or redrawn. A box remembered in pixels would then frame a different piece
/// of the curve than the one somebody drew around; values do not move.
///
/// The box frames BOTH directions whatever the axes allow, because measuring is what
/// it is mostly for and an axis that cannot be zoomed can still be read: the Virtual
/// DSP phase view has its height locked at ±180°, and a phase difference across a
/// crossover is exactly the kind of thing worth measuring there. Zooming is the
/// second act, and it moves only the axes that allow it. A direction with no axis to
/// read against is left null and drawn across the whole plot area.
/// </summary>
internal readonly record struct PlotZoomBox(
    Axis? HorizontalAxis,
    double HorizontalFrom,
    double HorizontalTo,
    Axis? VerticalAxis,
    double VerticalFrom,
    double VerticalTo)
{
    /// <summary>Nothing framed at all: neither axis can be zoomed.</summary>
    public bool IsEmpty => HorizontalAxis == null && VerticalAxis == null;

    /// <summary>
    /// The box a drag from <paramref name="start"/> to <paramref name="current"/>
    /// frames, read through the axes it was drawn over.
    /// </summary>
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

    /// <summary>Where the box sits on screen NOW, whatever the axes have done since.</summary>
    public OxyRect Screen(OxyRect plotArea)
    {
        (double left, double right) =
            Pixels(HorizontalAxis, HorizontalFrom, HorizontalTo, plotArea.Left, plotArea.Right);
        (double top, double bottom) =
            Pixels(VerticalAxis, VerticalFrom, VerticalTo, plotArea.Top, plotArea.Bottom);
        return new OxyRect(left, top, right - left, bottom - top);
    }

    /// <summary>True when the point is inside the box — the click REW zooms on.</summary>
    public bool Contains(OxyRect plotArea, ScreenPoint point) =>
        !IsEmpty && Screen(plotArea).Contains(point.X, point.Y);

    /// <summary>
    /// True when clicking the box would move anything. A box over a locked scale is
    /// still worth drawing — it measures — but it is not worth offering a zoom for.
    /// </summary>
    public bool CanZoom =>
        HorizontalAxis is { IsZoomEnabled: true } || VerticalAxis is { IsZoomEnabled: true };

    /// <summary>
    /// Zooms to what the box frames — but only along the axes that allow it. A box
    /// drawn over the Virtual DSP phase view moves the frequency axis and leaves the
    /// locked ±180° height where it is, having measured it all the same.
    /// </summary>
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

    /// <summary>
    /// The size of the framed area as text — "1.53 kHz × 12.4 dB". Both directions
    /// are stated whenever there is an axis to state them in, zoomable or not: the
    /// number is the point of the box, and a locked scale is no less measurable for
    /// being locked.
    /// </summary>
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

    /// <summary>
    /// An axis a span can be READ against: one the plot actually shows. The
    /// waterfall's hidden ±1 placeholder and the colour scales are not scales anybody
    /// reads a difference off, so a box over them measures the other direction alone.
    /// </summary>
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

/// <summary>
/// What REW writes beside its zoom box: how wide and how tall the framed area is IN
/// THE UNITS OF THE AXES it is drawn over — "1.53 kHz × 12.4 dB". Together with the
/// box outliving the drag, that is what makes the gesture a measuring tape as well
/// as a selection: the width of a suckout or the depth of a dip gets read without
/// counting gridlines, and without the scale moving unless the click that moves it
/// is given.
///
/// The arithmetic lives here rather than in the manipulator so it can be tested
/// without a view: everything below takes axes, values and screen coordinates and
/// returns numbers, text and rectangles.
/// </summary>
internal static class PlotZoomRectangleReadout
{
    /// <summary>Frequencies pass to kHz at and above this span, as REW writes them.</summary>
    private const double KilohertzThreshold = 1000;

    /// <summary>
    /// The units these plots are actually measured in. A list rather than a rule
    /// about short words, because a short word is just as often the NAME of a
    /// dimensionless quantity — the impulse view's "step" axis, the correlation
    /// views' "r" — and "0.420 step" reads as a unit that does not exist. A title
    /// that is not one of these says nothing after the number.
    /// </summary>
    private static readonly string[] KnownUnits =
        ["dB", "ms", "s", "Hz", "kHz", "deg", "\u00B0", "%", "samples"];

    private const string Hertz = "Hz";
    private const string Kilohertz = "kHz";
    private const string Degrees = "\u00B0";

    /// <summary>Gap between the box's corner and the label, in pixels.</summary>
    private const double Gap = 10;

    /// <summary>Padding between the label's text and its backdrop, in pixels.</summary>
    public const double PaddingX = 6;
    public const double PaddingY = 3;

    /// <summary>
    /// Shortest side, in pixels, that can be zoomed to. Below it the box is a slip of
    /// the hand rather than a selection, and REW answers such a box by saying which
    /// side is too small instead of zooming to it.
    /// </summary>
    private const double MinimumZoomSize = 10;

    /// <summary>
    /// Pointer travel below which nothing was drawn at all — a Ctrl + right CLICK
    /// rather than a box. It is not refused and not reported: there is nothing to
    /// refuse, and a message at every stray click would be worse than silence.
    /// </summary>
    private const double MinimumDragSize = 3;

    /// <summary>True once the pointer has moved far enough to have drawn a box.</summary>
    public static bool WasDrawn(ScreenPoint start, ScreenPoint current) =>
        Math.Abs(current.X - start.X) >= MinimumDragSize ||
        Math.Abs(current.Y - start.Y) >= MinimumDragSize;

    /// <summary>
    /// The second line of the label, under the size: what the waiting box is waiting
    /// for. It is a line of its own rather than a tail on the number, which keeps the
    /// label narrow enough to sit beside the box instead of stretching across the
    /// graph. There is none during the drag, none when there is nothing to measure,
    /// and none over a locked scale — where the box is a ruler and nothing else, and
    /// inviting a click that would do nothing is worse than saying nothing.
    /// </summary>
    public static string HintFor(PlotZoomBox box, bool pending) =>
        pending && box.CanZoom && !box.IsEmpty ? "click inside to zoom" : string.Empty;

    /// <summary>
    /// Why the box cannot be zoomed to, or null when it can. Judged at the click, and
    /// against the box as it stands on screen by then — it may have been panned or
    /// wheeled since it was drawn — along the sides a zoom would actually move. REW
    /// names the side that is too small rather than silently doing nothing, which is
    /// the difference between "the program ignored me" and "that box is too thin".
    /// </summary>
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

    /// <summary>
    /// A span in one axis's units, with the unit the axis says it is in. Frequencies
    /// pass to kHz where REW passes them, so a two-octave box over the midrange reads
    /// "1.53 kHz" rather than a four-digit number of hertz.
    /// </summary>
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

        // A degree sign is set against its number; a word unit is set apart from it.
        return unit == Degrees ? number + unit : $"{number} {unit}";
    }

    /// <summary>
    /// Three significant figures, which is as much as any of these axes is read to:
    /// a box is framed by eye, and a fourth digit only makes the label longer.
    /// </summary>
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

    /// <summary>
    /// What the axis says it is measured in. Most axes are titled with the unit
    /// itself ("dB", "ms", "deg"), some carry it in parentheses ("Sum loss (dB)")
    /// and some lead with it ("ms from peak"); a title longer than a unit is the
    /// NAME of the quantity, and names are left out. The two axes the app titles
    /// with nothing at all — frequency and phase — are known by their key instead,
    /// because they are the two the zoom box is drawn over most.
    /// </summary>
    private static string UnitOf(Axis axis)
    {
        string? title = axis.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            return axis.Key switch
            {
                PlotModelFactory.FrequencyAxisKey => Hertz,
                PlotModelFactory.PhaseAxisKey => Degrees,
                // A bottom log axis with no key is a frequency axis all the same —
                // the Time Alignment previews build theirs by hand.
                _ => axis is LogarithmicAxis && axis.IsHorizontal() ? Hertz : string.Empty,
            };
        }

        title = title.Trim();
        int opening = title.LastIndexOf('(');
        if (opening >= 0 && title.EndsWith(')'))
        {
            // Parentheses at the end of a title are the unit by convention, whatever
            // is in them: "Sum loss (dB)", "delay added to the upper channel (ms)".
            return title[(opening + 1)..^1].Trim();
        }

        // Otherwise the title has to BE a unit, whole or as its first word:
        // "dB", "samples", "ms from peak".
        string candidate = title.Split(' ', '\t')[0];
        return KnownUnits.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            ? candidate
            : string.Empty;
    }

    /// <summary>
    /// Where the label goes: OUTSIDE the box, beside the corner the drag ended at.
    /// The area being framed is what the user is looking at, and the number
    /// describing it must not sit on top of it. Clamped into the plot area, so a box
    /// drawn against an edge keeps its label on the graph instead of over the axis.
    /// </summary>
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

/// <summary>
/// Draws the zoom box and its read-out: the shaded rectangle while it is dragged and
/// for as long as it then waits to be clicked. The annotation belongs to the model it
/// was drawn over, so a rebuild — a new measurement, a mode switch — takes the box
/// with it rather than leaving it floating over a different graph.
/// </summary>
internal sealed class PlotZoomRectangleAnnotation : Annotation
{
    private static readonly OxyColor Fill = OxyColor.FromAColor(60, OxyColors.Gold);
    private static readonly OxyColor Stroke = OxyColor.FromAColor(220, OxyColors.Gold);
    private static readonly OxyColor LabelFill = OxyColor.FromAColor(220, OxyColors.Black);
    private static readonly OxyColor LabelStroke = OxyColor.FromAColor(140, OxyColors.White);

    /// <summary>Space between the size and the instruction under it, in pixels.</summary>
    private const double LineGap = 2;

    /// <summary>How much the instruction is set down from the size it explains.</summary>
    private const double HintFontStep = 1;

    private static readonly byte HintOpacity = 170;

    public PlotZoomRectangleAnnotation()
    {
        Layer = AnnotationLayer.AboveSeries;
    }

    /// <summary>What is framed, or null while no box is drawn.</summary>
    public PlotZoomBox? Box { get; set; }

    /// <summary>The size of the framed area; empty when there is nothing to read yet.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The instruction under it, or empty when the box is only a ruler.</summary>
    public string Hint { get; set; } = string.Empty;

    /// <summary>
    /// Which corner the drag ended at, so the label stays on the side the pointer
    /// left it on even after the box has travelled with a pan.
    /// </summary>
    public bool AnchorRight { get; set; }

    public bool AnchorBottom { get; set; }

    public override void Render(IRenderContext rc)
    {
        if (Box is not PlotZoomBox box || box.IsEmpty || PlotModel == null)
        {
            return;
        }

        OxyRect plotArea = PlotModel.PlotArea;
        OxyRect rectangle = box.Screen(plotArea);
        if (!TryIntersect(rectangle, plotArea, out OxyRect drawn))
        {
            // Panned or zoomed clean off the graph. The box is still remembered — it
            // is anchored to the data, and comes back with it.
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

        // Set down from the number it explains: the size is what is being read, the
        // instruction is only there until it has been read once.
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
