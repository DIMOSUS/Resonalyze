using OxyPlot.WindowsForms;

namespace Resonalyze;

internal static class PlotInteraction
{
    /// <summary>Every plot view goes through here so gestures match everywhere. See docs/tech/plot-interaction.md.</summary>
    public static void Enable(PlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.Controller = new PlotGestureController(view);
    }
}
