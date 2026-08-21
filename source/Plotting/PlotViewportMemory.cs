using OxyPlot;
using OxyPlot.Axes;
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
/// Only the axes the user MOVED are carried over, and they are found by asking the
/// axes themselves (<see cref="PlotAxisViewport.CaptureOverrides"/>) rather than by
/// remembering what the model looked like when it was drawn — which would depend on
/// WHEN the baseline was taken, and overlays arrive after the draw. An untouched
/// axis keeps whatever the next model computes for it, so the automatic behaviours
/// still work: the dB ceiling that lifts for a padded loopback, the group-delay
/// axis that fits its data, an auto-scaled axis that widens for an overlay.
/// </summary>
internal sealed class PlotViewportMemory
{
    private readonly PlotView view;
    private readonly Dictionary<Mode, IReadOnlyList<PlotAxisViewport>> savedByMode = new();

    private PlotModel? trackedModel;
    private Mode? trackedMode;

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
        if (savedByMode.TryGetValue(mode, out IReadOnlyList<PlotAxisViewport>? saved))
        {
            PlotAxisViewport.Apply(model, saved);
        }

        view.Model = model;
        trackedModel = model;
        trackedMode = mode;
    }

    /// <summary>
    /// Drops what a mode remembered. For a setting that changes what an axis MEANS
    /// (linear against logarithmic, dBr against dB SPL): the old numbers are not a
    /// range on the new axis, so the view is refitted instead of restored.
    /// </summary>
    public void Forget(Mode mode)
    {
        savedByMode.Remove(mode);
        if (trackedMode != mode || trackedModel == null)
        {
            return;
        }

        // The model on screen still carries the user's range; drop it there too, or
        // the capture that runs on the next redraw would save it straight back.
        foreach (Axis axis in trackedModel.Axes)
        {
            axis.Reset();
        }
    }

    private void Remember()
    {
        if (trackedMode is not Mode mode || trackedModel == null)
        {
            return;
        }

        IReadOnlyList<PlotAxisViewport> moved = PlotAxisViewport.CaptureOverrides(trackedModel);
        if (moved.Count == 0)
        {
            // Nothing is forced any more — a reset, or a mode that was never
            // touched. Forget it, so the next model is free to scale itself.
            savedByMode.Remove(mode);
            return;
        }

        savedByMode[mode] = moved;
    }
}
