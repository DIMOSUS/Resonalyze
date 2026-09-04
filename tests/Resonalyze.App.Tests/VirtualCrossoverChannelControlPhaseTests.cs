using System.Drawing;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The phase row of a Virtual DSP block: the field a processor with a channel phase
/// control gets, the readout that says what the angle actually builds, and the height
/// the block stands at with and without it.
/// </summary>
public sealed class VirtualCrossoverChannelControlPhaseTests
{
    [Fact]
    public void WithoutTheControl_TheRowIsHiddenRatherThanClipped()
    {
        // Clipping was the obvious way to do this and the wrong one: a clipped field
        // still takes focus on Tab, and still counts towards the height the fold is
        // measured against.
        using var control = new VirtualCrossoverChannelControl();

        Assert.False(control.PhaseControlShown);
        Assert.False(control.PhaseInput.Visible);
        Assert.False(control.PhaseLabel.Visible);
        Assert.False(control.PhaseInfoLabel.Visible);
        Assert.True(control.PeqMenuButton.Bottom <= control.ClientSize.Height);
    }

    [Fact]
    public void ShowingTheControl_AddsExactlyOneRow_AndTakingItAwayGivesItBack()
    {
        using var control = new VirtualCrossoverChannelControl();
        int without = control.Height;
        int rowPitch = control.PhaseInput.Top - control.PeqMenuButton.Top;

        control.PhaseControlShown = true;

        Assert.Equal(without + rowPitch, control.Height);
        Assert.True(control.PhaseInput.Visible);
        Assert.True(control.PhaseInput.Bottom <= control.ClientSize.Height);
        // The pin moves with the height, or the flow list stretches the block back.
        Assert.Equal(control.Height, control.MinimumSize.Height);
        Assert.Equal(control.Height, control.MaximumSize.Height);

        control.PhaseControlShown = false;

        Assert.Equal(without, control.Height);
        Assert.False(control.PhaseInput.Visible);
    }

    [Fact]
    public void FoldingWithThePhaseRowShown_LeavesTheSameBlockAsWithout()
    {
        // The folded block is measured from its last KEPT row plus the margin the
        // designer left, and the phase row is far below the fold either way — so the
        // two must fold to the same thing. They did not while the margin was measured
        // on demand: with a row parked below the pin it came out zero, and the block
        // drew its own border across the fold button.
        using var withRow = new VirtualCrossoverChannelControl { PhaseControlShown = true };
        using var without = new VirtualCrossoverChannelControl();

        withRow.Collapsed = true;
        without.Collapsed = true;

        Assert.Equal(without.Height, withRow.Height);
        Assert.False(withRow.PhaseInput.Visible);
        Assert.True(withRow.CollapseButton.Bottom < withRow.ClientSize.Height);

        withRow.Collapsed = false;

        Assert.True(withRow.PhaseInput.Visible);
        Assert.True(withRow.PhaseInput.Bottom <= withRow.ClientSize.Height);
    }

    [Fact]
    public void ScalingWithThePhaseRowShown_KeepsItInsideTheBlock()
    {
        using var control = new VirtualCrossoverChannelControl { PhaseControlShown = true };

        control.Scale(new SizeF(1.5f, 1.5f));

        Assert.True(control.PhaseInput.Bottom <= control.ClientSize.Height);
        Assert.Equal(control.Height, control.MaximumSize.Height);

        // And the row can still be taken away at the scaled size without clipping the
        // PEQ row that becomes the last one.
        control.PhaseControlShown = false;

        Assert.True(control.PeqMenuButton.Bottom <= control.ClientSize.Height);
        Assert.True(control.PhaseInput.Top > control.ClientSize.Height - 1);
    }

    [Fact]
    public void TheReadout_NamesTheCrossoverTheAngleIsStatedAt_AndFollowsTheZone()
    {
        using var control = new VirtualCrossoverChannelControl { PhaseControlShown = true };
        control.ProcessorSampleRateHz = 96_000;
        control.HighPassFrequencyInput.Value = 500;
        control.LowPassFrequencyInput.Value = 5_000;

        // A front block states its angle at its high-pass...
        control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Front;
        control.PhaseInput.Value = 180;

        Assert.Contains("500 Hz", control.PhaseInfoLabel.Text);
        Assert.Contains("AP2 500 Hz", control.PhaseInfoLabel.Text);

        // ...and a subwoofer block at its low-pass, without the angle changing.
        control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Sub;

        Assert.Equal(180m, control.PhaseInput.Value);
        Assert.Contains("5000 Hz", control.PhaseInfoLabel.Text);
        Assert.Contains("AP2 5000 Hz", control.PhaseInfoLabel.Text);
    }

    [Fact]
    public void TheReadout_SaysWhenTheDeviceCannotDeliverTheAngle()
    {
        // At a 5 kHz reference the first five settings all collapse onto the one
        // filter the ceiling allows, and it turns the phase 29.5° rather than the
        // 5.625° asked for. Saying "5.625" there would be the readout lying.
        using var control = new VirtualCrossoverChannelControl { PhaseControlShown = true };
        control.ProcessorSampleRateHz = 96_000;
        control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Front;
        control.HighPassFrequencyInput.Value = 5_000;

        control.PhaseInput.Value = (decimal)PhaseRotationControl.StepDegrees;

        // The number is formatted in the running culture (29.5 or 29,5), so the
        // assert reads the parts around the separator rather than pinning one.
        Assert.Contains("29", control.PhaseInfoLabel.Text);
        Assert.Contains("min", control.PhaseInfoLabel.Text);
        Assert.DoesNotContain("AP2", control.PhaseInfoLabel.Text);
        Assert.Equal(Resonalyze.Ui.UiPalette.WarningAmber, control.PhaseInfoLabel.ForeColor);

        // A reference low enough to leave the whole grid reachable reads plainly again.
        control.HighPassFrequencyInput.Value = 500;

        Assert.DoesNotContain("min", control.PhaseInfoLabel.Text);
        Assert.NotEqual(Resonalyze.Ui.UiPalette.WarningAmber, control.PhaseInfoLabel.ForeColor);
    }

    [Fact]
    public void AnAngleBetweenTwoPositions_SnapsToTheOneTheDeviceHas()
    {
        using var control = new VirtualCrossoverChannelControl { PhaseControlShown = true };

        control.PhaseInput.Value = 7m;

        Assert.Equal(5.625m, control.PhaseInput.Value);
        Assert.Equal(354.375m, control.PhaseInput.Maximum);
        Assert.Equal(0m, control.PhaseInput.Minimum);
    }

    [Fact]
    public void TheAngle_RaisesTheSettingsEvent_SoTheChainIsRecomputed()
    {
        using var control = new VirtualCrossoverChannelControl { PhaseControlShown = true };
        int raised = 0;
        control.SettingsChanged += (_, _) => raised++;

        control.PhaseInput.Value = 90m;

        Assert.Equal(1, raised);
    }

    [Fact]
    // Shown, so the list is built and realised on one STA thread — the phase row
    // moves the same size pin the fold does.
    public void TogglingTheRowInsideTheChannelList_NeverStacksOneBlockOverAnother() =>
        StaTest.Run(ToggleEveryBlockInTurn);

    private static void ToggleEveryBlockInTurn()
    {
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
            foreach (VirtualCrossoverChannelControl block in blocks)
            {
                block.PhaseControlShown = true;
            }

            AssertStacked(blocks);
            blocks[1].Collapsed = true;
            AssertStacked(blocks);
            foreach (VirtualCrossoverChannelControl block in blocks)
            {
                block.PhaseControlShown = false;
            }

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
}
