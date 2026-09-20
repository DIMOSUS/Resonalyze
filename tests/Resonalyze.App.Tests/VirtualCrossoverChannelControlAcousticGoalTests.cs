using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The acoustic goal is state the user can see: it reads out on the crossover row rather than living where only the
/// fit can find it. See docs/specs/acoustic-crossover-target.md.
/// </summary>
public sealed class VirtualCrossoverChannelControlAcousticGoalTests
{
    [Fact]
    public void TheButtonReadsOutWhatWasStated_AndSaysSoWhenNothingWas()
    {
        using var control = new VirtualCrossoverChannelControl();

        Assert.Equal("—", control.AcousticGoalButton.Text);

        control.SetAcousticGoal(
            highPass: new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24),
            lowPass: null);
        Assert.Equal("LR24", control.AcousticGoalButton.Text);

        // Both edges stated the same reads once; stated differently, both are shown.
        control.SetAcousticGoal(
            highPass: new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24),
            lowPass: new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24));
        Assert.Equal("LR24", control.AcousticGoalButton.Text);

        control.SetAcousticGoal(
            highPass: new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18),
            lowPass: new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 48));
        Assert.Equal("BW18/LR48", control.AcousticGoalButton.Text);

        control.SetAcousticGoal(null, null);
        Assert.Equal("—", control.AcousticGoalButton.Text);
    }

    [Fact]
    public void TheButtonSitsInTheCrossoverRow_InsideTheCard()
    {
        // In the row it belongs to: the wish is about the crossover, and it folds away with it.
        using var control = new VirtualCrossoverChannelControl();

        Assert.Equal(control.CrossoverKindComboBox.Top, control.AcousticGoalButton.Top);
        Assert.True(control.AcousticGoalButton.Right <= control.ClientSize.Width);
        Assert.True(
            control.AcousticGoalButton.Left >= control.CrossoverKindComboBox.Right,
            "the read-out must not sit on top of the crossover kind.");

        control.Collapsed = true;
        Assert.False(control.AcousticGoalButton.Visible);
        control.Collapsed = false;
        Assert.True(control.AcousticGoalButton.Visible);
    }
}
