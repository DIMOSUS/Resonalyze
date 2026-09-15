using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

/// <summary>Extra window size goes to the two plots, keeping their designed proportion.</summary>
public sealed class VirtualCrossoverPanelLayoutTests
{
    [Fact]
    public void TheDesignedSize_IsLeftExactlyAsTheDesignerDrewIt()
    {
        using var panel = new VirtualCrossoverPanel();
        (PlotView main, PlotView dsp) = Plots(panel);
        Rectangle mainDesign = main.Bounds;
        Rectangle dspDesign = dsp.Bounds;

        panel.Size = new Size(panel.Width - 200, panel.Height - 200);
        Assert.Equal(mainDesign, main.Bounds);
        Assert.Equal(dspDesign, dsp.Bounds);
    }

    [Fact]
    public void ABiggerPanel_SplitsTheExtraHeightInTheDesignedProportion()
    {
        using var panel = new VirtualCrossoverPanel();
        (PlotView main, PlotView dsp) = Plots(panel);
        double designedRatio = main.Height / (double)dsp.Height;
        int designedHeights = main.Height + dsp.Height;
        Size design = panel.Size;

        panel.Size = new Size(design.Width + 500, design.Height + 300);

        Assert.True(main.Height > 0 && dsp.Height > 0);
        Assert.Equal(designedRatio, main.Height / (double)dsp.Height, 2);
        Assert.Equal(designedHeights + 300, main.Height + dsp.Height);
    }

    [Fact]
    public void ABiggerPanel_WidensBothPlotsToTheSameRightEdge()
    {
        using var panel = new VirtualCrossoverPanel();
        (PlotView main, PlotView dsp) = Plots(panel);
        int designedGap = main.Right - dsp.Right;
        int mainWidth = main.Width;
        int dspWidth = dsp.Width;
        Size design = panel.Size;

        panel.Size = new Size(design.Width + 500, design.Height + 300);

        Assert.Equal(mainWidth + 500, main.Width);
        Assert.Equal(dspWidth + 500, dsp.Width);
        // The DSP plot starts further right, so the invariant is the right edge, not the width.
        Assert.Equal(designedGap, main.Right - dsp.Right);
    }

    [Fact]
    public void TheRowsBetweenThePlots_RideDownWithTheAcousticPlot()
    {
        using var panel = new VirtualCrossoverPanel();
        (PlotView main, PlotView dsp) = Plots(panel);
        Control curves = Field<Control>(panel, "labelCurves");
        Control view = Field<Control>(panel, "panel1");
        Control autoDelay = Field<Control>(panel, "buttonAutoDelay");
        int curvesGap = curves.Top - main.Bottom;
        int viewGap = view.Top - main.Bottom;
        int autoDelayGap = autoDelay.Top - main.Bottom;
        int dspGap = dsp.Top - main.Bottom;
        Size design = panel.Size;

        panel.Size = new Size(design.Width + 500, design.Height + 300);

        Assert.Equal(curvesGap, curves.Top - main.Bottom);
        Assert.Equal(viewGap, view.Top - main.Bottom);
        Assert.Equal(autoDelayGap, autoDelay.Top - main.Bottom);
        Assert.Equal(dspGap, dsp.Top - main.Bottom);
        Assert.True(main.Bottom < curves.Top);
    }

    [Fact]
    public void TheBottomRow_StaysAtTheBottomBelowTheDspPlot()
    {
        using var panel = new VirtualCrossoverPanel();
        (_, PlotView dsp) = Plots(panel);
        Control dspMode = Field<Control>(panel, "dspModePanel");
        Control export = Field<Control>(panel, "buttonExport");
        int modeGap = dspMode.Top - dsp.Bottom;
        int bottomGap = panel.ClientSize.Height - export.Bottom;
        Size design = panel.Size;

        panel.Size = new Size(design.Width + 500, design.Height + 300);

        Assert.Equal(modeGap, dspMode.Top - dsp.Bottom);
        Assert.Equal(bottomGap, panel.ClientSize.Height - export.Bottom);
    }

    [Fact]
    public void ComingBackFromABiggerWindow_RestoresTheDesignedArrangement()
    {
        using var panel = new VirtualCrossoverPanel();
        (PlotView main, PlotView dsp) = Plots(panel);
        Control curves = Field<Control>(panel, "labelCurves");
        Rectangle mainDesign = main.Bounds;
        Rectangle dspDesign = dsp.Bounds;
        Rectangle curvesDesign = curves.Bounds;
        Rectangle scrollableDesign = panel.DisplayRectangle;
        Size design = panel.Size;

        panel.Size = new Size(design.Width + 500, design.Height + 300);
        panel.Size = design;

        Assert.Equal(mainDesign, main.Bounds);
        Assert.Equal(dspDesign, dsp.Bounds);
        Assert.Equal(curvesDesign, curves.Bounds);
        // Sized while the plots were big, the scrollable area left the panel stuck scrolled sideways.
        Assert.Equal(scrollableDesign, panel.DisplayRectangle);
        Assert.Equal(Point.Empty, panel.AutoScrollPosition);
    }

    [Fact]
    public void AScaledPanel_StretchesFromItsScaledSize()
    {
        using var panel = new VirtualCrossoverPanel();
        (PlotView main, PlotView dsp) = Plots(panel);

        // At 150% the stretch baseline must scale with the controls, or scaled size reads as user enlargement.
        panel.Scale(new SizeF(1.5f, 1.5f));
        int scaledMainHeight = main.Height;
        int scaledDspHeight = dsp.Height;
        int scaledMainWidth = main.Width;
        Size scaled = panel.Size;

        panel.PerformLayout();
        Assert.Equal(scaledMainHeight, main.Height);
        Assert.Equal(scaledDspHeight, dsp.Height);

        panel.Size = new Size(scaled.Width + 500, scaled.Height + 300);
        Assert.Equal(
            scaledMainHeight + scaledDspHeight + 300, main.Height + dsp.Height);
        Assert.Equal(scaledMainWidth + 500, main.Width);

        panel.Size = scaled;
        Assert.Equal(scaledMainHeight, main.Height);
        Assert.Equal(scaledMainWidth, main.Width);
        Assert.Equal(scaledDspHeight, dsp.Height);
    }


    [Fact]
    public void ItsPadding_ScalesWithTheArrangement()
    {
        using var panel = new VirtualCrossoverPanel();
        Padding designedPadding = panel.Padding;
        Point designedCorner = Field<Control>(panel, "channelListPanel").Location;

        panel.AutoScaleDimensions = new SizeF(48F, 48F);

        Assert.Equal(designedPadding.Left * 2, panel.Padding.Left);
        Assert.Equal(designedPadding.Top * 2, panel.Padding.Top);

        // base.ScaleControl scales Padding on every pass, so a bounds-only cascade would count it twice.
        ScaleBoundsOnly(panel, 2F);

        Assert.Equal(designedPadding.Left * 2, panel.Padding.Left);
        Assert.Equal(designedPadding.Top * 2, panel.Padding.Top);
        Assert.Equal(
            new Point(designedPadding.Left * 2, designedPadding.Top * 2),
            panel.DisplayRectangle.Location);
        Assert.Equal(
            new Point(designedCorner.X * 2, designedCorner.Y * 2),
            Field<Control>(panel, "channelListPanel").Location);
    }

    [Fact]
    public void TheShellsCascadeAfterItsOwnAutoScale_DoesNotScaleTheArrangementTwice()
    {
        using var panel = new VirtualCrossoverPanel();

        // Declaring 76.8 DPI makes the container scale by 96/76.8 = 1.25, as a 125% display does.
        panel.AutoScaleDimensions = new SizeF(76.8F, 76.8F);
        Size scaledArrangement = Plots(panel).Main.Size;
        Size scaledPanel = panel.Size;

        // The shell's pass resizes the panel only; counting it in the baseline twice caused the 125% scrollbars/cut-off plot.
        ScaleBoundsOnly(panel, 1.25F);
        panel.Size = scaledPanel;

        Assert.Equal(scaledArrangement, Plots(panel).Main.Size);
        Assert.True(
            panel.DisplayRectangle.Width <= panel.ClientSize.Width,
            $"content {panel.DisplayRectangle.Width} wide in a {panel.ClientSize.Width} client");
        Assert.True(
            panel.DisplayRectangle.Height <= panel.ClientSize.Height,
            $"content {panel.DisplayRectangle.Height} tall in a {panel.ClientSize.Height} client");
    }

    private static void ScaleBoundsOnly(Control panel, float factor) =>
        typeof(Control)
            .GetMethod(
                "ScaleControl",
                BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(SizeF), typeof(BoundsSpecified)])!
            .Invoke(panel, [new SizeF(factor, factor), BoundsSpecified.All]);


    private static (PlotView Main, PlotView Dsp) Plots(VirtualCrossoverPanel panel) =>
        (Field<PlotView>(panel, "mainPlotView"), Field<PlotView>(panel, "dspPlotView"));

    private static T Field<T>(VirtualCrossoverPanel panel, string name) =>
        (T)typeof(VirtualCrossoverPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
}
