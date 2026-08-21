using OxyPlot.WindowsForms;

namespace Resonalyze;

internal static class PlotInteraction
{
    /// <summary>
    /// Gives a plot the app's REW-shaped zoom, pan and limits gestures (see
    /// <see cref="PlotGestureController"/>). Every plot view goes through here, so
    /// the mouse behaves the same on the main plot, the EQ wizard and the Virtual
    /// DSP views.
    /// </summary>
    public static void Enable(PlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.Controller = new PlotGestureController(view);
    }
}
