using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// The channel block draws its own rounded outline, which puts that outline on the
/// last row and column of its CLIENT area — where the framework's old
/// <see cref="BorderStyle.FixedSingle"/> sat outside it, and clipped whatever
/// reached it. Nothing clips now, so the block has to keep its own content off its
/// edge; both ways it failed in the field are below.
/// </summary>
public sealed class VirtualCrossoverChannelControlBorderTests
{
    // The fold measured the block to the last kept row, which the border was then
    // drawn on: the fold button came out with the outline through its bottom edge
    // and the corners cut off. Folded, the block leaves the gap it leaves expanded.
    [Fact]
    public void Folding_LeavesTheSameGapUnderTheLastRowAsTheExpandedBlock()
    {
        using var control = new VirtualCrossoverChannelControl();
        int expanded = BottomGap(control);

        control.Collapsed = true;

        Assert.True(expanded > 0);
        Assert.Equal(expanded, BottomGap(control));
        Assert.True(control.CollapseButton.Bottom < control.ClientSize.Height);
    }

    // The PEQ summary is written at run time and is longer than its row: it used to
    // be cut mid-glyph by the non-client border, and with that gone it painted its
    // own background over the outline instead. It is bounded and ellipsised now —
    // the full text was already in its tooltip.
    [Fact]
    public void ThePeqSummary_StaysInsideTheBlockHoweverLongItGets()
    {
        using var control = new VirtualCrossoverChannelControl();

        control.PeqInfoLabel.Text =
            "A very long EQ profile name: 14 bands, preamp -3,5 dB";

        Assert.False(control.PeqInfoLabel.AutoSize);
        Assert.True(control.PeqInfoLabel.AutoEllipsis);
        Assert.True(control.PeqInfoLabel.Right < control.ClientSize.Width);
    }

    private static int BottomGap(VirtualCrossoverChannelControl control)
    {
        int content = 0;
        foreach (Control child in control.Controls)
        {
            if (child.Visible)
            {
                content = Math.Max(content, child.Bottom);
            }
        }

        return control.ClientSize.Height - content;
    }
}
