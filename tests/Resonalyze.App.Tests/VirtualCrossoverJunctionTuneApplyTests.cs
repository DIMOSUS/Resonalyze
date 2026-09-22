using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverJunctionTuneApplyTests : IDisposable
{
    private static readonly CrossoverEdge Before = new(CrossoverFilterFamily.Butterworth, 180, 36);
    private static readonly CrossoverEdge FoundLow = new(CrossoverFilterFamily.Butterworth, 175, 36);
    private static readonly CrossoverEdge FoundHigh = new(CrossoverFilterFamily.Butterworth, 185, 24);
    private static readonly AgentViewInputs View = new(VirtualCrossoverGroupView.FrontAndSub, false, false, 0, null);

    private readonly VirtualCrossoverSession session = new();
    private readonly VirtualCrossoverProcessingCoordinator coordinator = new();
    private readonly AgentSessionReader reader;
    private readonly VirtualCrossoverJunctionTuneApply tune;

    public VirtualCrossoverJunctionTuneApplyTests()
    {
        foreach (string name in new[] { "A", "B", "C" })
        {
            session.Channels.Add(new VirtualCrossoverChannel(name));
        }

        foreach (bool right in new[] { false, true })
        {
            Lower.SideSettings(right).CrossoverKind = CrossoverKind.LowPass;
            Lower.SideSettings(right).LowPassEdge = Before;
            Upper.SideSettings(right).CrossoverKind = CrossoverKind.HighPass;
            Upper.SideSettings(right).HighPassEdge = Before;
        }

        reader = new AgentSessionReader(
            session,
            coordinator,
            VirtualCrossoverMetrics.Through(
                coordinator, () => session.MagnitudeGate, oppositeSide: false, channel => session.Calibration.For(channel)),
            new VirtualCrossoverHybrid(session));
        tune = new VirtualCrossoverJunctionTuneApply(session, reader);
    }

    private VirtualCrossoverChannel Lower => session.Channels[0];

    private VirtualCrossoverChannel Upper => session.Channels[1];

    public void Dispose() => coordinator.Dispose();

    [Fact]
    public void ApplyWritesTheFoundCrossover_EvenWhereTheReportCalledItNotWorthTheChange()
    {
        Apply(Result(changed: false), goal: null);

        AssertFound();
    }

    [Fact]
    public void ApplyWritesAWinningCrossover_OntoBothSides()
    {
        Apply(Result(changed: true), goal: null);

        AssertFound();
    }

    [Fact]
    public void WithNothingBetterFound_ApplyLeavesTheCrossoverAlone()
    {
        JunctionTuneCandidate same = Candidate(Before, Before);

        Apply(Result(same, same, changed: false), goal: null);

        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(Before, Lower.SideSettings(right).LowPassEdge);
            Assert.Equal(Before, Upper.SideSettings(right).HighPassEdge);
        }
    }

    [Fact]
    public void TheAskedAcousticCrossover_IsWritten_EvenWhereTheFoundCrossoverMissesIt()
    {
        var asked = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24);
        JunctionTuneReading[] missed =
            [new("left", -0.6, -1.8, 2.8, new JunctionAcousticFit(4.6, 4.6, 41.8, 50.7, 21.5))];
        var found = new JunctionTuneCandidate(FoundLow, FoundHigh, missed, missed, 90, 360);
        var result = new JunctionTuneResult(
            Candidate(Before, Before), found, Changed: true, [], [], [], 1, 90, 360, [], ClosestAcousticCostDb: 1.3);

        Apply(result, asked);

        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(asked, Lower.SideSettings(right).AcousticLowPass);
            Assert.Equal(asked, Upper.SideSettings(right).AcousticHighPass);
        }
    }

    [Fact]
    public void AGoalGoesOnlyOntoAnEdgeTheCrossoverLeftOnScreenRuns()
    {
        foreach (bool right in new[] { false, true })
        {
            Lower.SideSettings(right).CrossoverKind = CrossoverKind.Off;
        }

        var kept = new JunctionTuneCandidate(null, Before, [], [], 90, 360);
        var asked = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24);

        Apply(Result(kept, kept, changed: false), asked);

        foreach (bool right in new[] { false, true })
        {
            Assert.Null(Lower.SideSettings(right).AcousticLowPass);
            Assert.Equal(asked, Upper.SideSettings(right).AcousticHighPass);
        }
    }

    [Fact]
    public void Undo_PutsTheCrossoverAndTheGoalBack()
    {
        Apply(Result(changed: true), new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24));
        AssertFound();

        AgentProposalApplier.Restore(tune.TakeUndo().Channels);

        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(Before, Lower.SideSettings(right).LowPassEdge);
            Assert.Equal(Before, Upper.SideSettings(right).HighPassEdge);
            Assert.Null(Lower.SideSettings(right).AcousticLowPass);
            Assert.Null(Upper.SideSettings(right).AcousticHighPass);
        }
    }

    [Fact]
    public void Undo_BringsBackTheGoalsEveryChannelHeld()
    {
        VirtualCrossoverChannel untouched = session.Channels[2];
        var lowerGoal = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24);
        var upperGoal = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18);
        var otherGoal = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 48);
        foreach (bool right in new[] { false, true })
        {
            Lower.SideSettings(right).AcousticLowPass = lowerGoal;
            Upper.SideSettings(right).AcousticHighPass = upperGoal;
            untouched.SideSettings(right).AcousticHighPass = otherGoal;
        }

        Apply(Result(changed: true), new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24));
        AgentProposalApplier.Restore(tune.TakeUndo().Channels);

        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(lowerGoal, Lower.SideSettings(right).AcousticLowPass);
            Assert.Equal(upperGoal, Upper.SideSettings(right).AcousticHighPass);
            Assert.Equal(otherGoal, untouched.SideSettings(right).AcousticHighPass);
        }
    }

    [Fact]
    public void TheUndo_RemembersTheSessionAsTheApplyLeftIt_AndTellsALaterChange()
    {
        Apply(Result(changed: true), goal: null, generation: 3);

        Assert.Equal("A/B", tune.Undoable(3));
        Assert.Equal(reader.Fingerprint(View), tune.Undo!.FingerprintAfter);
        Assert.True(tune.Unchanged(reader.Fingerprint(View)));

        session.Channels[2].SideSettings(false).GainDb = -3;

        Assert.False(tune.Unchanged(reader.Fingerprint(View)));
        Assert.Same(tune.Undo, tune.UndoFor(3));
    }

    [Fact]
    public void TheUndo_BelongsToTheProjectItWasAppliedIn()
    {
        Apply(Result(changed: true), goal: null, generation: 3);

        Assert.Null(tune.Undoable(4));
        Assert.Null(tune.UndoFor(4));

        Assert.Null(tune.Undo);
        Assert.Null(tune.Undoable(3));
        Assert.Throws<InvalidOperationException>(() => tune.TakeUndo());
    }

    [Fact]
    public void TakingTheUndo_UsesItUp()
    {
        Apply(Result(changed: true), goal: null);

        tune.TakeUndo();

        Assert.Null(tune.Undo);
        Assert.False(tune.Unchanged(reader.Fingerprint(View)));
    }

    [Fact]
    public void TheUndo_KeepsTheBlockOrderAndTheViewItWasTakenIn()
    {
        var view = new AgentViewInputs(VirtualCrossoverGroupView.FrontAndSub, true, true, -6.5, null);
        session.Project.RearFillOffsetMs = 4;

        tune.Apply(Lower, Upper, Result(changed: true), null, view, 1);

        AgentImportUndo before = tune.Undo!.Channels;
        Assert.Equal(session.Channels, before.Order);
        Assert.True(before.HybridTicked);
        Assert.Equal(-6.5, before.TargetLevelDb);
        Assert.Equal(4, before.RearFillOffsetMs);
        Assert.Equal(6, before.Channels.Count);
    }

    [Fact]
    public void AFirCrossoverOnTheSideNotShown_StillRefusesTheTune()
    {
        var lower = new VirtualCrossoverChannel("A");
        var upper = new VirtualCrossoverChannel("B");
        var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 180, 24);
        var design = new FirCrossoverDesign(
            CrossoverKind.LowPass, edge, edge, FirCrossoverMethod.IirMagnitude, FirWindow.Kaiser, 8, 1_023, 48_000);
        lower.SideSettings(true).Fir = design.Build();
        lower.SideSettings(true).FirDesign = design;

        Assert.False(lower.ActiveRight);
        Assert.Contains("right side", AgentJunctionTune.FirCrossoverRefusal(lower, upper), StringComparison.Ordinal);
        Assert.Null(AgentJunctionTune.FirCrossoverRefusal(upper, lower));
    }

    [Fact]
    public void AStatedGoal_IsPartOfTheSessionAnAssistantReadsAgainst()
    {
        string before = reader.Fingerprint(View);

        Lower.SideSettings(false).AcousticLowPass = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24);

        Assert.NotEqual(before, reader.Fingerprint(View));
        Lower.SideSettings(false).AcousticLowPass = null;
        Assert.Equal(before, reader.Fingerprint(View));
    }

    [Fact]
    public void ARefusal_IsASentence()
    {
        JunctionTuneOutcome refused = VirtualCrossoverJunctionTuneSearch.Refusal("the session changed while the search ran");

        Assert.True(refused.Refused);
        Assert.False(refused.CanApply);
        Assert.Equal("Refused.", refused.Status);
        Assert.Equal(
            "The session changed while the search ran.",
            string.Concat(refused.Report.Single().Spans.Select(span => span.Text)));
    }

    private void Apply(JunctionTuneResult landed, JunctionAcousticTarget? goal, long generation = 1) =>
        tune.Apply(Lower, Upper, landed, goal, View, generation);

    private void AssertFound()
    {
        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(FoundLow, Lower.SideSettings(right).LowPassEdge);
            Assert.Equal(FoundHigh, Upper.SideSettings(right).HighPassEdge);
        }
    }

    private static JunctionTuneResult Result(bool changed) =>
        Result(Candidate(Before, Before), Candidate(FoundLow, FoundHigh), changed);

    private static JunctionTuneResult Result(
        JunctionTuneCandidate current, JunctionTuneCandidate best, bool changed) =>
        new(current, best, changed, [], [], [], 1, 90, 360, []);

    private static JunctionTuneCandidate Candidate(CrossoverEdge lowPass, CrossoverEdge highPass) =>
        new(lowPass, highPass, [], [], 90, 360);
}
