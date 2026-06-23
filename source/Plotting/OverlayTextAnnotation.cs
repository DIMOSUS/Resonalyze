using System;
using System.Collections.Generic;
using System.Text;
using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze
{
    public enum TextFlowDirection
    {
        TopDown,
        BottomUp
    }

    public class OverlayTextAnnotation : TextualAnnotation
    {
        public bool IsPlotLabelOverlay { get; init; }
        public TextFlowDirection TextFlowDirection { get; init; } =
            TextFlowDirection.BottomUp;

        public override void Render(IRenderContext rc)
        {
            if (this.Text == null)
            {
                return;
            }

            var axisRect = PlotElementUtilities.GetClippingRect(this);
            var textHeight = rc.MeasureText("X", this.ActualFont, this.ActualFontSize, this.ActualFontWeight).Height;
            double x = TextPosition.X;
            double y = textHeight * (0.5 + 1.0 * TextPosition.Y);
            double screenY = TextFlowDirection == TextFlowDirection.TopDown
                ? axisRect.Top + y
                : axisRect.Bottom - y;
            var position = new ScreenPoint(
                (1.0 - x) * axisRect.BottomLeft.X + x * axisRect.TopRight.X,
                screenY);

            this.GetActualTextAlignment(out var ha, out var va);
            rc.DrawMathText(
                position,
                this.Text,
                this.GetSelectableFillColor(this.ActualTextColor),
                this.ActualFont,
                this.ActualFontSize,
                this.ActualFontWeight,
                0,
                ha,
                va);
        }
    }
}
