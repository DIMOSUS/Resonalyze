using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// What pressing Apply in Tune junction does to the panel: the settings of both blocks and what their cards show.
/// The dialog and the search are tested on their own; this is the write between them, which nothing else reached.
/// </summary>
public sealed class VirtualCrossoverJunctionTuneApplyTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly CrossoverEdge Before = new(CrossoverFilterFamily.Butterworth, 180, 36);
    private static readonly CrossoverEdge FoundLow = new(CrossoverFilterFamily.Butterworth, 175, 36);
    private static readonly CrossoverEdge FoundHigh = new(CrossoverFilterFamily.Butterworth, 185, 24);

    [Fact]
    public void ApplyWritesTheFoundCrossover_EvenWhereTheReportCalledItNotWorthTheChange() => StaTest.Run(() =>
    {
        // The report says the found crossover gains less than the keep margin, and recommends keeping the one on
        // screen. Apply is still the user's explicit choice: pressing it and seeing nothing change is a button that
        // does not work.
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

    private static void AssertFound(
        VirtualCrossoverPanel panel, VirtualCrossoverChannel lower, VirtualCrossoverChannel upper)
    {
        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(FoundLow, lower.SideSettings(right).LowPassEdge);
            Assert.Equal(FoundHigh, upper.SideSettings(right).HighPassEdge);
        }

        // And the cards say so: a write the card does not show is one the next edit on the card undoes.
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

    // Bound the way applying a project binds, with a low-pass on the lower block and a high-pass on the upper.
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
