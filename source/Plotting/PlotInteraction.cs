using OxyPlot;
using OxyPlot.WindowsForms;

namespace Resonalyze;

internal static class PlotInteraction
{
    /// <summary>
    /// Makes a double-click reset the axis under the cursor (or both axes when the
    /// cursor is in the plot area) back to the scale defined when the model was built,
    /// undoing any zoom or pan. The standard pan/zoom/track bindings are preserved.
    /// </summary>
    public static void EnableDoubleClickAxisReset(PlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var controller = new PlotController();
        controller.BindMouseDown(
            OxyMouseButton.Left,
            OxyModifierKeys.None,
            clickCount: 2,
            PlotCommands.ResetAt);
        view.Controller = controller;
    }
}
