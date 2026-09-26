using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze;

/// <summary>View-free axis arithmetic for the zoom gestures. See docs/tech/plot-interaction.md#axis-zoom-arithmetic.</summary>
internal static class PlotAxisZoom
{
    /// <summary>Each end's share of the axis strip that moves a single limit; the middle half zooms the axis.</summary>
    private const double EndZoneFraction = 0.25;

    /// <summary>Matches OxyPlot's <c>ZoomWheelFine</c>.</summary>
    public const double FineWheelFactor = 0.1;

    public const double StepZoomInScale = 2.0;
    public const double StepZoomOutScale = 0.5;

    /// <summary>OxyPlot's own formula (<see cref="ZoomStepManipulator"/>), so custom and stock gestures move equally.</summary>
    public static double ScaleFromWheelDelta(int delta, double factor)
    {
        double step = delta * 0.001 * factor;
        return step > 0 ? 1 + step : 1.0 / (1 - step);
    }

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

        // Inside the plot area, or an ambiguous corner.
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

        // Through the axis transform, so log and reversed axes read like linear ones.
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

    /// <summary>Zooms at the opposite end, which pins it.</summary>
    public static void ZoomEnd(Axis axis, bool maximumEnd, double scale)
    {
        ArgumentNullException.ThrowIfNull(axis);
        if (!axis.IsZoomEnabled)
        {
            return;
        }

        axis.ZoomAt(scale, maximumEnd ? axis.ActualMinimum : axis.ActualMaximum);
    }

    public static bool ZoomAxisAt(
        PlotModel model,
        ScreenPoint point,
        bool horizontal,
        double scale) =>
        ZoomFoundAxis(model, point, horizontal, scale, _ => horizontal ? point.X : point.Y);

    /// <summary>The on-graph buttons: about the middle of the visible range, since the button itself sits at an end of the axis.</summary>
    public static bool ZoomAxisAboutCentre(
        PlotModel model,
        ScreenPoint point,
        bool horizontal,
        double scale) =>
        // Screen middle, so a log axis keeps its visual middle.
        ZoomFoundAxis(
            model,
            point,
            horizontal,
            scale,
            axis => (axis.Transform(axis.ActualMinimum) + axis.Transform(axis.ActualMaximum)) / 2);

    private static bool ZoomFoundAxis(
        PlotModel model,
        ScreenPoint point,
        bool horizontal,
        double scale,
        Func<Axis, double> screenAnchor)
    {
        ArgumentNullException.ThrowIfNull(model);

        Axis? axis = FindAxis(model, point, horizontal);
        if (axis == null || !axis.IsZoomEnabled)
        {
            return false;
        }

        axis.ZoomAt(scale, axis.InverseTransform(screenAnchor(axis)));
        return true;
    }

    /// <summary>Falls back to the axis under the plot centre: <see cref="PlotModel.GetAxesFromPoint"/> reports one axis only over a strip.</summary>
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

    /// <summary>First visible, zoomable, non-colour axis. Pinned modes (waterfall, burst decay) have none, which hides the dialog and buttons.</summary>
    public static Axis? FindZoomableAxis(PlotModel model, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Axes.FirstOrDefault(axis =>
            axis is not IColorAxis &&
            axis.IsAxisVisible &&
            axis.IsZoomEnabled &&
            axis.IsHorizontal() == horizontal);
    }

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
