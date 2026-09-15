using OxyPlot;

namespace Resonalyze;

/// <summary>Draws and measures the zoom box only; the waiting box and zooming click belong to <see cref="PlotGestureController"/>.</summary>
internal sealed class PlotZoomRectangleManipulator : MouseManipulator
{
    private readonly PlotGestureController owner;

    public PlotZoomRectangleManipulator(IPlotView plotView, PlotGestureController owner)
        : base(plotView)
    {
        ArgumentNullException.ThrowIfNull(owner);
        this.owner = owner;
    }

    public override void Started(OxyMouseEventArgs e)
    {
        // The base assigns the axes the box is measured in.
        base.Started(e);
        PlotView.SetCursorType(CursorType.ZoomRectangle);
        owner.BeginZoomBox();
        Draw(e.Position);
    }

    public override void Delta(OxyMouseEventArgs e)
    {
        base.Delta(e);
        Draw(e.Position);
    }

    public override void Completed(OxyMouseEventArgs e)
    {
        base.Completed(e);
        PlotView.SetCursorType(CursorType.Default);
        owner.FinishZoomBox(
            PlotZoomBox.Frame(XAxis, YAxis, StartPosition, e.Position),
            StartPosition,
            e.Position);
    }

    private void Draw(ScreenPoint position)
    {
        owner.UpdateZoomBox(
            PlotZoomRectangleReadout.WasDrawn(StartPosition, position)
                ? PlotZoomBox.Frame(XAxis, YAxis, StartPosition, position)
                : null,
            StartPosition,
            position);
    }
}
