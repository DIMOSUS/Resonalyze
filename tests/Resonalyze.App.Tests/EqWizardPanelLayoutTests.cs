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

    private static PlotView Plot(EqWizardPanel panel) => Field<PlotView>(panel, "plotWizard");

    // The panel's controls are private designer fields; the layout they end up
    // with is the whole subject here, so the test reads them by name.
    private static T Field<T>(EqWizardPanel panel, string name) =>
        (T)typeof(EqWizardPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
}
