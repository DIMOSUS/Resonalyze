using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

/// <summary>
/// What a bigger window buys the EQ wizard. The curve being equalized is what
/// the tool is read on, so every pixel the window gains goes to the plot — out to
/// the panel's right edge and down to the PEQ bank. The bank and the auto-tune
/// box keep their size and ride down to the bottom-left corner: the bank is a
/// fixed grid of strips on percent styles, so room given to it would only enlarge
/// the strips.
/// </summary>
public sealed class EqWizardPanelLayoutTests
{
    [Fact]
    public void TheDesignedSize_IsLeftExactlyAsTheDesignerDrewIt()
    {
        using var panel = new EqWizardPanel();
        Rectangle plotDesign = Plot(panel).Bounds;
        Rectangle bankDesign = Field<Control>(panel, "panelPEQ").Bounds;
        Rectangle autoTuneDesign = Field<Control>(panel, "panelAutoTune").Bounds;

        // Nothing below the designed size shrinks: the panel scrolls, the way it
        // did before it could stretch at all.
        panel.Size = new Size(panel.Width - 200, panel.Height - 200);

        Assert.Equal(plotDesign, Plot(panel).Bounds);
        Assert.Equal(bankDesign, Field<Control>(panel, "panelPEQ").Bounds);
        Assert.Equal(autoTuneDesign, Field<Control>(panel, "panelAutoTune").Bounds);
    }

    [Fact]
    public void ABiggerPanel_SpendsEveryAddedPixelOnThePlot()
    {
        using var panel = new EqWizardPanel();
        PlotView plot = Plot(panel);
        Size plotDesign = plot.Size;
        Size design = panel.Size;

        panel.Size = new Size(design.Width + 500, design.Height + 300);

        Assert.Equal(plotDesign.Width + 500, plot.Width);
        Assert.Equal(plotDesign.Height + 300, plot.Height);
    }

    [Fact]
    public void ABiggerPanel_KeepsTheBankAndTheAutoTuneBoxAtTheBottomLeft()
    {
        using var panel = new EqWizardPanel();
        PlotView plot = Plot(panel);
        Control bank = Field<Control>(panel, "panelPEQ");
        Control autoTune = Field<Control>(panel, "panelAutoTune");
        Rectangle bankDesign = bank.Bounds;
        Rectangle autoTuneDesign = autoTune.Bounds;
        int bankGap = bank.Top - plot.Bottom;
        int bottomGap = panel.ClientSize.Height - bank.Bottom;
        Size design = panel.Size;

        panel.Size = new Size(design.Width + 500, design.Height + 300);

        // Same size, same left edge, moved down by exactly what the plot grew —
        // which is what "anchored to the bottom-left corner" means here.
        Assert.Equal(bankDesign.Size, bank.Size);
        Assert.Equal(autoTuneDesign.Size, autoTune.Size);
        Assert.Equal(bankDesign.Left, bank.Left);
        Assert.Equal(autoTuneDesign.Left, autoTune.Left);
        Assert.Equal(bankDesign.Top + 300, bank.Top);
        Assert.Equal(autoTuneDesign.Top + 300, autoTune.Top);
        // The gap under the plot is what stops the growing plot from running over
        // the bank, and the gap under the bank is what keeps the pair on-screen.
        Assert.Equal(bankGap, bank.Top - plot.Bottom);
        Assert.Equal(bottomGap, panel.ClientSize.Height - bank.Bottom);
        Assert.True(plot.Bottom < bank.Top);
    }

    [Fact]
    public void ComingBackFromABiggerWindow_RestoresTheDesignedArrangement()
    {
        using var panel = new EqWizardPanel();
        PlotView plot = Plot(panel);
        Control bank = Field<Control>(panel, "panelPEQ");
        Rectangle plotDesign = plot.Bounds;
        Rectangle bankDesign = bank.Bounds;
        Rectangle scrollableDesign = panel.DisplayRectangle;
        Size design = panel.Size;

        panel.Size = new Size(design.Width + 500, design.Height + 300);
        panel.Size = design;

        Assert.Equal(plotDesign, plot.Bounds);
        Assert.Equal(bankDesign, bank.Bounds);
        // The controls being right is only half of it: the scrollable area has to
        // shrink back with them, or the panel comes back scrolled with its bars
        // stuck on.
        Assert.Equal(scrollableDesign, panel.DisplayRectangle);
        Assert.Equal(Point.Empty, panel.AutoScrollPosition);
    }

    [Fact]
    public void AScaledPanel_StretchesFromItsScaledSize()
    {
        using var panel = new EqWizardPanel();
        PlotView plot = Plot(panel);
        Control bank = Field<Control>(panel, "panelPEQ");

        // What a 150% display does: the container scales every control, so the
        // arrangement the stretch measures against has to scale with them.
        // Measured against the designer's 96-DPI numbers instead, the panel would
        // read its own scaled size as "the user enlarged the window" and blow the
        // plot up by the scale factor on top of it.
        panel.Scale(new SizeF(1.5f, 1.5f));
        Size scaledPlot = plot.Size;
        int scaledBankTop = bank.Top;
        Size scaled = panel.Size;

        panel.PerformLayout();
        Assert.Equal(scaledPlot, plot.Size);
        Assert.Equal(scaledBankTop, bank.Top);

        panel.Size = new Size(scaled.Width + 500, scaled.Height + 300);
        Assert.Equal(scaledPlot.Width + 500, plot.Width);
        Assert.Equal(scaledPlot.Height + 300, plot.Height);
        Assert.Equal(scaledBankTop + 300, bank.Top);

        // And back: the scaled arrangement is what it must return to, not the
        // designer's 96-DPI one.
        panel.Size = scaled;
        Assert.Equal(scaledPlot, plot.Size);
        Assert.Equal(scaledBankTop, bank.Top);
    }


    /// <summary>
    /// The panel's padding is part of the arrangement, and the anchored controls
    /// are placed against it: the channel column and the buttons under it sit at
    /// the padding's own corner. A padding left at the designer's 96-DPI number
    /// while everything around it scaled put every one of them 6 px off at 192 DPI
    /// (#120) — which is what the shell re-assigning `Padding = new Padding(6)`
    /// after this panel had scaled its own did.
    /// </summary>
    [Fact]
    public void ItsPadding_ScalesWithTheArrangement()
    {
        using var panel = new EqWizardPanel();
        Padding designedPadding = panel.Padding;
        Point designedCorner = Field<Control>(panel, "buttonSource").Location;

        // Factor 2, the arithmetic of a 192 DPI display.
        panel.AutoScaleDimensions = new SizeF(48F, 48F);

        Assert.Equal(designedPadding.Left * 2, panel.Padding.Left);
        Assert.Equal(designedPadding.Top * 2, panel.Padding.Top);
        Assert.Equal(
            new Point(designedPadding.Left * 2, designedPadding.Top * 2),
            panel.DisplayRectangle.Location);
        Assert.Equal(
            new Point(designedCorner.X * 2, designedCorner.Y * 2),
            Field<Control>(panel, "buttonSource").Location);
    }

    [Fact]
    public void TheShellsCascadeAfterItsOwnAutoScale_DoesNotScaleTheArrangementTwice()
    {
        using var panel = new EqWizardPanel();

        // A real auto-scale pass, at whatever DPI the machine runs: declaring a
        // lower source DPI makes the container scale itself by 96/76.8 = 1.25, the
        // arithmetic a 125% display puts it through. The arrangement inside moves
        // with it, and so must the baseline the stretch measures against.
        panel.AutoScaleDimensions = new SizeF(76.8F, 76.8F);
        Size scaledArrangement = Plot(panel).Size;
        Size scaledPanel = panel.Size;

        // Then the shell's own scale reaches the panel, and that pass resizes the
        // PANEL ONLY — measured at a real 125%, a form scaling its children leaves
        // the arrangement inside an auto-scaling container where it was. So the
        // baseline must sit this one out: counted twice it ran a whole factor ahead
        // and the stretch sized the plot for a panel a quarter wider than the one
        // it is in, which is the field report — both scrollbars at 125%, the plot
        // cut off at the right.
        ScaleBoundsOnly(panel, 1.25F);
        panel.Size = scaledPanel;

        Assert.Equal(scaledArrangement, Plot(panel).Size);
        Assert.True(
            panel.DisplayRectangle.Width <= panel.ClientSize.Width,
            $"content {panel.DisplayRectangle.Width} wide in a {panel.ClientSize.Width} client");
        Assert.True(
            panel.DisplayRectangle.Height <= panel.ClientSize.Height,
            $"content {panel.DisplayRectangle.Height} tall in a {panel.ClientSize.Height} client");
    }

    // The pass a parent makes over this panel: Control.ScaleControl, the protected
    // entry point WinForms itself calls, and the one the panel overrides.
    private static void ScaleBoundsOnly(Control panel, float factor) =>
        typeof(Control)
            .GetMethod(
                "ScaleControl",
                BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(SizeF), typeof(BoundsSpecified)])!
            .Invoke(panel, [new SizeF(factor, factor), BoundsSpecified.All]);


    private static PlotView Plot(EqWizardPanel panel) => Field<PlotView>(panel, "plotWizard");

    // The panel's controls are private designer fields; the layout they end up
    // with is the whole subject here, so the test reads them by name.
    private static T Field<T>(EqWizardPanel panel, string name) =>
        (T)typeof(EqWizardPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
}
