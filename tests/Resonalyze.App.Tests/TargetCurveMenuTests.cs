using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// The menu behind the Target button in the EQ Wizard and in Virtual DSP. It is
/// the only place the app states which of the two shapes a target has, so what it
/// ticks — and that it names the file when there is one — is the whole point of it.
/// </summary>
public sealed class TargetCurveMenuTests
{
    [Fact]
    public void WithoutAnImportedCurveTheParametricShapeIsTheOneInForce()
    {
        using ContextMenuStrip menu = TargetCurveMenu.Build(null, () => { }, () => { });

        Assert.Equal(2, menu.Items.Count);
        Assert.True(Item(menu, 0).Checked);
        Assert.False(Item(menu, 1).Checked);
        Assert.Equal("Import from file…", Item(menu, 1).Text);
    }

    [Fact]
    public void AnImportedCurveIsTickedAndNamed()
    {
        ImportedTargetCurve imported = ImportedTargetCurve.FromPoints(
            "my-house-curve.txt",
            [new OverlayPoint(100, 6), new OverlayPoint(1_000, 0)])!;

        using ContextMenuStrip menu = TargetCurveMenu.Build(imported, () => { }, () => { });

        Assert.False(Item(menu, 0).Checked);
        Assert.True(Item(menu, 1).Checked);
        Assert.Contains("my-house-curve.txt", Item(menu, 1).Text);
        // The tooltip is what says how much of a curve it is: a file that read as
        // two points and one that read as four hundred look alike on the button.
        Assert.Contains("2 points", Item(menu, 1).ToolTipText);
    }

    [Fact]
    public void EachEntryCallsItsOwnAction()
    {
        int parametric = 0;
        int import = 0;
        using ContextMenuStrip menu = TargetCurveMenu.Build(
            null,
            () => parametric++,
            () => import++);

        Item(menu, 0).PerformClick();
        Item(menu, 1).PerformClick();

        Assert.Equal(1, parametric);
        Assert.Equal(1, import);
    }

    private static ToolStripMenuItem Item(ContextMenuStrip menu, int index) =>
        (ToolStripMenuItem)menu.Items[index];
}
