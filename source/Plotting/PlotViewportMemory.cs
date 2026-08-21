using OxyPlot;
using OxyPlot.WindowsForms;

namespace Resonalyze;

/// <summary>
/// Keeps each mode's zoom on the main plot. The plot models are rebuilt from
/// scratch on every settings change, every new measurement and every overlay
/// toggle, so without this a zoom lasts until the next redraw — which, in a tool
/// whose whole point is to change a setting and look at what moved, is until the
/// next thing the user does. REW holds its limits until they are changed; this is
/// the same contract, kept per mode so the frequency plot and the impulse plot do
/// not fight over one range.
///
/// Only the axes the user MOVED are carried over, found by comparing what an axis
/// shows now against what it showed when its model was built. An untouched axis
/// keeps whatever the next model computes for it, so the automatic behaviours
/// still work: the dB ceiling that lifts for a padded loopback, the group-delay
/// axis that fits its data, the impulse axes that follow the curve.
/// </summary>
internal sealed class PlotViewportMemory
{
    private readonly PlotView view;
    private readonly Dictionary<Mode, IReadOnlyList<PlotAxisViewport>> savedByMode = new();

    private PlotModel? trackedModel;
    private Mode? trackedMode;

    // What the tracked model's axes showed before the user touched them. Anything
    // that differs from this is a zoom or a pan and is worth carrying over; anything
    // that matches is still the model's own decision.
    private IReadOnlyList<PlotAxisViewport> trackedNominal = Array.Empty<PlotAxisViewport>();

    public PlotViewportMemory(PlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        this.view = view;
    }

    public PlotModel? Model => view.Model;

    /// <summary>
    /// Puts a model on the view, carrying over the zoom of the mode it belongs to.
    /// The carry-over is applied BEFORE the model is shown: overlays force a
    /// synchronous repaint while drawing, and a zoom restored afterwards would
    /// flash the default scale first, reading as a jump on every redraw.
    /// </summary>
    public void Show(PlotModel? model, Mode mode)
    {
        Remember();

        // The new model's own idea of its scale, read before the carry-over is
        // applied on top of it — that is what "the user has not touched this axis"
        // will be compared against next time.
        IReadOnlyList<PlotAxisViewport> nominal = PlotAxisViewport.Capture(model);
        if (savedByMode.TryGetValue(mode, out IReadOnlyList<PlotAxisViewport>? saved))
        {
            PlotAxisViewport.Apply(model, saved);
        }

        view.Model = model;
        trackedModel = model;
        trackedMode = mode;
        trackedNominal = nominal;
    }

    /// <summary>
    /// Drops what a mode remembered. For a setting that changes what an axis MEANS
    /// (linear against logarithmic, dBr against dB SPL): the old numbers are not a
    /// range on the new axis, so the view is refitted instead of restored.
    /// </summary>
    public void Forget(Mode mode)
    {
        savedByMode.Remove(mode);
        if (trackedMode == mode)
        {
            // Stop the model on screen from putting the same range straight back.
            trackedNominal = PlotAxisViewport.Capture(trackedModel);
        }
    }

    private void Remember()
    {
        if (trackedMode is not Mode mode || trackedModel == null)
        {
            return;
        }

        List<PlotAxisViewport> moved = PlotAxisViewport.Capture(trackedModel)
            .Where(IsMoved)
            .ToList();
        if (moved.Count == 0)
        {
            // Everything is back where the model put it — a reset, or a mode that was
            // never touched. Forget the mode so the next model is free to scale itself.
            savedByMode.Remove(mode);
            return;
        }

        savedByMode[mode] = moved;
    }

    private bool IsMoved(PlotAxisViewport viewport)
    {
        PlotAxisViewport? nominal = trackedNominal.FirstOrDefault(viewport.SameAxis);

        // An axis with no nominal to compare against is one this model gained after
        // the capture; leave it to the model rather than pinning a range nobody set.
        return nominal != null && !viewport.SameRange(nominal);
    }
}
