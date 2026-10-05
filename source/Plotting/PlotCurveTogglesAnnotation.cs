using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze;

/// <summary>Check boxes in the plot's top-left corner, one per curve group; a press toggles one. Laid out from the plot area
/// alone, so the controller hit-tests without a render pass. See docs/tech/plot-interaction.md#curve-toggles.</summary>
internal sealed class PlotCurveTogglesAnnotation : Annotation, IPlotDragHandles
{
    private const double BoxSize = 12;
    private const double RowHeight = 18;
    private const double RowWidth = 62;
    private const double LabelGap = 6;
    private const double Padding = 4;
    private const double TopInset = 8;
    private const double LabelFontSize = 11;

    // Below this the column would cover the curves it controls.
    private const double MinimumPlotSize = 120;

    private static readonly OxyColor Backdrop = UiPalette.PlotLegendBackground.ToOxy();
    private static readonly OxyColor Label = UiPalette.TextDefault.ToOxy();
    private static readonly OxyColor Fill = UiPalette.PlotOverlayFill.ToOxy();
    private static readonly OxyColor Stroke = UiPalette.PlotOverlayStroke.ToOxy();
    private static readonly OxyColor HoveredFill = UiPalette.PlotOverlayFillHovered.ToOxy();
    private static readonly OxyColor HoveredStroke = UiPalette.PlotOverlayStrokeHovered.ToOxy();

    private readonly string[] labels;
    private readonly bool[] states;
    private int? hovered;

    public PlotCurveTogglesAnnotation(IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        this.labels = [.. labels];
        states = Enumerable.Repeat(true, this.labels.Length).ToArray();
        Layer = AnnotationLayer.AboveSeries;
    }

    /// <summary>Raised with the box's index when a press toggles it.</summary>
    public event Action<int>? Toggled;

    /// <summary>False draws nothing and takes no press: a plot with nothing to toggle.</summary>
    public bool Shown { get; set; } = true;

    public bool IsChecked(int index) => states[index];

    /// <summary>Sets a box without raising <see cref="Toggled"/>.</summary>
    public void SetChecked(int index, bool value) => states[index] = value;

    /// <summary>The box and its label, or null while the boxes are not drawn.</summary>
    public OxyRect? RowBounds(int index)
    {
        if (!Shown || PlotModel is not { } model ||
            model.PlotArea.Width < MinimumPlotSize || model.PlotArea.Height < MinimumPlotSize)
        {
            return null;
        }

        return new OxyRect(
            model.PlotArea.Left + PlotZoomButtons.LeftPairClearance + Padding,
            model.PlotArea.Top + TopInset + (index * RowHeight),
            RowWidth,
            RowHeight);
    }

    public int? HitTest(ScreenPoint point)
    {
        for (int i = 0; i < labels.Length; i++)
        {
            if (RowBounds(i) is OxyRect row && row.Contains(point.X, point.Y))
            {
                return i;
            }
        }

        return null;
    }

    public bool Hover(int? handle)
    {
        bool changed = hovered != handle;
        hovered = handle;
        return changed;
    }

    public CursorType Cursor(int handle, OxyModifierKeys modifiers) => CursorType.Pan;

    public void Press(int handle, ScreenPoint point, OxyModifierKeys modifiers)
    {
        states[handle] = !states[handle];
        Toggled?.Invoke(handle);
    }

    public void Drag(ScreenPoint point)
    {
    }

    public void Release()
    {
    }

    public bool Wheel(int handle, int delta) => false;

    public override void Render(IRenderContext rc)
    {
        if (labels.Length == 0 || RowBounds(0) is not OxyRect first)
        {
            return;
        }

        rc.DrawRectangle(
            new OxyRect(
                first.Left - Padding,
                first.Top - (Padding / 2),
                RowWidth + (2 * Padding),
                (labels.Length * RowHeight) + Padding),
            Backdrop,
            OxyColors.Undefined,
            0,
            EdgeRenderingMode.PreferSharpness);
        for (int i = 0; i < labels.Length; i++)
        {
            double top = first.Top + (i * RowHeight);
            OxyColor stroke = hovered == i ? HoveredStroke : Stroke;
            var box = new OxyRect(first.Left, top + ((RowHeight - BoxSize) / 2), BoxSize, BoxSize);
            rc.DrawRectangle(
                box, hovered == i ? HoveredFill : Fill, stroke, 1, EdgeRenderingMode.PreferSharpness);
            if (states[i])
            {
                // Drawn rather than typeset, as the zoom buttons' glyphs are.
                rc.DrawLine(
                    [
                        new ScreenPoint(box.Left + 2.5, box.Top + 6.5),
                        new ScreenPoint(box.Left + 5, box.Bottom - 3),
                        new ScreenPoint(box.Right - 2.5, box.Top + 3)
                    ],
                    stroke,
                    1.6,
                    EdgeRenderingMode.PreferGeometricAccuracy,
                    null,
                    LineJoin.Miter);
            }

            rc.DrawText(
                new ScreenPoint(box.Right + LabelGap, top + (RowHeight / 2)),
                labels[i],
                Label,
                PlotModel.DefaultFont,
                LabelFontSize,
                FontWeights.Normal,
                0,
                OxyPlot.HorizontalAlignment.Left,
                OxyPlot.VerticalAlignment.Middle,
                null);
        }
    }
}
