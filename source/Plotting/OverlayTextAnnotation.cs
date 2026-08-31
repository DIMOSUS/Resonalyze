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

    /// <summary>
    /// Plot-area overlay text. <c>TextPosition.X</c> is the 0..1 fraction across the
    /// plot area; <c>TextPosition.Y</c> is the line slot counted from the flowing
    /// edge (top for <see cref="TextFlowDirection.TopDown"/>, bottom for
    /// <see cref="TextFlowDirection.BottomUp"/>). The FIRST line of the text sits in
    /// that slot and a multi-line block grows with the flow — into the plot, never
    /// past its edge. (The block used to be centred on the slot instead, which
    /// pushed the first line of any multi-line note half out of the plot area.)
    /// </summary>
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
            double y = textHeight * TextPosition.Y;
            double screenY = TextFlowDirection == TextFlowDirection.TopDown
                ? axisRect.Top + y
                : axisRect.Bottom - y;
            var position = new ScreenPoint(
                (1.0 - x) * axisRect.BottomLeft.X + x * axisRect.TopRight.X,
                screenY);

            // Anchor the block's flowing edge at the slot: a single line renders
            // exactly where the old centre-on-slot math put it, while extra lines
            // extend into the plot instead of being clipped by its border.
            this.GetActualTextAlignment(out var ha, out _);
            var va = TextFlowDirection == TextFlowDirection.TopDown
                ? VerticalAlignment.Top
                : VerticalAlignment.Bottom;
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
