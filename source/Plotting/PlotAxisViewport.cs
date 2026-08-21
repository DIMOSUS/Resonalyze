using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze;

/// <summary>
/// A snapshot of one axis's visible range. It carries a user's zoom across a model
/// rebuild (the plot models are rebuilt from scratch on every settings change,
/// measurement and overlay toggle, so the axis objects that were zoomed are gone by
/// the time the new model is shown) and backs the zoom undo stack.
///
/// Axes are matched by <see cref="Key"/> whenever the axis has one, because the
/// models name their axes ("frequency", "decibel", "phase", ...) and two different
/// modes both put a left-hand <see cref="LinearAxis"/> in the same place: matching
/// those by position alone would restore a phase range onto a group-delay axis.
/// Position plus type is the fallback for the unnamed axes (the EQ wizard's dB axis,
/// the Virtual DSP value axis).
/// </summary>
internal sealed record PlotAxisViewport(
    string? Key,
    AxisPosition Position,
    Type AxisType,
    double Minimum,
    double Maximum)
{
    // Two ranges count as the same when they agree to this fraction of the span.
    // The comparison is between a range computed when a model was built and the
    // same range read back later, so the tolerance only has to absorb arithmetic,
    // not a user's gesture — the smallest wheel step moves an axis by percents.
    private const double RangeTolerance = 1e-6;

    /// <summary>Every axis's visible range, whoever decided it.</summary>
    public static IReadOnlyList<PlotAxisViewport> Capture(PlotModel? model)
    {
        if (model == null)
        {
            return Array.Empty<PlotAxisViewport>();
        }

        // ActualMinimum/ActualMaximum only refresh on render, so a capture taken
        // before the previous paint settled (common with Compare, whose model is
        // slower to build) would read the nominal range and drop the user's zoom.
        // Update the model in place first so the actual range reflects the live
        // pan/zoom synchronously, independent of paint timing.
        ((IPlotModel)model).Update(false);

        var viewports = new List<PlotAxisViewport>(model.Axes.Count);
        foreach (Axis axis in model.Axes)
        {
            viewports.Add(Describe(axis, axis.ActualMinimum, axis.ActualMaximum));
        }

        return viewports;
    }

    /// <summary>
    /// Only the ranges a USER has forced on the axes — the ones the model did not
    /// choose for itself.
    ///
    /// OxyPlot keeps exactly that distinction in <c>Axis.ViewMinimum</c> /
    /// <c>ViewMaximum</c>, which are <c>protected</c>, so it is read the only way the
    /// public API allows: reset the axes (which is what drops an override), let the
    /// model recompute the range it wants, and put back the ones that turn out to
    /// differ. What the plot shows is left exactly as it was found.
    ///
    /// Asking the axes rather than remembering what they looked like when the model
    /// was shown is what makes the answer independent of WHEN it is asked. Overlays
    /// join a plot after it is drawn — a mode switch restores its slots after the
    /// draw, Show All and the slot check boxes act later still — and on an
    /// auto-scaled axis they widen the range through the DATA, not through an
    /// override, so no arrival order can make that read as a zoom.
    /// </summary>
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

    /// <summary>True when both snapshots describe the same axis of the same plot.</summary>
    public bool SameAxis(PlotAxisViewport other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Key != null || other.Key != null
            ? Key == other.Key
            : Position == other.Position && AxisType == other.AxisType;
    }

    /// <summary>True when both snapshots show the same range, within arithmetic noise.</summary>
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
