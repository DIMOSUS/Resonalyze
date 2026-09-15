using System.Drawing;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverChannelControlCollapseTests
{
    [Fact]
    public void Collapsing_KeepsTheHeaderRowsAndCutsTheChainRows()
    {
        using var control = new VirtualCrossoverChannelControl();

        control.Collapsed = true;

        Assert.True(control.InvertCheckBox.Bottom <= control.ClientSize.Height);
        Assert.True(control.ZoneComboBox.Bottom <= control.ClientSize.Height);
        Assert.True(control.CollapseButton.Bottom <= control.ClientSize.Height);
        Assert.True(control.MuteButton.Bottom <= control.ClientSize.Height);
        Assert.True(control.BypassCheckBox.Bottom <= control.ClientSize.Height);
        Assert.True(control.ShowProcessedCheckBox.Visible);
        Assert.True(control.BypassCheckBox.Visible);
        Assert.True(control.CrossoverKindComboBox.Bottom > control.ClientSize.Height);
        Assert.False(control.CrossoverKindComboBox.Visible);
        Assert.False(control.PeqMenuButton.Visible);
        Assert.True(control.InvertCheckBox.Visible);
    }

    [Fact]
    public void Expanding_RestoresTheDesignerHeightAndTheHiddenRows()
    {
        using var control = new VirtualCrossoverChannelControl();
        int expandedHeight = control.Height;

        control.Collapsed = true;
        int collapsedHeight = control.Height;
        control.Collapsed = false;

        Assert.True(collapsedHeight < expandedHeight);
        Assert.Equal(expandedHeight, control.Height);
        Assert.True(control.CrossoverKindComboBox.Visible);
        Assert.True(control.PeqMenuButton.Visible);
    }

    [Fact]
    public void Collapsing_MovesTheSizePinSoTheFlowListCannotStretchTheBlockBack()
    {
        // Pinned via MinimumSize == MaximumSize; setting only Height would be undone by the next layout.
        using var control = new VirtualCrossoverChannelControl();

        control.Collapsed = true;

        Assert.Equal(control.Height, control.MinimumSize.Height);
        Assert.Equal(control.Height, control.MaximumSize.Height);
        control.Height = 1000;
        Assert.Equal(control.MaximumSize.Height, control.Height);
    }

    [Fact]
    public void CollapseButton_TogglesTheStateAndItsLabel()
    {
        using var control = new VirtualCrossoverChannelControl();
        int raised = 0;
        control.CollapsedChanged += (_, _) => raised++;
        control.SettingsChanged += (_, _) => Assert.Fail(
            "Folding a block changes no DSP setting and must not force a recompute.");

        control.CollapseButton.PerformClick();

        Assert.True(control.Collapsed);
        Assert.Equal("+", control.CollapseButton.Text);
        Assert.Equal(1, raised);

        control.CollapseButton.PerformClick();

        Assert.False(control.Collapsed);
        Assert.Equal("−", control.CollapseButton.Text);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void ScalingWhileFolded_UnfoldsToAHeightThatStillHoldsEveryRow()
    {
        // The expanded height is parked outside scaled bounds, so a scale while folded could clip the chain on unfold.
        using var control = new VirtualCrossoverChannelControl();
        control.Collapsed = true;
        int foldedAt100 = control.Height;

        control.Scale(new SizeF(1.5f, 1.5f));

        Assert.True(control.Height > foldedAt100);
        Assert.Equal(control.Height, control.MaximumSize.Height);
        Assert.True(control.InvertCheckBox.Bottom <= control.ClientSize.Height);

        control.Collapsed = false;

        Assert.Equal(control.Height, control.MaximumSize.Height);
        Assert.True(control.PeqMenuButton.Bottom <= control.ClientSize.Height);
        Assert.True(control.LowPassFrequencyInput.Bottom <= control.ClientSize.Height);

        control.Collapsed = true;
        control.Scale(new SizeF(1 / 1.5f, 1 / 1.5f));
        control.Collapsed = false;

        Assert.True(control.PeqMenuButton.Bottom <= control.ClientSize.Height);
        Assert.Equal(control.Height, control.MaximumSize.Height);
    }

    [Fact]
    public void ScalingWhileUnfolded_LeavesTheFoldOnTheSameRow()
    {
        using var control = new VirtualCrossoverChannelControl();
        control.Scale(new SizeF(1.5f, 1.5f));

        control.Collapsed = true;

        Assert.True(control.InvertCheckBox.Bottom <= control.ClientSize.Height);
        Assert.False(control.CrossoverKindComboBox.Visible);
        Assert.True(control.CrossoverKindComboBox.Bottom > control.ClientSize.Height);
    }

    [Fact]
    public void FoldButton_IsReachedWithTheZoneRowRatherThanAfterTheChain()
    {
        using var control = new VirtualCrossoverChannelControl();

        Assert.True(control.CollapseButton.TabIndex > control.DelayInput.TabIndex);
        Assert.True(control.CollapseButton.TabIndex < control.ZoneComboBox.TabIndex);
        Assert.True(control.ZoneComboBox.TabIndex < control.MonoCheckBox.TabIndex);
        Assert.True(control.MonoCheckBox.TabIndex < control.InvertCheckBox.TabIndex);
        Assert.True(control.InvertCheckBox.TabIndex < control.CrossoverKindComboBox.TabIndex);
    }

    [Fact]
    public void FoldingInsideTheChannelList_NeverStacksOneBlockOverAnother() =>
        StaTest.Run(FoldEveryBlockInTurn);

    private static void FoldEveryBlockInTurn()
    {
        // Field bug: an unbounded intermediate size pin let the flow list draw the next block 74 px over the one above.
        using var form = new Form();
        using var list = new FlowLayoutPanel
        {
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Size = new Size(347, 684)
        };
        form.Controls.Add(list);

        var blocks = new List<VirtualCrossoverChannelControl>();
        for (int index = 0; index < 4; index++)
        {
            var block = new VirtualCrossoverChannelControl
            {
                Margin = new Padding(0, 0, 0, 6),
                ChannelName = ((char)('A' + index)).ToString()
            };
            blocks.Add(block);
            list.Controls.Add(block);
        }

        try
        {
            form.Show();

            AssertStacked(blocks);
            blocks[0].Collapsed = true;
            AssertStacked(blocks);
            blocks[1].Collapsed = true;
            AssertStacked(blocks);
            blocks[0].Collapsed = false;
            AssertStacked(blocks);
            blocks[3].Collapsed = true;
            AssertStacked(blocks);
        }
        finally
        {
            form.Close();
            foreach (VirtualCrossoverChannelControl block in blocks)
            {
                block.Dispose();
            }
        }
    }

    private static void AssertStacked(IReadOnlyList<VirtualCrossoverChannelControl> blocks)
    {
        for (int index = 1; index < blocks.Count; index++)
        {
            VirtualCrossoverChannelControl above = blocks[index - 1];
            VirtualCrossoverChannelControl below = blocks[index];
            Assert.True(
                below.Top >= above.Bottom,
                $"block {below.ChannelName} at {below.Bounds} overlaps " +
                $"{above.ChannelName} at {above.Bounds}");
        }
    }

    [Fact]
    public void SettingTheSameStateAgain_RaisesNothing()
    {
        using var control = new VirtualCrossoverChannelControl();
        int raised = 0;
        control.CollapsedChanged += (_, _) => raised++;

        control.Collapsed = false;

        Assert.Equal(0, raised);
    }
}
