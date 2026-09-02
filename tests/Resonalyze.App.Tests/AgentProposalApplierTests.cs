using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// The commit half of an import: a second look at the ticked rows against the
/// live settings, one write for the whole set, and an exact way back. Pinned
/// on settings objects alone — the panel adds only the control refresh.
/// </summary>
public sealed class AgentProposalApplierTests
{
    [Fact]
    public void Prepare_KeepsTheTickedRows_AndRefusesWhenOneWentStale()
    {
        (AgentSessionSnapshot session, AgentProposal proposal) = Scene();
        var ticked = new HashSet<string>(["op-1", "op-3"]);

        string? problem = AgentProposalApplier.Prepare(proposal, ticked, session, out List<AgentOperationVerdict> toApply);
        Assert.Null(problem);
        Assert.Equal(["op-1", "op-3"], toApply.Select(verdict => verdict.Id));

        // The user turned the gain knob while the dialog was open.
        session.Find("A:right")!.Settings.GainDb = -2.5;
        problem = AgentProposalApplier.Prepare(proposal, ticked, session, out toApply);
        Assert.NotNull(problem);
        Assert.Contains("op-1", problem);
        Assert.Contains("changed while the review was open", problem);
        Assert.Empty(toApply);

        Assert.Contains("No applicable change", AgentProposalApplier.Prepare(proposal, new HashSet<string>(), session, out _));
    }

    [Fact]
    public void Prepare_IgnoresARefusedRowThatSharesTheTickedRowsId()
    {
        (AgentSessionSnapshot session, AgentProposal proposal) = Scene();
        proposal = proposal with
        {
            Rejected = [new AgentRejectedOperation("op-1", "garbage", "Unsupported operation 'garbage'.")]
        };

        string? problem = AgentProposalApplier.Prepare(proposal, new HashSet<string>(["op-1"]), session, out List<AgentOperationVerdict> toApply);

        Assert.Null(problem);
        AgentOperationVerdict only = Assert.Single(toApply);
        Assert.IsType<SetGainOperation>(only.Operation);
    }

    [Fact]
    public void Apply_WritesOnlyTheTickedRows_AndRestoreBringsEverythingBack()
    {
        (AgentSessionSnapshot session, AgentProposal proposal) = Scene();
        VirtualCrossoverChannelSettings aRight = session.Find("A:right")!.Settings;
        VirtualCrossoverChannelSettings bLeft = session.Find("B:left")!.Settings;
        VirtualCrossoverChannelSettings before = AgentOperations.CloneEditable(bLeft);
        Assert.Null(AgentProposalApplier.Prepare(
            proposal, new HashSet<string>(["op-1", "op-3", "op-4"]), session, out List<AgentOperationVerdict> toApply));

        List<AgentUndoEntry> undo = AgentProposalApplier.Apply(toApply);

        // Ticked: gain on A right, polarity and the PEQ bank on B left.
        Assert.Equal(-3.0, aRight.GainDb);
        Assert.True(bLeft.InvertPolarity);
        Assert.Equal(-1.0, bLeft.PeqPreampDb);
        Assert.Single(bLeft.PeqBands);
        Assert.Equal(AgentProposalApplier.PeqSourceName, bLeft.PeqSourceName);
        // Not ticked: the delay on A right stays.
        Assert.Equal(1.42, aRight.DelayMs);
        // Untouched fields stay untouched.
        Assert.Equal(CrossoverKind.BandPass, bLeft.CrossoverKind);
        Assert.Equal("left mid.json", bLeft.DisplayName);

        Assert.Equal(2, undo.Count);
        AgentProposalApplier.Restore(undo);
        Assert.Equal(-2.0, aRight.GainDb);
        Assert.False(bLeft.InvertPolarity);
        Assert.Equal(before.PeqPreampDb, bLeft.PeqPreampDb);
        Assert.Equal(before.PeqBands, bLeft.PeqBands);
        Assert.Equal(before.PeqSourceName, bLeft.PeqSourceName);
        Assert.Equal(before.LowPassEdge, bLeft.LowPassEdge);
    }

    [Fact]
    public void Apply_PutsBackWhatItWrote_WhenAWriteThrows()
    {
        (AgentSessionSnapshot session, AgentProposal proposal) = Scene();
        VirtualCrossoverChannelSettings aRight = session.Find("A:right")!.Settings;
        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);
        AgentOperationVerdict gain = review.Verdicts.Single(verdict => verdict.Id == "op-1");
        // A crossover naming a family the mapper cannot resolve: the review would
        // have rejected it, so this is a forged row — the applier still has to
        // leave the channel as it found it.
        AgentOperationVerdict forged = review.Verdicts.Single(verdict => verdict.Id == "op-2") with
        {
            Operation = new SetCrossoverOperation("op-2", "A:right", "", new AgentCrossover("Off", null, null),
                new AgentCrossover("LowPass", null, new AgentCrossoverEdge("NoSuchFamily", 100, 24, null)))
        };

        Assert.Throws<InvalidDataException>(() => AgentProposalApplier.Apply([gain, forged]));

        Assert.Equal(-2.0, aRight.GainDb);
        Assert.Equal(CrossoverKind.Off, aRight.CrossoverKind);
    }

    private static (AgentSessionSnapshot Session, AgentProposal Proposal) Scene()
    {
        var aLeft = new VirtualCrossoverChannelSettings();
        var aRight = new VirtualCrossoverChannelSettings { GainDb = -2.0, DelayMs = 1.42 };
        var bLeft = new VirtualCrossoverChannelSettings
        {
            DisplayName = "left mid.json",
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 250, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2800, 24),
            PeqBands = [new PeqBand(820, 2.1, -2.4), new PeqBand(3000, 1.0, 1.5)],
            PeqSourceName = "EQ Wizard"
        };
        var session = new AgentSessionSnapshot(
            [
                new AgentChannelSnapshot("A", AgentChannelSide.Left, aLeft),
                new AgentChannelSnapshot("A", AgentChannelSide.Right, aRight),
                new AgentChannelSnapshot("B", AgentChannelSide.Left, bLeft)
            ],
            96_000, 50, null);
        var proposal = new AgentProposal(null, "summary", [], [],
            [
                new SetGainOperation("op-1", "A:right", "level", -2.0, -3.0),
                new SetDelayOperation("op-2", "A:right", "arrival", 1.42, 1.5),
                new SetPolarityOperation("op-3", "B:left", "phase", false, true),
                new ReplacePeqBankOperation("op-4", "B:left", "door",
                    AgentPeqHash.Compute(bLeft.PeqPreampDb, bLeft.PeqBands),
                    new AgentPeqBank(-1.0, [new AgentPeqBand("Peaking", 820, 2.1, -2.4)]))
            ],
            []);
        return (session, proposal);
    }
}
