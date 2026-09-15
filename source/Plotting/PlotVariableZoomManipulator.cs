using OxyPlot;

namespace Resonalyze;

/// <summary>REW's variable zoom (middle drag): horizontal drag zooms X, vertical zooms Y, around the press point.</summary>
internal sealed class PlotVariableZoomManipulator : MouseManipulator
{
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

        // Re-read the anchor pixel through the axis each step so the pressed point stays still.
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
