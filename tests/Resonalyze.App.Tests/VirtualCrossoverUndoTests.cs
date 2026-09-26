using System.Text;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>Each command's write, taken back by the snapshot its undo keeps: every channel side field for field, the block
/// order and the session-wide settings the command moves; and the rules of a step. The dialogs' glue is in
/// <see cref="VirtualCrossoverUndoWiringTests"/>.</summary>
public sealed class VirtualCrossoverUndoTests : IDisposable
{
    private static readonly AgentViewInputs View = new(VirtualCrossoverGroupView.FrontAndSub, false, false, 0, null);

    private readonly VirtualCrossoverSession session = new();
    private readonly VirtualCrossoverProcessingCoordinator coordinator = new();
    private readonly VirtualCrossoverUndoHistory history = new();
    private readonly AgentSessionReader reader;

    public VirtualCrossoverUndoTests()
    {
        session.Project.Pairs.Add(new VirtualCrossoverChannelPairSettings());
        for (int index = 0; index < session.Project.Pairs.Count; index++)
        {
            var channel = new VirtualCrossoverChannel(VirtualCrossoverSheet.ChannelName(index))
            {
                Pair = session.Project.Pairs[index]
            };
            session.Channels.Add(channel);
            foreach (bool right in new[] { false, true })
            {
                VirtualCrossoverChannelSettings settings = channel.SideSettings(right);
                double shift = index * 10 + (right ? 5 : 0);
                settings.GainDb = -1 - shift / 10;
                settings.DelayMs = 0.37 + shift / 100;
                settings.InvertPolarity = right;
                settings.CrossoverKind = CrossoverKind.BandPass;
                settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 60 + shift, 12);
                settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Bessel, 4_000 + shift, 18);
                settings.AcousticHighPass = right ? new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24) : null;
                settings.PhaseRotationDegrees = 30 + shift;
                settings.PeqPreampDb = -2.5 - shift / 10;
                settings.PeqBands = [new PeqBand(1_000 + shift, 2, -3), new PeqBand(90 + shift, 1, 0, PeqBandType.AllPassSecondOrder)];
                settings.PeqSourceName = $"bank {shift}";
            }
        }

        // A mono block has one settings set: the snapshot takes it alone, and a write to it is undone like any other.
        session.Channels[3].Pair.Mono = true;
        session.Project.SetStereoScene(0.25, rightHandDrive: false);
        session.Project.StereoLevelDifferenceDb = -1;
        session.Project.RearFillOffsetMs = 12;
        reader = new AgentSessionReader(
            session,
            coordinator,
            VirtualCrossoverMetrics.Through(
                coordinator, () => session.MagnitudeGate, oppositeSide: false, channel => session.Calibration.For(channel)),
            new VirtualCrossoverHybrid(session));
    }

    public void Dispose() => coordinator.Dispose();

    [Fact]
    public void UndoingAutoCrossover_PutsBackEveryChannel_TheBlockOrderAndThePhaseRotations()
    {
        Taken taken = Take();
        var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24);

        int cleared = VirtualCrossoverAutoSetup.Write(
            session.Channels,
            [
                new CrossoverProposal(CrossoverKind.LowPass, null, edge, -4),
                new CrossoverProposal(CrossoverKind.BandPass, edge, edge with { FrequencyHz = 3_000 }, -6, true),
                new CrossoverProposal(CrossoverKind.HighPass, edge with { FrequencyHz = 3_000 }, null, -9),
                new CrossoverProposal(CrossoverKind.HighPass, edge with { FrequencyHz = 5_000 }, null, -12)
            ]);
        session.Reorder(VirtualCrossoverAutoSetup.Reorder(session.Channels, [.. session.Channels], [2, 0, 3, 1]));

        Assert.Equal(7, cleared);
        Assert.NotEqual(taken.Order, session.Channels);
        Assert.Equal(-12, taken.Order[3].SideSettings(false).GainDb);

        taken.Before.Restore(session, reader);

        AssertAsTaken(taken);
        Assert.Equal(["A", "B", "C", "D"], session.Channels.Select(channel => channel.Name));
        Assert.Equal(35, session.Channels[0].SideSettings(true).PhaseRotationDegrees);
    }

    [Fact]
    public void UndoingAutoDelay_PutsBackTheDelaysPolarityGains_AndTheScene()
    {
        Taken taken = Take();
        var request = new AutoDelayRunRequest(0.4, true, true, 2, 18);
        List<AutoDelayChannelOutcome> outcomes = session.Sides()
            .Select(side => (side.Channel, Settings: side.Channel.SideSettings(side.RightSide)))
            .Select(side => new AutoDelayChannelOutcome(
                side.Channel, side.Settings, side.Channel.Name,
                side.Settings.DelayMs, side.Settings.InvertPolarity, side.Settings.GainDb,
                side.Settings.DelayMs + 1.23, !side.Settings.InvertPolarity, side.Settings.GainDb - 2, true,
                null, null, string.Empty, null, string.Empty))
            .ToList();

        VirtualCrossoverAutoDelay.Commit(
            new AutoDelayRunResult(outcomes, Stereo: true, request, string.Empty, new StringBuilder()), session.Project);

        Assert.Equal(18, session.Project.RearFillOffsetMs);
        Assert.True(session.Project.StereoRightHandDrive);
        Assert.NotEqual(taken.Fingerprint, reader.Fingerprint(View));

        taken.Before.Restore(session, reader);

        AssertAsTaken(taken);
        Assert.Equal(0.25, session.Project.StereoSceneOffsetMagnitudeMs);
        Assert.False(session.Project.StereoRightHandDrive);
        Assert.Equal(-1, session.Project.StereoLevelDifferenceDb);
        Assert.Equal(12, session.Project.RearFillOffsetMs);
    }

    [Fact]
    public void UndoingACopy_PutsBackEveryPartItCarried()
    {
        Taken taken = Take();
        var everything = new VirtualCrossoverCopyScope(
            Gain: true, Delay: true, InvertPolarity: true, Crossover: true, AllPass: true, Phase: true, Peq: true, Fir: true);
        foreach (VirtualCrossoverChannel channel in session.Channels.Take(3))
        {
            everything.Copy(channel.SideSettings(false), channel.SideSettings(true));
        }

        Assert.Null(session.Channels[0].SideSettings(true).AcousticHighPass);
        Assert.Equal(30, session.Channels[0].SideSettings(true).PhaseRotationDegrees);

        taken.Before.Restore(session, reader);

        AssertAsTaken(taken);
    }

    [Fact]
    public void AnUndo_BelongsToTheProjectItWasWrittenIn()
    {
        VirtualCrossoverUndo undo = VirtualCrossoverUndo.AutoCrossover(history);
        Write(undo, 3, "A, B");

        Assert.Equal("A, B", undo.Undoable(3));
        Assert.Null(undo.Undoable(4));
        Assert.NotNull(undo.Step);
        Assert.Null(undo.For(4));
        Assert.Null(undo.Step);
        Assert.Null(undo.Undoable(3));
        Assert.Throws<InvalidOperationException>(() => undo.Take());
    }

    [Fact]
    public void ALaterChange_IsOneUndoWouldTakeBack_NotTheViewOrTheGate()
    {
        VirtualCrossoverUndo undo = VirtualCrossoverUndo.AutoDelay(history);
        Write(undo, 1, "both sides");
        AgentImportUndo before = undo.Step!.Before;

        session.Project.ActiveSideRight = true;
        session.Project.PhaseGateFor(rightSide: false).OffsetMs = 20;
        session.Channels[0].Pair.Enabled = false;
        Assert.True(undo.Unchanged(Now()));
        Assert.False(undo.Unchanged(Now() with { HybridTicked = true }));
        Assert.False(undo.Unchanged(Now() with { Order = [.. session.Channels.AsEnumerable().Reverse()] }));
        Assert.False(undo.Unchanged(Now() with { RearFillOffsetMs = 3 }));

        session.Channels[1].SideSettings(true).DelayMs += 0.01;

        Assert.False(undo.Unchanged(Now()));
        Assert.Same(undo.Step, undo.For(1));
        Assert.Same(before, undo.Take());
        Assert.Null(undo.Step);
    }

    [Fact]
    public void AWriteThatChangedNothing_LeavesTheStepBeforeIt()
    {
        VirtualCrossoverUndo undo = VirtualCrossoverUndo.CopySide(history);
        Write(undo, 1, "L → R of A");
        VirtualCrossoverUndoStep first = undo.Step!;

        undo.Remember(Now(), 1, "L → R of A", Now());

        Assert.Same(first, undo.Step);
    }

    [Fact]
    public void TakingAStep_DropsTheStepsWrittenAfterIt_AndKeepsTheOnesBefore()
    {
        VirtualCrossoverUndo crossover = VirtualCrossoverUndo.AutoCrossover(history);
        VirtualCrossoverUndo delay = VirtualCrossoverUndo.AutoDelay(history);
        VirtualCrossoverUndo copy = VirtualCrossoverUndo.CopySide(history);
        Write(crossover, 1, "A, B");
        Write(delay, 1, "both sides");
        Write(copy, 1, "L → R of A");

        delay.Take().Restore(session, reader);

        Assert.Null(copy.Step);
        Assert.NotNull(crossover.Step);
        Assert.True(crossover.Unchanged(Now()));

        crossover.Take();
        Write(copy, 1, "L → R of B");
        Assert.NotNull(copy.Step);
    }

    [Fact]
    public void TheQuestion_NamesTheWriteTheSessionMovedOnFrom()
    {
        var tune = new VirtualCrossoverJunctionTuneApply(session, reader, history);
        AgentImportUndo before = Now();
        session.Channels[0].SideSettings(false).GainDb -= 1;
        tune.Remember(before, 1, session.Channels[0], session.Channels[1], Now());
        VirtualCrossoverUndo copy = VirtualCrossoverUndo.CopySide(history);
        Write(copy, 1, VirtualCrossoverUndo.Copied(fromRight: false, session.Channels.Take(2)));

        Assert.Contains("A/B", tune.Undo.ChangedSince(tune.Undo.Step!));
        Assert.Contains("L → R of A, B", copy.ChangedSince(copy.Step!));
    }

    [Fact]
    public void ARefusal_OffersTheUndo_OnlyWhileThereIsOneInThisProject()
    {
        VirtualCrossoverUndo undo = VirtualCrossoverUndo.AutoDelay(history);

        Assert.Null(undo.InsteadOf("Auto delay cannot run.", 1));

        Write(undo, 1, "both sides");

        Assert.Contains("Auto delay cannot run.", undo.InsteadOf("Auto delay cannot run.", 1));
        Assert.Null(undo.InsteadOf("Auto delay cannot run.", 2));
    }

    [Fact]
    public void TheComparison_SeesEveryFieldTheRestoreCopies()
    {
        VirtualCrossoverChannelSettings kept = session.Channels[1].SideSettings(false);
        var edited = new List<Action<VirtualCrossoverChannelSettings>>
        {
            settings => settings.GainDb += 0.1,
            settings => settings.DelayMs += 0.01,
            settings => settings.InvertPolarity ^= true,
            settings => settings.CrossoverKind = CrossoverKind.Off,
            settings => settings.LowPassEdge = settings.LowPassEdge with { FrequencyHz = 3_000 },
            settings => settings.HighPassEdge = settings.HighPassEdge with { SlopeDbPerOctave = 24 },
            settings => settings.AcousticLowPass = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 12),
            settings => settings.AcousticHighPass = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 12),
            settings => settings.PhaseRotationDegrees += 1,
            settings => settings.PeqPreampDb -= 1,
            settings => settings.PeqBands = [.. settings.PeqBands.Skip(1)],
            settings => settings.PeqSourceName = "other",
            settings => settings.Fir = new FirFilter([1.0], 48_000),
            settings => settings.FirSourceName = "other.wav",
            settings => settings.FirDesign = new FirCrossoverDesign(
                CrossoverKind.LowPass, default, default, FirCrossoverMethod.IirMagnitude, FirWindow.Kaiser, 8, 1_023, 48_000),
            settings => settings.FirRunSampleRateHz = 96_000
        };

        Assert.True(AgentProposalApplier.SameEditable(kept, AgentOperations.CloneEditable(kept)));
        Assert.All(edited, edit =>
        {
            VirtualCrossoverChannelSettings changed = AgentOperations.CloneEditable(kept);
            edit(changed);
            Assert.False(AgentProposalApplier.SameEditable(kept, changed));
            AgentProposalApplier.CopyEditable(kept, changed);
            Assert.True(AgentProposalApplier.SameEditable(kept, changed));
        });
    }

    private AgentImportUndo Now() => AgentImportUndo.Capture(session, reader, View);

    // A write to one gain, remembered as a command would.
    private void Write(VirtualCrossoverUndo undo, long generation, string what)
    {
        AgentImportUndo before = Now();
        session.Channels[0].SideSettings(false).GainDb -= 0.5;
        undo.Remember(before, generation, what, Now());
    }

    // The undo's snapshot, and apart from it copies of what it must bring back, since it restores into the live objects.
    private Taken Take() =>
        new(
            Now(),
            [.. session.Channels],
            [.. session.Project.Pairs],
            session.Sides()
                .Select(side => side.Channel.SideSettings(side.RightSide))
                .ToDictionary(settings => settings, AgentOperations.CloneEditable),
            reader.Fingerprint(View));

    private void AssertAsTaken(Taken taken)
    {
        Assert.Equal(taken.Order, session.Channels);
        Assert.Equal(taken.Pairs, session.Project.Pairs);
        Assert.All(session.Channels, channel => Assert.Same(session.Project.Pairs[session.Channels.IndexOf(channel)], channel.Pair));
        Assert.All(taken.Sides, side => Assert.True(
            AgentProposalApplier.SameEditable(side.Value, side.Key), $"{side.Key.PeqSourceName} was not put back."));
        Assert.True(taken.Before.SameAs(Now()));
        Assert.Equal(taken.Fingerprint, reader.Fingerprint(View));
    }

    private sealed record Taken(
        AgentImportUndo Before,
        List<VirtualCrossoverChannel> Order,
        List<VirtualCrossoverChannelPairSettings> Pairs,
        Dictionary<VirtualCrossoverChannelSettings, VirtualCrossoverChannelSettings> Sides,
        string Fingerprint);
}
