using OxyPlot;

namespace Resonalyze;

/// <summary>
/// REW's "variable zoom": hold the middle mouse button and drag — right and left
/// zoom the horizontal axis in and out, up and down do the same for the vertical
/// one, both around the point where the button went down. It replaces OxyPlot's
/// default middle-button zoom rectangle, which REW puts on Ctrl + right drag
/// instead (where OxyPlot also has it).
/// </summary>
internal sealed class PlotVariableZoomManipulator : MouseManipulator
{
    /// <summary>
    /// Drag distance that doubles (or halves) an axis. Roughly a thumb's travel:
    /// short enough to cross two octaves in one gesture, long enough to land on a
    /// particular decade without fighting it.
    /// </summary>
    private const double PixelsPerDoubling = 150;

    private ScreenPoint anchor;
    private ScreenPoint previous;

    public PlotVariableZoomManipulator(IPlotView plotView)
        : base(plotView)
    {
    }

    public override void Started(OxyMouseEventArgs e)
    {
        base.Started(e);
        anchor = e.Position;
        previous = e.Position;
        PlotView.SetCursorType(CursorType.ZoomRectangle);
    }

    public override void Delta(OxyMouseEventArgs e)
    {
        base.Delta(e);

        double dx = e.Position.X - previous.X;
        double dy = previous.Y - e.Position.Y;
        previous = e.Position;

        // The anchor is held in screen coordinates and re-read through the axis on
        // every step: zooming AT the value currently under that pixel is what keeps
        // the pressed point still while the scale changes underneath it.
        if (XAxis is { IsZoomEnabled: true } && dx != 0)
        {
            XAxis.ZoomAt(ScaleFor(dx), XAxis.InverseTransform(anchor.X));
        }

        if (YAxis is { IsZoomEnabled: true } && dy != 0)
        {
            YAxis.ZoomAt(ScaleFor(dy), YAxis.InverseTransform(anchor.Y));
        }

        PlotView.InvalidatePlot(false);
    }

    public override void Completed(OxyMouseEventArgs e)
    {
        base.Completed(e);
        PlotView.SetCursorType(CursorType.Default);
    }

    private static double ScaleFor(double pixels) =>
        Math.Pow(2, pixels / PixelsPerDoubling);
}
