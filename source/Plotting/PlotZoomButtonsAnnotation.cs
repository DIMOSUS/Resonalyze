using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze;

/// <summary>One of the four on-graph zoom buttons: which axis it moves, and which way.</summary>
internal readonly record struct PlotZoomButton(ScreenPoint Center, bool Horizontal, bool ZoomIn);

/// <summary>
/// REW's on-graph zoom buttons: a plus/minus pair against each axis that appears
/// while the pointer is over the graph and zooms that axis by about two. They are
/// the discoverable half of the zoom gestures — a user who never reads a shortcut
/// list still finds them — so they exist for the same reason REW has them, not
/// because the wheel is not enough.
///
/// The layout is static: it reads the model's plot area and its axes, so the
/// controller can hit-test the buttons without a render pass of its own.
/// </summary>
internal static class PlotZoomButtons
{
    public const double Radius = 9;

    // Distance from the pair's midpoint to each button's centre, and from the plot
    // area's edge to the pair. Kept clear of the axis labels but inside the frame,
    // so the buttons read as belonging to the axis they move.
    private const double Spacing = 12;
    private const double Inset = 20;

    // Below this the plot is a thumbnail (the history window's previews, a collapsed
    // panel) and the buttons would cover the curve they are meant to help read.
    private const double MinimumPlotSize = 160;

    /// <summary>
    /// The buttons a model actually offers: a pair per axis the user may move. A
    /// mode that pins its scale gets none rather than buttons that do nothing.
    /// </summary>
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

/// <summary>
/// Draws <see cref="PlotZoomButtons"/> over the plot. The controller owns the
/// pointer: the buttons are only drawn while it is inside the plot area, and the
/// one under it is drawn brighter.
/// </summary>
internal sealed class PlotZoomButtonsAnnotation : Annotation
{
    private static readonly OxyColor Fill = OxyColor.FromAColor(70, OxyColors.Black);
    private static readonly OxyColor Stroke = OxyColor.FromAColor(120, OxyColors.White);
    private static readonly OxyColor HoveredFill = OxyColor.FromAColor(150, OxyColors.Black);
    private static readonly OxyColor HoveredStroke = OxyColor.FromAColor(230, OxyColors.White);

    public PlotZoomButtonsAnnotation()
    {
        Layer = AnnotationLayer.AboveSeries;
    }

    /// <summary>Where the pointer is, or null when it is not over the plot area.</summary>
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

        // The glyph: a minus bar, plus a vertical stroke for zoom in. Drawn rather
        // than typeset so it stays crisp and centred at any DPI.
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
