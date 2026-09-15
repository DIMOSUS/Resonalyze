using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

/// <summary>Extra window space goes to the plot; the bank keeps its size (percent-styled strips) and rides to the bottom-left.</summary>
public sealed class EqWizardPanelLayoutTests
{
    [Fact]
    public void TheDesignedSize_IsLeftExactlyAsTheDesignerDrewIt()
    {
        using var panel = new EqWizardPanel();
        Rectangle plotDesign = Plot(panel).Bounds;
        Rectangle bankDesign = Field<Control>(panel, "panelPEQ").Bounds;
        Rectangle autoTuneDesign = Field<Control>(panel, "panelAutoTune").Bounds;

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

        Assert.Equal(bankDesign.Size, bank.Size);
        Assert.Equal(autoTuneDesign.Size, autoTune.Size);
        Assert.Equal(bankDesign.Left, bank.Left);
        Assert.Equal(autoTuneDesign.Left, autoTune.Left);
        Assert.Equal(bankDesign.Top + 300, bank.Top);
        Assert.Equal(autoTuneDesign.Top + 300, autoTune.Top);
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
        Assert.Equal(scrollableDesign, panel.DisplayRectangle);
        Assert.Equal(Point.Empty, panel.AutoScrollPosition);
    }

    [Fact]
    public void AScaledPanel_StretchesFromItsScaledSize()
    {
        using var panel = new EqWizardPanel();
        PlotView plot = Plot(panel);
        Control bank = Field<Control>(panel, "panelPEQ");

        // The baseline scales with the container, or a 150% display reads as a user enlargement.
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

        panel.Size = scaled;
        Assert.Equal(scaledPlot, plot.Size);
        Assert.Equal(scaledBankTop, bank.Top);
    }


    /// <summary>Anchored controls sit at the padding's corner, so padding must scale with the arrangement.</summary>
    [Fact]
    public void ItsPadding_ScalesWithTheArrangement()
    {
        using var panel = new EqWizardPanel();
        Padding designedPadding = panel.Padding;
        Point designedCorner = Field<Control>(panel, "buttonSource").Location;

        panel.AutoScaleDimensions = new SizeF(48F, 48F);

        Assert.Equal(designedPadding.Left * 2, panel.Padding.Left);
        Assert.Equal(designedPadding.Top * 2, panel.Padding.Top);

        // base.ScaleControl scales Padding on every pass: counted twice it reads 24 where 12 was drawn.
        ScaleBoundsOnly(panel, 2F);

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

        // A lower source DPI (76.8) makes the container scale by 1.25, as a 125% display does.
        panel.AutoScaleDimensions = new SizeF(76.8F, 76.8F);
        Size scaledArrangement = Plot(panel).Size;
        Size scaledPanel = panel.Size;

        // The shell's pass resizes only the panel, so the baseline sits it out (counted twice: scrollbars and a cut-off plot at 125%).
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

    private static void ScaleBoundsOnly(Control panel, float factor) =>
        typeof(Control)
            .GetMethod(
                "ScaleControl",
                BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(SizeF), typeof(BoundsSpecified)])!
            .Invoke(panel, [new SizeF(factor, factor), BoundsSpecified.All]);


    private static PlotView Plot(EqWizardPanel panel) => Field<PlotView>(panel, "plotWizard");

    private static T Field<T>(EqWizardPanel panel, string name) =>
        (T)typeof(EqWizardPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
}
