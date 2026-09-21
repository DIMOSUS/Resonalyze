using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class AgentJunctionTuneWriteTests
{
    private static readonly CrossoverEdge Kept = new(CrossoverFilterFamily.LinkwitzRiley, 300, 24);
    private static readonly CrossoverEdge Found = new(CrossoverFilterFamily.LinkwitzRiley, 350, 24);
    private static readonly JunctionAcousticTarget Asked = new(CrossoverFilterFamily.LinkwitzRiley, 12);

    [Fact]
    public void ApplyingOnlyTheGoal_LeavesTheEdgesWhereTheyWere()
    {
        (VirtualCrossoverChannel lower, VirtualCrossoverChannel upper) = Junction();

        AgentJunctionTune.Write(Result(), lower, upper, Asked, applyCrossover: false);

        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(Kept, lower.SideSettings(right).LowPassEdge);
            Assert.Equal(Kept, upper.SideSettings(right).HighPassEdge);
            Assert.Equal(Asked, lower.SideSettings(right).AcousticLowPass);
            Assert.Equal(Asked, upper.SideSettings(right).AcousticHighPass);
        }

        AgentJunctionTune.Write(Result(), lower, upper, null);

        Assert.Equal(Found, lower.Settings.LowPassEdge);
        Assert.Equal(Found, upper.Settings.HighPassEdge);
    }

    [Fact]
    public void AnAskedGoal_ReplacesTheOneTheCardsHeld()
    {
        (VirtualCrossoverChannel lower, VirtualCrossoverChannel upper) = Junction();
        var held = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18);
        lower.Settings.AcousticLowPass = held;
        upper.Settings.AcousticHighPass = held;

        AgentJunctionTune.Write(Result(), lower, upper, Asked);

        Assert.Equal(Asked, lower.Settings.AcousticLowPass);
        Assert.Equal(Asked, upper.Settings.AcousticHighPass);
    }

    [Fact]
    public void APlainTune_LeavesTheGoalTheCardsHeld()
    {
        (VirtualCrossoverChannel lower, VirtualCrossoverChannel upper) = Junction();
        var held = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18);
        lower.Settings.AcousticLowPass = held;
        upper.Settings.AcousticHighPass = held;

        AgentJunctionTune.Write(Result(), lower, upper, acoustic: null);

        Assert.Equal(held, lower.Settings.AcousticLowPass);
        Assert.Equal(held, upper.Settings.AcousticHighPass);
    }

    private static JunctionTuneResult Result() =>
        new(
            new JunctionTuneCandidate(Kept, Kept, [], [], 150, 600),
            new JunctionTuneCandidate(Found, Found, [], [], 175, 700),
            Changed: true, [], [], [], 1, 150, 700, []);

    private static (VirtualCrossoverChannel Lower, VirtualCrossoverChannel Upper) Junction()
    {
        var lower = new VirtualCrossoverChannel("A");
        var upper = new VirtualCrossoverChannel("B");
        foreach (bool right in new[] { false, true })
        {
            lower.SideSettings(right).CrossoverKind = CrossoverKind.LowPass;
            lower.SideSettings(right).LowPassEdge = Kept;
            upper.SideSettings(right).CrossoverKind = CrossoverKind.HighPass;
            upper.SideSettings(right).HighPassEdge = Kept;
        }

        return (lower, upper);
    }
}
