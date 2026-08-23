using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

/// <summary>
/// What a bigger window buys the Virtual DSP panel. The tool is read on two
/// stacked plots, and every pixel the window gains has to reach them — a
/// maximized window used to leave both at their designed size with the rest of
/// the screen empty. The designed PROPORTION between them is what must survive
/// the stretch: the acoustic plot is what is being read, the DSP plot is its
/// companion, and growing one alone would break that relation.
/// </summary>
public sealed class VirtualCrossoverPanelLayoutTests
{
    [Fact]
    public void TheDesignedSize_IsLeftExactlyAsTheDesignerDrewIt()
    {
        using var panel = new VirtualCrossoverPanel();
        (PlotView main, PlotView dsp) = Plots(panel);
        Rectangle mainDesign = main.Bounds;
        Rectangle dspDesign = dsp.Bounds;

        // The panel opens at the size the form's minimum hands it, and nothing
        // below that shrinks: it scrolls, the way it did before it could stretch.
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

        // Both grew, and their heights still read as the same pair of plots.
        Assert.True(main.Height > 0 && dsp.Height > 0);
        Assert.Equal(designedRatio, main.Height / (double)dsp.Height, 2);
        // Every added pixel was spent on them: nothing is banked as dead space
        // (the rows between the plots keep their own designed gaps and simply
        // ride down).
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
        // The DSP plot starts further right (the button column sits beside it),
        // so "the same right edge" is the invariant, not the same width.
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

        // Whatever the acoustic plot's new bottom edge is, the controls under it
        // keep the distance the designer gave them — that is what stops the
        // Curves and View rows from ending up under the plot.
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

        // Anchored, not laid out here — this is the pin that says the two
        // mechanisms agree: the DSP plot grows exactly into the room its mode
        // row leaves, so the row is never overrun and never floats away.
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
        // The controls being right is only half of it: the scrollable area has
        // to shrink back with them. Sized while the plots were still the big
        // ones, it left the panel scrolled sideways with both bars stuck on and
        // the channel column pushed off the left edge.
        Assert.Equal(scrollableDesign, panel.DisplayRectangle);
        Assert.Equal(Point.Empty, panel.AutoScrollPosition);
    }

    [Fact]
    public void AScaledPanel_StretchesFromItsScaledSize()
    {
        using var panel = new VirtualCrossoverPanel();
        (PlotView main, PlotView dsp) = Plots(panel);

        // What a 150% display does: the container scales every control, so the
        // arrangement the stretch measures against has to scale with them.
        // Measured against the designer's 96-DPI numbers instead, the panel
        // would read its own scaled size as "the user enlarged the window" and
        // blow the plots up by the scale factor on top of it.
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

        // And back: the scaled arrangement is what it must return to, not the
        // designer's 96-DPI one.
        panel.Size = scaled;
        Assert.Equal(scaledMainHeight, main.Height);
        Assert.Equal(scaledMainWidth, main.Width);
        Assert.Equal(scaledDspHeight, dsp.Height);
    }

    private static (PlotView Main, PlotView Dsp) Plots(VirtualCrossoverPanel panel) =>
        (Field<PlotView>(panel, "mainPlotView"), Field<PlotView>(panel, "dspPlotView"));

    // The panel's controls are private designer fields; the layout they end up
    // with is the whole subject here, so the test reads them by name.
    private static T Field<T>(VirtualCrossoverPanel panel, string name) =>
        (T)typeof(VirtualCrossoverPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
}
