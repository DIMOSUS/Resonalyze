using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze;

internal readonly record struct PlotZoomButton(ScreenPoint Center, bool Horizontal, bool ZoomIn);

/// <summary>REW's on-graph plus/minus pair per movable axis, shown while the pointer is over the graph. Static layout so the controller
/// hit-tests without a render pass.</summary>
internal static class PlotZoomButtons
{
    public const double Radius = 9;

    private const double Spacing = 12;
    private const double Inset = 20;

    // Thumbnails (history previews, collapsed panels): buttons would cover the curve.
    private const double MinimumPlotSize = 160;

    public static IReadOnlyList<PlotZoomButton> Layout(PlotModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        bool horizontal = PlotAxisZoom.FindZoomableAxis(model, horizontal: true) != null;
        bool vertical = PlotAxisZoom.FindZoomableAxis(model, horizontal: false) != null;
        if (!horizontal && !vertical)
        {
            return Array.Empty<PlotZoomButton>();
        }

        return Layout(model.PlotArea)
            .Where(button => button.Horizontal ? horizontal : vertical)
            .ToList();
    }

    public static IReadOnlyList<PlotZoomButton> Layout(OxyRect plotArea)
    {
        if (plotArea.Width < MinimumPlotSize || plotArea.Height < MinimumPlotSize)
        {
            return Array.Empty<PlotZoomButton>();
        }

        double centerX = (plotArea.Left + plotArea.Right) / 2;
        double centerY = (plotArea.Top + plotArea.Bottom) / 2;
        double bottom = plotArea.Bottom - Inset;
        double left = plotArea.Left + Inset;

        return
        [
            new PlotZoomButton(new ScreenPoint(centerX - Spacing, bottom), Horizontal: true, ZoomIn: false),
            new PlotZoomButton(new ScreenPoint(centerX + Spacing, bottom), Horizontal: true, ZoomIn: true),
            new PlotZoomButton(new ScreenPoint(left, centerY + Spacing), Horizontal: false, ZoomIn: false),
            new PlotZoomButton(new ScreenPoint(left, centerY - Spacing), Horizontal: false, ZoomIn: true),
        ];
    }

    public static bool TryHit(PlotModel model, ScreenPoint point, out PlotZoomButton hit)
    {
        foreach (PlotZoomButton button in Layout(model))
        {
            double dx = point.X - button.Center.X;
            double dy = point.Y - button.Center.Y;
            if ((dx * dx) + (dy * dy) <= Radius * Radius)
            {
                hit = button;
                return true;
            }
        }

        hit = default;
        return false;
    }
}

internal sealed class PlotZoomButtonsAnnotation : Annotation
{
    private static readonly OxyColor Fill = UiPalette.PlotOverlayFill.ToOxy();
    private static readonly OxyColor Stroke = UiPalette.PlotOverlayStroke.ToOxy();
    private static readonly OxyColor HoveredFill = UiPalette.PlotOverlayFillHovered.ToOxy();
    private static readonly OxyColor HoveredStroke = UiPalette.PlotOverlayStrokeHovered.ToOxy();

    public PlotZoomButtonsAnnotation()
    {
        Layer = AnnotationLayer.AboveSeries;
    }

    public ScreenPoint? Pointer { get; set; }

    public override void Render(IRenderContext rc)
    {
        if (Pointer is not ScreenPoint pointer || PlotModel == null)
        {
            return;
        }

        foreach (PlotZoomButton button in PlotZoomButtons.Layout(PlotModel))
        {
            double dx = pointer.X - button.Center.X;
            double dy = pointer.Y - button.Center.Y;
            bool hovered = (dx * dx) + (dy * dy) <= PlotZoomButtons.Radius * PlotZoomButtons.Radius;
            RenderButton(rc, button, hovered);
        }
    }

    private static void RenderButton(IRenderContext rc, PlotZoomButton button, bool hovered)
    {
        OxyColor stroke = hovered ? HoveredStroke : Stroke;
        rc.DrawCircle(
            button.Center,
            PlotZoomButtons.Radius,
            hovered ? HoveredFill : Fill,
            stroke,
            1,
            EdgeRenderingMode.PreferGeometricAccuracy);

        // Drawn rather than typeset so the glyph stays crisp and centred at any DPI.
        const double arm = PlotZoomButtons.Radius - 4;
        rc.DrawLine(
            [
                new ScreenPoint(button.Center.X - arm, button.Center.Y),
                new ScreenPoint(button.Center.X + arm, button.Center.Y)
            ],
            stroke,
            1.6,
            EdgeRenderingMode.PreferGeometricAccuracy,
            null,
            LineJoin.Miter);

        if (!button.ZoomIn)
        {
            return;
        }

        rc.DrawLine(
            [
                new ScreenPoint(button.Center.X, button.Center.Y - arm),
                new ScreenPoint(button.Center.X, button.Center.Y + arm)
            ],
            stroke,
            1.6,
            EdgeRenderingMode.PreferGeometricAccuracy,
            null,
            LineJoin.Miter);
    }
}
