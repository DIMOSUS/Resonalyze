using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze;

/// <summary>One axis's visible range, for carrying zoom across model rebuilds and for undo. Matched by <see cref="Key"/>, else position + type.
/// See docs/tech/plot-interaction.md#axis-snapshots.</summary>
internal sealed record PlotAxisViewport(
    string? Key,
    AxisPosition Position,
    Type AxisType,
    double Minimum,
    double Maximum)
{
    // Absorbs arithmetic between build-time and read-back ranges only; the smallest wheel step moves percents.
    private const double RangeTolerance = 1e-6;

    public static IReadOnlyList<PlotAxisViewport> Capture(PlotModel? model)
    {
        if (model == null)
        {
            return Array.Empty<PlotAxisViewport>();
        }

        // Actual ranges refresh only on render; update first so a capture before paint settles does not drop the zoom.
        ((IPlotModel)model).Update(false);

        var viewports = new List<PlotAxisViewport>(model.Axes.Count);
        foreach (Axis axis in model.Axes)
        {
            viewports.Add(Describe(axis, axis.ActualMinimum, axis.ActualMaximum));
        }

        return viewports;
    }

    /// <summary>Only ranges a user forced: reset, recompute, restore those that differ (OxyPlot's ViewMinimum is protected).
    /// Independent of when overlays arrive. See docs/tech/plot-interaction.md#axis-override-capture.</summary>
    public static IReadOnlyList<PlotAxisViewport> CaptureOverrides(PlotModel? model)
    {
        if (model == null)
        {
            return Array.Empty<PlotAxisViewport>();
        }

        var plot = (IPlotModel)model;
        plot.Update(false);
        List<PlotAxisViewport> shown = model.Axes
            .Select(axis => Describe(axis, axis.ActualMinimum, axis.ActualMaximum))
            .ToList();

        foreach (Axis axis in model.Axes)
        {
            axis.Reset();
        }

        plot.Update(false);

        var overrides = new List<PlotAxisViewport>();
        for (int index = 0; index < model.Axes.Count; index++)
        {
            Axis axis = model.Axes[index];
            PlotAxisViewport wasShowing = shown[index];
            if (!double.IsFinite(wasShowing.Minimum) ||
                !double.IsFinite(wasShowing.Maximum) ||
                wasShowing.SameRange(Describe(axis, axis.ActualMinimum, axis.ActualMaximum)))
            {
                continue;
            }

            axis.Zoom(wasShowing.Minimum, wasShowing.Maximum);
            overrides.Add(wasShowing);
        }

        return overrides;
    }

    private static PlotAxisViewport Describe(Axis axis, double minimum, double maximum) =>
        new(
            string.IsNullOrEmpty(axis.Key) ? null : axis.Key,
            axis.Position,
            axis.GetType(),
            minimum,
            maximum);

    public static void Apply(
        PlotModel? model,
        IReadOnlyList<PlotAxisViewport>? viewports)
    {
        if (model == null || viewports == null || viewports.Count == 0)
        {
            return;
        }

        foreach (Axis axis in model.Axes)
        {
            PlotAxisViewport? viewport = Match(axis, viewports);
            if (viewport == null ||
                !double.IsFinite(viewport.Minimum) ||
                !double.IsFinite(viewport.Maximum) ||
                viewport.Maximum <= viewport.Minimum)
            {
                continue;
            }

            axis.Zoom(viewport.Minimum, viewport.Maximum);
        }
    }

    public bool SameAxis(PlotAxisViewport other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Key != null || other.Key != null
            ? Key == other.Key
            : Position == other.Position && AxisType == other.AxisType;
    }

    public bool SameRange(PlotAxisViewport other)
    {
        ArgumentNullException.ThrowIfNull(other);
        double tolerance = Math.Max(Math.Abs(Maximum - Minimum), 1e-12) * RangeTolerance;
        return Math.Abs(Minimum - other.Minimum) <= tolerance &&
            Math.Abs(Maximum - other.Maximum) <= tolerance;
    }

    private static PlotAxisViewport? Match(
        Axis axis,
        IReadOnlyList<PlotAxisViewport> viewports)
    {
        if (!string.IsNullOrEmpty(axis.Key))
        {
            return viewports.FirstOrDefault(item => item.Key == axis.Key);
        }

        return viewports.FirstOrDefault(item =>
            item.Key == null &&
            item.Position == axis.Position &&
            item.AxisType == axis.GetType());
    }
}
