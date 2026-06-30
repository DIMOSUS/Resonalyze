using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze;

// A large, faded caption drawn behind the series and centred in the plot area —
// a background watermark that stays centred regardless of zoom or pan.
public sealed class PlotWatermarkAnnotation : TextualAnnotation
{
    public PlotWatermarkAnnotation()
    {
        Layer = AnnotationLayer.BelowSeries;
    }

    // Vertical placement as a fraction of the plot area height: 0 is the top edge,
    // 0.5 the centre, 1 the bottom edge.
    public double VerticalPosition { get; init; } = 0.5;

    public override void Render(IRenderContext rc)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        OxyRect rect = PlotElementUtilities.GetClippingRect(this);
        var position = new ScreenPoint(
            (rect.Left + rect.Right) / 2.0,
            rect.Top + (rect.Bottom - rect.Top) * VerticalPosition);
        rc.DrawMathText(
            position,
            Text,
            ActualTextColor,
            ActualFont,
            ActualFontSize,
            ActualFontWeight,
            0,
            OxyPlot.HorizontalAlignment.Center,
            OxyPlot.VerticalAlignment.Middle);
    }
}
