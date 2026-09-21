using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// What the junction tune says about a stated acoustic slope — the same lines in the dialog's report and in the AI
/// import's summary, because both come through <c>AgentJunctionTune.Describe</c>.
/// </summary>
public sealed class AgentJunctionTuneAcousticReportTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void AReachedGoal_IsReportedWithTheSlopesAndSaysItTravelsToTheEqStage()
    {
        // A perfect impulse is a flat driver, so an LR24 pair IS acoustic LR24 and the lattice reaches it.
        var report = new List<string>();
        (JunctionTunePlan plan, JunctionTuneResult result) = Tune(
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24));

        AgentJunctionTune.Describe(report, plan, result);

        string text = string.Join(Environment.NewLine, report);
        Assert.Contains("acoustic LR24 asked", text);
        Assert.Contains("reached.", text);
        Assert.Contains("slopes over the handover, all fitted the same way", text);
        Assert.Contains("the goal is written onto these edges", text);
        // The claim stays where the evidence is: a magnitude fit, not a transfer function.
        Assert.Contains("magnitude fit", text);
    }

    [Fact]
    public void AGoalTheDriversCannotReach_SaysSo_AndThatAutoTuneWillAimAtItAnyway()
    {
        var report = new List<string>();
        (JunctionTunePlan plan, JunctionTuneResult result) = OutOfReach();

        AgentJunctionTune.Describe(report, plan, result);

        string text = string.Join(Environment.NewLine, report);
        Assert.Contains("OUT OF REACH.", text);
        Assert.Contains("the goal is written onto these edges, but the crossover misses it", text);
    }

    [Fact]
    public void AGoalSomeFilterCouldReach_ButTheCrossoverKeptDoesNot_IsCalledMissed()
    {
        // "Reachable" is a statement about the lattice; whether the fit aims at a slope the filter makes is a
        // statement about the crossover that stays. The goal is written either way, and the summary must not
        // call a kept crossover that misses it a landing.
        CrossoverEdge edge = new(CrossoverFilterFamily.LinkwitzRiley, 1_000, 48);
        var far = new JunctionTuneReading(
            "left", -0.5, -1.0, 1.0, new JunctionAcousticFit(6.0, -6.0, 40, 40, 24));
        var kept = new JunctionTuneCandidate(edge, edge, [far], [far], 500, 2_000);
        var result = new JunctionTuneResult(
            kept, kept, Changed: false, [], [], [], 10, 500, 2_000, [], ClosestAcousticCostDb: 1.0);
        var asked = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24);
        var plan = new JunctionTunePlan(
            "Junction tune A/B",
            new VirtualCrossoverChannel("A"),
            new VirtualCrossoverChannel("B"),
            [],
            new JunctionTuneOptions(
                [CrossoverFilterFamily.LinkwitzRiley], null, 950, 1_050, false, SampleRate,
                AcousticTarget: asked));
        var report = new List<string>();

        AgentJunctionTune.Describe(report, plan, result);

        string text = string.Join(Environment.NewLine, report);
        Assert.Contains("reached.", text);
        Assert.Contains("the crossover misses it by 6.0 dB", text);
    }
    [Fact]
    public void ApplyingOnlyTheGoal_LeavesTheEdgesWhereTheReportSaidTheyWouldStay()
    {
        // The dialog offers Apply on a kept crossover so a goal can be stated without retuning. Writing the best
        // candidate's edges there would move the crossover the report had just called unchanged.
        (JunctionTunePlan plan, JunctionTuneResult result) = Tune(
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24));
        var lower = new VirtualCrossoverChannel("A");
        var upper = new VirtualCrossoverChannel("B");
        foreach (bool right in new[] { false, true })
        {
            lower.SideSettings(right).CrossoverKind = CrossoverKind.LowPass;
            lower.SideSettings(right).LowPassEdge = Edge(300);
            upper.SideSettings(right).CrossoverKind = CrossoverKind.HighPass;
            upper.SideSettings(right).HighPassEdge = Edge(300);
        }

        AgentJunctionTune.Write(
            result, lower, upper, new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24),
            applyCrossover: false);

        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(Edge(300), lower.SideSettings(right).LowPassEdge);
            Assert.Equal(Edge(300), upper.SideSettings(right).HighPassEdge);
            // Both sides carry the goal, since one electrical filter serves both.
            Assert.Equal(24, lower.SideSettings(right).AcousticLowPass!.SlopeDbPerOctave);
            Assert.Equal(24, upper.SideSettings(right).AcousticHighPass!.SlopeDbPerOctave);
        }

        // And applying the crossover does move the edges.
        AgentJunctionTune.Write(result, lower, upper, null);

        Assert.Equal(result.Best.LowerLowPass, lower.Settings.LowPassEdge);
        Assert.Equal(result.Best.UpperHighPass, upper.Settings.HighPassEdge);
        _ = plan;
    }

    [Fact]
    public void AGoalAskedForAndMissed_IsStillWrittenOverTheWishTheCardsHeld()
    {
        // The goal is the user's statement, shown on the card: a tune asked for LR12 writes LR12, whatever the
        // cards said before and however far the crossover lands from it. The report says how far.
        (_, JunctionTuneResult unreachable) = OutOfReach();
        var lower = new VirtualCrossoverChannel("A");
        var upper = new VirtualCrossoverChannel("B");
        var held = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18);
        foreach (bool right in new[] { false, true })
        {
            lower.SideSettings(right).AcousticLowPass = held;
            upper.SideSettings(right).AcousticHighPass = held;
        }

        AgentJunctionTune.Write(
            unreachable, lower, upper,
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 12));

        var asked = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 12);
        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(asked, lower.SideSettings(right).AcousticLowPass);
            Assert.Equal(asked, upper.SideSettings(right).AcousticHighPass);
        }
    }

    [Fact]
    public void APlainTune_LeavesTheWishTheCardsHeldAlone()
    {
        // No goal asked for: the wish is the user's, stated on the card, and a tune that was not asked about it
        // has no opinion to write.
        (_, JunctionTuneResult result) = OutOfReach();
        var lower = new VirtualCrossoverChannel("A");
        var upper = new VirtualCrossoverChannel("B");
        var held = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18);
        lower.Settings.AcousticLowPass = held;
        upper.Settings.AcousticHighPass = held;

        AgentJunctionTune.Write(result, lower, upper, acoustic: null);

        Assert.Equal(held, lower.Settings.AcousticLowPass);
        Assert.Equal(held, upper.Settings.AcousticHighPass);
    }

    /// <summary>A junction whose drivers already fall at LR24: no 12 dB/oct filter leaves the sum as soft as LR12.</summary>
    private static (JunctionTunePlan Plan, JunctionTuneResult Result) OutOfReach()
    {
        CrossoverEdge own = new(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        Complex[] rolledOff = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), LowPass(own), SampleRate, SampleRate);
        Complex[] rolledOn = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), HighPass(own), SampleRate, SampleRate);
        var bare = new DspChannelChain(Crossover: CrossoverSpec.Off);
        return Tune(
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 12),
            new JunctionTuneSide("left", rolledOff, bare, rolledOn, bare, SampleRate),
            slopes: [12]);
    }

    private static CrossoverEdge Edge(double hz) =>
        new(CrossoverFilterFamily.LinkwitzRiley, hz, 24);

    [Fact]
    public void WithNoGoalStated_TheReportSaysNothingAboutOne()
    {
        var report = new List<string>();
        (JunctionTunePlan plan, JunctionTuneResult result) = Tune(acoustic: null);

        AgentJunctionTune.Describe(report, plan, result);

        Assert.DoesNotContain("acoustic", string.Join(Environment.NewLine, report));
    }

    private static (JunctionTunePlan Plan, JunctionTuneResult Result) Tune(
        JunctionAcousticTarget? acoustic,
        JunctionTuneSide? side = null,
        IReadOnlyList<int>? slopes = null)
    {
        CrossoverEdge lr = new(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneSide read = side ?? new JunctionTuneSide(
            "left", Impulse(), LowPass(lr), Impulse(), HighPass(lr), SampleRate);
        var options = new JunctionTuneOptions(
            [CrossoverFilterFamily.LinkwitzRiley],
            slopes,
            950,
            1_050,
            IndependentSlopes: false,
            SampleRate,
            AcousticTarget: acoustic);
        return (
            new JunctionTunePlan("Junction tune A/B", new VirtualCrossoverChannel("A"),
                new VirtualCrossoverChannel("B"), [read], options),
            CrossoverJunctionTuner.Tune([read], options));
    }

    private static Complex[] Impulse()
    {
        var impulse = new Complex[16_384];
        impulse[480] = 1;
        return impulse;
    }

    private static DspChannelChain LowPass(CrossoverEdge edge) =>
        new(Crossover: new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge));

    private static DspChannelChain HighPass(CrossoverEdge edge) =>
        new(Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge));
}
