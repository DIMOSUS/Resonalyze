using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze;

internal enum PlotCalloutDirection
{
    LeftUp,
    LeftDown,
    RightUp,
    RightDown
}

internal sealed class PlotCalloutMarkerAnnotation : Annotation
{
    public DataPoint AnchorPoint { get; init; }

    public string Text { get; init; } = string.Empty;

    public OxyColor Color { get; init; } = OxyColors.White;

    public PlotCalloutDirection Direction { get; init; } = PlotCalloutDirection.RightUp;

    public double StrokeThickness { get; init; } = 1.0;

    public double DotRadius { get; init; } = 2.0;

    public double DiagonalWidth { get; init; } = 13.0;

    public double DiagonalHeight { get; init; } = 14.0;

    public double ShelfLength { get; init; } = 42.0;

    public double TextGap { get; init; } = 2.0;

    public double LabelFontSize { get; init; } = 10.0;

    public double LabelFontWeight { get; init; } = FontWeights.Bold;

    public override void Render(IRenderContext rc)
    {
        base.Render(rc);

        ScreenPoint anchor = Transform(AnchorPoint);
        double horizontalSign = Direction is PlotCalloutDirection.RightUp or PlotCalloutDirection.RightDown
            ? 1.0
            : -1.0;
        double verticalSign = Direction is PlotCalloutDirection.LeftDown or PlotCalloutDirection.RightDown
            ? 1.0
            : -1.0;

        ScreenPoint knee = new(
            anchor.X + horizontalSign * DiagonalWidth,
            anchor.Y + verticalSign * DiagonalHeight);
        ScreenPoint shelfEnd = new(
            knee.X + horizontalSign * ShelfLength,
            knee.Y);

        rc.DrawEllipse(
            new OxyRect(
                anchor.X - DotRadius,
                anchor.Y - DotRadius,
                DotRadius * 2.0,
                DotRadius * 2.0),
            Color,
            Color,
            StrokeThickness,
            EdgeRenderingMode);

        rc.DrawLine(
            new[] { anchor, knee, shelfEnd },
            Color,
            StrokeThickness,
            EdgeRenderingMode,
            null,
            LineJoin.Miter);

        double textLeft = shelfEnd.X;
        ScreenPoint textPosition = new(textLeft, knee.Y - TextGap);
        rc.DrawText(
            textPosition,
            Text,
            Color,
            ActualFont,
            LabelFontSize,
            LabelFontWeight,
            0,
            horizontalSign > 0 ? OxyPlot.HorizontalAlignment.Right : OxyPlot.HorizontalAlignment.Left,
            OxyPlot.VerticalAlignment.Bottom,
            null);
    }
}
