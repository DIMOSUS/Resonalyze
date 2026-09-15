using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;

namespace Resonalyze;

/// <summary>Per-mode zoom carried across model rebuilds; only user-moved axes are restored. See docs/tech/plot-interaction.md#viewport-memory.</summary>
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

    /// <summary>Applies the zoom before showing: overlays repaint synchronously, and a late restore flashes the default scale.</summary>
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

    /// <summary>For settings that change what an axis means (log/linear, dBr/SPL): refit instead of restore.</summary>
    public void Forget(Mode mode)
    {
        savedByMode.Remove(mode);
        if (trackedMode != mode || trackedModel == null)
        {
            return;
        }

        // Reset the on-screen axes too, or the next capture saves the range straight back.
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
            savedByMode.Remove(mode);
            return;
        }

        savedByMode[mode] = moved;
    }
}
