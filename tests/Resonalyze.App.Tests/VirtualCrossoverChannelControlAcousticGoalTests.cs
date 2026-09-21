using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

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

        control.SetAcousticGoal(
            highPass: new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18),
            lowPass: new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 48),
            highPassRuns: false,
            lowPassRuns: true);
        Assert.Equal("LR48", control.AcousticGoalButton.Text);
    }

    [Fact]
    public void TheButtonSitsInTheCrossoverRow_InsideTheCard()
    {
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
