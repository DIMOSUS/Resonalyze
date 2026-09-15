using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze;

// Faded caption behind the series, centred in the plot area regardless of zoom or pan.
public sealed class PlotWatermarkAnnotation : TextualAnnotation
{
    public PlotWatermarkAnnotation()
    {
        Layer = AnnotationLayer.BelowSeries;
    }

    // Fraction of plot height from the top.
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
