using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze;

/// <summary>
/// The Auto Tune window's From and To lines as handles: it reports where along the frequency axis a held edge went,
/// and the panel sets that field from it. It draws only the edge being worked, over the plot's own dashed line, and
/// a held edge's readout beside the pointer.
/// </summary>
internal sealed class EqWindowEdgesAnnotation : Annotation, IPlotDragHandles
{
    public const int From = 0;
    public const int To = 1;

    private const double Reach = 4;
    private const double LabelGap = 6;
    private const double LabelPaddingX = 6;
    private const double LabelPaddingY = 3;

    private static readonly OxyColor Line = UiPalette.CurveWindowFill.ToOxy();
    private static readonly OxyColor LabelFill = UiPalette.PlotZoomLabelFill.ToOxy();
    private static readonly OxyColor LabelStroke = UiPalette.PlotZoomLabelStroke.ToOxy();

    private readonly double[] edgesHz = new double[2];
    private int? hovered;
    private int? dragged;
    private double grabOffset;
    private ScreenPoint pointer;

    public EqWindowEdgesAnnotation()
    {
        Layer = AnnotationLayer.AboveSeries;
    }

    public event Action? Pressed;

    /// <summary>Where the held edge is now: <see cref="From"/> or <see cref="To"/>, and its frequency (Hz).</summary>
    public event Action<int, double>? Dragged;

    public void Show(double fromHz, double toHz)
    {
        edgesHz[From] = fromHz;
        edgesHz[To] = toHz;
    }

    public double ScreenX(int edge) => Transform(new DataPoint(edgesHz[edge], 0)).X;

    public int? HitTest(ScreenPoint point)
    {
        if (XAxis == null || YAxis == null || PlotModel == null)
        {
            return null;
        }

        double from = ScreenX(From);
        double to = ScreenX(To);
        bool nearFrom = Near(point, from);
        bool nearTo = Near(point, to);
        if (nearFrom && nearTo)
        {
            // Lines closer than the reach: the side of their middle picks, so they can always be pulled apart.
            return point.X < (from + to) / 2 ? From : To;
        }

        return nearFrom ? From : nearTo ? To : null;
    }

    public bool Hover(int? handle)
    {
        if (dragged != null || hovered == handle)
        {
            return false;
        }

        hovered = handle;
        return true;
    }

    public CursorType Cursor(int handle, OxyModifierKeys modifiers) => CursorType.ZoomHorizontal;

    public void Press(int handle, ScreenPoint point, OxyModifierKeys modifiers)
    {
        dragged = handle;
        hovered = handle;
        grabOffset = ScreenX(handle) - point.X;
        pointer = point;
        Pressed?.Invoke();
    }

    public void Drag(ScreenPoint point)
    {
        if (dragged is not int edge)
        {
            return;
        }

        pointer = point;
        Dragged?.Invoke(edge, InverseTransform(new ScreenPoint(point.X + grabOffset, point.Y)).X);
    }

    public void Release() => dragged = null;

    public bool Wheel(int handle, int delta) => false;

    public override void Render(IRenderContext rc)
    {
        base.Render(rc);
        if ((dragged ?? hovered) is not int edge)
        {
            return;
        }

        OxyRect area = PlotModel.PlotArea;
        double x = ScreenX(edge);
        if (!OnGraph(x))
        {
            return;
        }

        rc.DrawLine(
            [new ScreenPoint(x, area.Top), new ScreenPoint(x, area.Bottom)],
            Line,
            2,
            EdgeRenderingMode.PreferGeometricAccuracy);
        if (dragged == edge)
        {
            RenderReadout(rc, edge, x, area);
        }
    }

    // Outside the window, at the pointer's height, flipped to stay on the graph.
    private void RenderReadout(IRenderContext rc, int edge, double x, OxyRect area)
    {
        string text = $"{(edge == From ? "From" : "To")} {edgesHz[edge]:0} Hz";
        OxySize size = rc.MeasureText(text, ActualFont, ActualFontSize, ActualFontWeight);
        double width = size.Width + (2 * LabelPaddingX);
        double height = size.Height + (2 * LabelPaddingY);
        double left = edge == From ? x - LabelGap - width : x + LabelGap;
        if (left < area.Left || left + width > area.Right)
        {
            left = edge == From ? x + LabelGap : x - LabelGap - width;
        }

        var label = new OxyRect(
            Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(pointer.Y - (height / 2), area.Top, Math.Max(area.Top, area.Bottom - height)),
            width,
            height);
        rc.DrawRectangle(label, LabelFill, LabelStroke, 1, EdgeRenderingMode.PreferGeometricAccuracy);
        rc.DrawText(
            new ScreenPoint(label.Left + LabelPaddingX, label.Top + LabelPaddingY),
            text,
            ActualTextColor,
            ActualFont,
            ActualFontSize,
            ActualFontWeight);
    }

    // A line zoomed off the graph is not drawn, so the pointer at the graph's edge must not take it either.
    private bool Near(ScreenPoint point, double x) => OnGraph(x) && Math.Abs(point.X - x) <= Reach;

    private bool OnGraph(double x) => x >= PlotModel.PlotArea.Left && x <= PlotModel.PlotArea.Right;
}
