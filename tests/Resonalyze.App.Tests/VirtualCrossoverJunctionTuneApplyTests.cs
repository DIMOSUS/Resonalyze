using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverJunctionTuneApplyTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly CrossoverEdge Before = new(CrossoverFilterFamily.Butterworth, 180, 36);
    private static readonly CrossoverEdge FoundLow = new(CrossoverFilterFamily.Butterworth, 175, 36);
    private static readonly CrossoverEdge FoundHigh = new(CrossoverFilterFamily.Butterworth, 185, 24);

    [Fact]
    public void ApplyWritesTheFoundCrossover_EvenWhereTheReportCalledItNotWorthTheChange() => StaTest.Run(() =>
    {
        using VirtualCrossoverPanel panel = Loaded(out VirtualCrossoverChannel lower, out VirtualCrossoverChannel upper);

        Apply(panel, lower, upper, Result(changed: false), goal: null);

        AssertFound(panel, lower, upper);
    });

    [Fact]
    public void ApplyWritesAWinningCrossover_OntoBothSidesAndBothCards() => StaTest.Run(() =>
    {
        using VirtualCrossoverPanel panel = Loaded(out VirtualCrossoverChannel lower, out VirtualCrossoverChannel upper);

        Apply(panel, lower, upper, Result(changed: true), goal: null);

        AssertFound(panel, lower, upper);
    });

    [Fact]
    public void WithNothingBetterFound_ApplyLeavesTheCrossoverAlone() => StaTest.Run(() =>
    {
        using VirtualCrossoverPanel panel = Loaded(out VirtualCrossoverChannel lower, out VirtualCrossoverChannel upper);
        JunctionTuneCandidate same = Candidate(Before, Before);

        Apply(panel, lower, upper, Result(same, same, changed: false), goal: null);

        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(Before, lower.SideSettings(right).LowPassEdge);
            Assert.Equal(Before, upper.SideSettings(right).HighPassEdge);
        }
    });

    [Fact]
    public void TheAskedAcousticCrossover_LandsOnTheCards_EvenWhereTheFoundCrossoverMissesIt() => StaTest.Run(() =>
    {
        using VirtualCrossoverPanel panel = Loaded(out VirtualCrossoverChannel lower, out VirtualCrossoverChannel upper);
        var asked = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24);
        JunctionTuneReading[] missed =
            [new("left", -0.6, -1.8, 2.8, new JunctionAcousticFit(4.6, 4.6, 41.8, 50.7, 21.5))];
        var found = new JunctionTuneCandidate(FoundLow, FoundHigh, missed, missed, 90, 360);
        var result = new JunctionTuneResult(
            Candidate(Before, Before), found, Changed: true, [], [], [], 1, 90, 360, [], ClosestAcousticCostDb: 1.3);

        Apply(panel, lower, upper, result, asked);

        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(asked, lower.SideSettings(right).AcousticLowPass);
            Assert.Equal(asked, upper.SideSettings(right).AcousticHighPass);
        }

        Assert.Equal("BW24", Card(panel, lower).AcousticGoalButton.Text);
        Assert.Equal("BW24", Card(panel, upper).AcousticGoalButton.Text);
    });

    [Fact]
    public void UndoLastApply_PutsTheCrossoverAndTheGoalBack() => StaTest.Run(() =>
    {
        using VirtualCrossoverPanel panel = Loaded(out VirtualCrossoverChannel lower, out VirtualCrossoverChannel upper);
        var asked = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24);
        Apply(panel, lower, upper, Result(changed: true), asked);
        AssertFound(panel, lower, upper);

        typeof(VirtualCrossoverPanel).GetMethod("UndoJunctionTune", Hidden)!.Invoke(panel, null);

        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(Before, lower.SideSettings(right).LowPassEdge);
            Assert.Equal(Before, upper.SideSettings(right).HighPassEdge);
            Assert.Null(lower.SideSettings(right).AcousticLowPass);
            Assert.Null(upper.SideSettings(right).AcousticHighPass);
        }

        Assert.Equal(180m, Card(panel, lower).LowPassFrequencyInput.Value);
        Assert.Equal("—", Card(panel, lower).AcousticGoalButton.Text);
    });

    [Fact]
    public void AGoalGoesOnlyOntoAnEdgeTheCrossoverLeftOnScreenRuns() => StaTest.Run(() =>
    {
        using VirtualCrossoverPanel panel = Loaded(out VirtualCrossoverChannel lower, out VirtualCrossoverChannel upper);
        foreach (bool right in new[] { false, true })
        {
            lower.SideSettings(right).CrossoverKind = CrossoverKind.Off;
        }

        var kept = new JunctionTuneCandidate(null, Before, [], [], 90, 360);
        var asked = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24);

        Apply(panel, lower, upper, Result(kept, kept, changed: false), asked);

        foreach (bool right in new[] { false, true })
        {
            Assert.Null(lower.SideSettings(right).AcousticLowPass);
            Assert.Equal(asked, upper.SideSettings(right).AcousticHighPass);
        }
    });

    [Fact]
    public void AGoalForAnEdgeSwitchedOff_IsNoLongerShownAsStated() => StaTest.Run(() =>
    {
        using VirtualCrossoverPanel panel = Loaded(out VirtualCrossoverChannel lower, out _);
        var asked = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24);
        lower.Settings.AcousticLowPass = asked;
        typeof(VirtualCrossoverPanel).GetMethod("ApplySettingsToControl", Hidden)!.Invoke(panel, [lower]);
        Assert.Equal("BW24", Card(panel, lower).AcousticGoalButton.Text);

        Card(panel, lower).CrossoverKindComboBox.SelectedItem = CrossoverKind.Off;

        Assert.Equal(CrossoverKind.Off, lower.Settings.CrossoverKind);
        Assert.Equal("—", Card(panel, lower).AcousticGoalButton.Text);
        Assert.Equal(asked, lower.Settings.AcousticLowPass);
    });

    [Fact]
    public void AStatedGoal_IsPartOfTheSessionAnAssistantReadsAgainst() => StaTest.Run(() =>
    {
        using VirtualCrossoverPanel panel = Loaded(out VirtualCrossoverChannel lower, out _);
        string before = Fingerprint(panel);

        lower.SideSettings(false).AcousticLowPass = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24);

        Assert.NotEqual(before, Fingerprint(panel));
        lower.SideSettings(false).AcousticLowPass = null;
        Assert.Equal(before, Fingerprint(panel));
    });

    private static string Fingerprint(VirtualCrossoverPanel panel) =>
        (string)typeof(VirtualCrossoverPanel)
            .GetMethod("ComputeAgentFingerprint", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, null)!;

    private static void AssertFound(
        VirtualCrossoverPanel panel, VirtualCrossoverChannel lower, VirtualCrossoverChannel upper)
    {
        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(FoundLow, lower.SideSettings(right).LowPassEdge);
            Assert.Equal(FoundHigh, upper.SideSettings(right).HighPassEdge);
        }

        VirtualCrossoverChannelControl lowerCard = Card(panel, lower);
        Assert.Equal(175m, lowerCard.LowPassFrequencyInput.Value);
        Assert.Equal(36, lowerCard.LowPassSlopeComboBox.SelectedItem);
        VirtualCrossoverChannelControl upperCard = Card(panel, upper);
        Assert.Equal(185m, upperCard.HighPassFrequencyInput.Value);
        Assert.Equal(24, upperCard.HighPassSlopeComboBox.SelectedItem);
    }

    private static JunctionTuneResult Result(bool changed) =>
        Result(Candidate(Before, Before), Candidate(FoundLow, FoundHigh), changed);

    private static JunctionTuneResult Result(
        JunctionTuneCandidate current, JunctionTuneCandidate best, bool changed) =>
        new(current, best, changed, [], [], [], 1, 90, 360, []);

    private static JunctionTuneCandidate Candidate(CrossoverEdge lowPass, CrossoverEdge highPass) =>
        new(lowPass, highPass, [], [], 90, 360);

    private static void Apply(
        VirtualCrossoverPanel panel,
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        JunctionTuneResult landed,
        JunctionAcousticTarget? goal) =>
        typeof(VirtualCrossoverPanel)
            .GetMethod("ApplyJunctionTune", Hidden)!
            .Invoke(panel, [lower, upper, landed, goal]);

    private static VirtualCrossoverChannelControl Card(VirtualCrossoverPanel panel, VirtualCrossoverChannel channel) =>
        (VirtualCrossoverChannelControl)typeof(VirtualCrossoverPanel)
            .GetMethod("ControlFor", Hidden)!
            .Invoke(panel, [channel])!;

    private static VirtualCrossoverPanel Loaded(
        out VirtualCrossoverChannel lower, out VirtualCrossoverChannel upper)
    {
        var panel = new VirtualCrossoverPanel();
        List<VirtualCrossoverChannel> channels = panel.Session.Channels;
        for (int index = 0; index < channels.Count; index++)
        {
            channels[index].Pair = panel.Session.Project.Pairs[index];
        }

        lower = channels[0];
        upper = channels[1];
        foreach (bool right in new[] { false, true })
        {
            lower.SideSettings(right).CrossoverKind = CrossoverKind.LowPass;
            lower.SideSettings(right).LowPassEdge = Before;
            upper.SideSettings(right).CrossoverKind = CrossoverKind.HighPass;
            upper.SideSettings(right).HighPassEdge = Before;
        }

        return panel;
    }
}
