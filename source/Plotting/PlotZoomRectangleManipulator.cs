using OxyPlot;

namespace Resonalyze;

/// <summary>
/// REW's zoom box, the drawing half: Ctrl + right drag frames an area on the graph
/// and reads its size out as it goes. What it does NOT do is zoom — in REW the box
/// stays on the graph when the button comes up and the scale only moves when the box
/// is clicked, which is what lets it be used as a measuring tape. The waiting, the
/// click and the zoom itself belong to <see cref="PlotGestureController"/>, which is
/// where the box outlives this manipulator.
/// </summary>
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
        // The base assigns the axes the drag is over, which is what the box is
        // measured in, so it goes first.
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
        // Below a few pixels there is nothing to draw and nothing to read: the box is
        // still a dot, and a "0.000 Hz" flashing under the cursor at every press is
        // noise.
        owner.UpdateZoomBox(
            PlotZoomRectangleReadout.WasDrawn(StartPosition, position)
                ? PlotZoomBox.Frame(XAxis, YAxis, StartPosition, position)
                : null,
            StartPosition,
            position);
    }
}
