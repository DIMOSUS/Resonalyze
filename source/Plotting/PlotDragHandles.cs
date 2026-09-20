using OxyPlot;

namespace Resonalyze;

/// <summary>
/// An annotation whose handles the pointer can grab. <see cref="PlotGestureController"/> asks it before its own left
/// press, double click and wheel, so a handle wins over the tracker, the limits dialog and zoom.
/// See docs/tech/plot-interaction.md#drag-handles.
/// </summary>
internal interface IPlotDragHandles
{
    /// <summary>The handle under the point, or null.</summary>
    int? HitTest(ScreenPoint point);

    /// <returns>Whether the highlight changed, so the plot needs a repaint.</returns>
    bool Hover(int? handle);

    void Press(int handle, ScreenPoint point);

    void Drag(ScreenPoint point);

    void Release();

    /// <returns>False leaves the wheel to zoom.</returns>
    bool Wheel(int handle, int delta);
}

/// <summary>Forwards a left drag to the grabbed handle; the pointer must travel a few pixels before a click becomes a drag.</summary>
internal sealed class PlotDragHandleManipulator : MouseManipulator
{
    private const double MinimumTravel = 3;

    private readonly IPlotDragHandles handles;
    private readonly int handle;
    private readonly Action<bool> dragging;
    private bool moved;

    public PlotDragHandleManipulator(IPlotView view, IPlotDragHandles handles, int handle, Action<bool> dragging)
        : base(view)
    {
        this.handles = handles;
        this.handle = handle;
        this.dragging = dragging;
    }

    public override void Started(OxyMouseEventArgs e)
    {
        base.Started(e);
        dragging(true);
        PlotView.SetCursorType(CursorType.Pan);
        handles.Press(handle, e.Position);
        PlotView.InvalidatePlot(false);
    }

    public override void Delta(OxyMouseEventArgs e)
    {
        base.Delta(e);
        ScreenVector travel = e.Position - StartPosition;
        if (!moved && Math.Max(Math.Abs(travel.X), Math.Abs(travel.Y)) < MinimumTravel)
        {
            return;
        }

        moved = true;
        handles.Drag(e.Position);
    }

    public override void Completed(OxyMouseEventArgs e)
    {
        base.Completed(e);
        handles.Release();
        dragging(false);
        PlotView.SetCursorType(CursorType.Default);
        PlotView.InvalidatePlot(false);
    }
}
