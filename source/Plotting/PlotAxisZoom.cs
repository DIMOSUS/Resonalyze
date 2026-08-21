using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze;

/// <summary>
/// The axis arithmetic behind the plot zoom gestures, kept out of the controller so
/// it can be tested without a view: the wheel-delta to zoom-factor conversion, the
/// "pointer is over the end of an axis" hit test, and the single-limit zoom that
/// gesture performs.
/// </summary>
internal static class PlotAxisZoom
{
    /// <summary>
    /// Fraction of an axis's length at each end that reads as "the end of the axis"
    /// rather than the axis as a whole. A quarter at each end leaves the middle half
    /// for the ordinary "zoom this axis around the pointer" gesture.
    /// </summary>
    private const double EndZoneFraction = 0.25;

    /// <summary>
    /// The wheel factor for a fine step, matching what OxyPlot's own
    /// <c>ZoomWheelFine</c> command applies.
    /// </summary>
    public const double FineWheelFactor = 0.1;

    /// <summary>How far a keyboard or button zoom step moves: REW's "factor of about two".</summary>
    public const double StepZoomInScale = 2.0;
    public const double StepZoomOutScale = 0.5;

    /// <summary>
    /// Converts a wheel delta into an axis zoom factor using OxyPlot's own formula
    /// (<see cref="ZoomStepManipulator"/>), so a gesture handled here and a gesture
    /// handled by the stock manipulator move the axis by the same amount.
    /// </summary>
    public static double ScaleFromWheelDelta(int delta, double factor)
    {
        double step = delta * 0.001 * factor;
        return step > 0 ? 1 + step : 1.0 / (1 - step);
    }

    /// <summary>
    /// True when <paramref name="point"/> sits over one END of an axis's strip — the
    /// place where REW moves that single limit instead of zooming the whole axis.
    /// </summary>
    public static bool TryGetAxisEnd(
        PlotModel model,
        ScreenPoint point,
        out Axis? axis,
        out bool maximumEnd)
    {
        ArgumentNullException.ThrowIfNull(model);
        axis = null;
        maximumEnd = false;

        OxyRect area = model.PlotArea;
        bool belowOrAbove = point.Y > area.Bottom || point.Y < area.Top;
        bool leftOrRight = point.X < area.Left || point.X > area.Right;

        // Inside the plot area, or in a corner where the two strips overlap and the
        // gesture would be ambiguous.
        if (belowOrAbove == leftOrRight)
        {
            return false;
        }

        model.GetAxesFromPoint(point, out Axis xAxis, out Axis yAxis);
        Axis? candidate = belowOrAbove ? xAxis : yAxis;
        if (candidate == null || !candidate.IsZoomEnabled)
        {
            return false;
        }

        // Locate the pointer along the axis through the axis's own transform, so a
        // logarithmic or reversed axis is read the same way as a linear one.
        double start = candidate.Transform(candidate.ActualMinimum);
        double end = candidate.Transform(candidate.ActualMaximum);
        double span = end - start;
        if (Math.Abs(span) < 1)
        {
            return false;
        }

        double position = belowOrAbove ? point.X : point.Y;
        double fraction = (position - start) / span;
        if (fraction < EndZoneFraction)
        {
            axis = candidate;
            maximumEnd = false;
            return true;
        }

        if (fraction > 1 - EndZoneFraction)
        {
            axis = candidate;
            maximumEnd = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Moves one end of an axis, holding the other end still. Zooming AT the opposite
    /// end is what pins it: the anchor value keeps its place, so the range grows or
    /// shrinks from the far side only.
    /// </summary>
    public static void ZoomEnd(Axis axis, bool maximumEnd, double scale)
    {
        ArgumentNullException.ThrowIfNull(axis);
        if (!axis.IsZoomEnabled)
        {
            return;
        }

        axis.ZoomAt(scale, maximumEnd ? axis.ActualMinimum : axis.ActualMaximum);
    }

    /// <summary>
    /// Zooms one axis around the value under <paramref name="point"/>. Used by the
    /// keyboard steps (x/X, y/Y) and the on-graph zoom buttons, both of which zoom
    /// around the pointer like REW does.
    /// </summary>
    public static bool ZoomAxisAt(
        PlotModel model,
        ScreenPoint point,
        bool horizontal,
        double scale)
    {
        ArgumentNullException.ThrowIfNull(model);

        Axis? axis = FindAxis(model, point, horizontal);
        if (axis == null || !axis.IsZoomEnabled)
        {
            return false;
        }

        axis.ZoomAt(scale, axis.InverseTransform(horizontal ? point.X : point.Y));
        return true;
    }

    /// <summary>
    /// The axis of the requested orientation under the pointer, falling back to the
    /// one under the middle of the plot area. The fallback is what makes a keyboard
    /// step work while the pointer rests over the OTHER axis's strip, where
    /// <see cref="PlotModel.GetAxesFromPoint"/> deliberately reports one axis only.
    /// </summary>
    private static Axis? FindAxis(PlotModel model, ScreenPoint point, bool horizontal)
    {
        model.GetAxesFromPoint(point, out Axis xAxis, out Axis yAxis);
        Axis? axis = horizontal ? xAxis : yAxis;
        if (axis != null)
        {
            return axis;
        }

        OxyRect area = model.PlotArea;
        model.GetAxesFromPoint(
            new ScreenPoint(area.Left + (area.Width / 2), area.Top + (area.Height / 2)),
            out xAxis,
            out yAxis);
        return horizontal ? xAxis : yAxis;
    }

    /// <summary>
    /// The axis of one orientation that the user is allowed to move: the first
    /// visible, zoomable one. Colour axes are skipped — the waterfall's palette is
    /// positioned like a left-hand axis but is not a scale anybody pans. A mode that
    /// pins its scale (the waterfall, burst decay) has none, which is what tells the
    /// limits dialog and the on-graph buttons to stay out of the way.
    /// </summary>
    public static Axis? FindZoomableAxis(PlotModel model, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Axes.FirstOrDefault(axis =>
            axis is not IColorAxis &&
            axis.IsAxisVisible &&
            axis.IsZoomEnabled &&
            axis.IsHorizontal() == horizontal);
    }

    /// <summary>
    /// What to call an axis in the interface. The plots title the axes they can
    /// ("dB", "ms"); the frequency, phase and impulse axes carry a key instead, and
    /// the key is what the rest of the code calls them, so it is a truthful label.
    /// </summary>
    public static string DescribeAxis(Axis axis)
    {
        ArgumentNullException.ThrowIfNull(axis);

        if (!string.IsNullOrWhiteSpace(axis.Title))
        {
            return axis.Title;
        }

        return string.IsNullOrWhiteSpace(axis.Key)
            ? "unnamed"
            : char.ToUpperInvariant(axis.Key[0]) + axis.Key[1..];
    }

    /// <summary>
    /// The pointer position to zoom around when a command carries none: the last
    /// known pointer if it is over the plot area, the centre of the plot otherwise.
    /// </summary>
    public static ScreenPoint ClampToPlotArea(PlotModel model, ScreenPoint point)
    {
        ArgumentNullException.ThrowIfNull(model);

        OxyRect area = model.PlotArea;
        if (area.Width <= 0 || area.Height <= 0)
        {
            return point;
        }

        return new ScreenPoint(
            Math.Clamp(point.X, area.Left, area.Right),
            Math.Clamp(point.Y, area.Top, area.Bottom));
    }
}
