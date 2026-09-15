using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>The self-drawn outline sits inside the client area, so content must keep off the edge.</summary>
public sealed class VirtualCrossoverChannelControlBorderTests
{
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
