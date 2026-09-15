using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

public sealed class AgentProposalApplierTests
{
    [Fact]
    public void Prepare_KeepsTheTickedRows_AndRefusesWhenOneWentStale()
    {
        (AgentSessionSnapshot session, AgentProposal proposal) = Scene();
        var ticked = new HashSet<string>(["op-1", "op-3"]);

        string? problem = AgentProposalApplier.Prepare(
            proposal, ticked, session.Fingerprint, session,
            out List<AgentOperationVerdict> toApply, out _);
        Assert.Null(problem);
        Assert.Equal(["op-1", "op-3"], toApply.Select(verdict => verdict.Id));

        session.Find("A:right")!.Settings.GainDb = -2.5;
        problem = AgentProposalApplier.Prepare(
            proposal, ticked, session.Fingerprint, session, out toApply, out _);
        Assert.NotNull(problem);
        Assert.Contains("op-1", problem);
        Assert.Contains("changed while the review was open", problem);
        Assert.Empty(toApply);

        Assert.Contains("No applicable change", AgentProposalApplier.Prepare(
            proposal, new HashSet<string>(), session.Fingerprint, session, out _, out _));
    }

    [Fact]
    public void Prepare_Refuses_WhenFingerprintMovesFromValidFToG()
    {
        (AgentSessionSnapshot scene, AgentProposal proposal) = Scene();
        const string Package = "11111111-1111-1111-1111-111111111111";
        const string F = "aaaaaaaaaaaaaaaa";
        const string G = "bbbbbbbbbbbbbbbb";
        proposal = proposal with { PackageId = Package };
        AgentSessionSnapshot reviewed = scene with
        {
            LastPackageId = Package,
            LastPackageFingerprint = F,
            Fingerprint = F
        };
        var ticked = new HashSet<string>(["op-1"]);

        Assert.True(AgentProposalValidator.Review(proposal, reviewed)
            .Verdicts.Single(verdict => verdict.Id == "op-1").Ticked);

        string? problem = AgentProposalApplier.Prepare(
            proposal, ticked, F, reviewed with { Fingerprint = G },
            out List<AgentOperationVerdict> toApply, out _);
        Assert.NotNull(problem);
        Assert.Contains("changed while the review was open", problem);
        Assert.Empty(toApply);
    }

    [Fact]
    public void Prepare_AppliesManuallyTickedStaleRow_WhenReviewAndApplyBothUseG()
    {
        (AgentSessionSnapshot scene, AgentProposal proposal) = Scene();
        const string Package = "11111111-1111-1111-1111-111111111111";
        const string F = "aaaaaaaaaaaaaaaa";
        const string G = "bbbbbbbbbbbbbbbb";
        proposal = proposal with { PackageId = Package };
        AgentSessionSnapshot staleAtReview = scene with
        {
            LastPackageId = Package,
            LastPackageFingerprint = F,
            Fingerprint = G
        };
        var ticked = new HashSet<string>(["op-1"]);

        AgentOperationVerdict row = AgentProposalValidator.Review(proposal, staleAtReview)
            .Verdicts.Single(verdict => verdict.Id == "op-1");
        Assert.True(row.Applicable);
        Assert.False(row.Ticked);

        string? problem = AgentProposalApplier.Prepare(
            proposal, ticked, G, staleAtReview,
            out List<AgentOperationVerdict> toApply, out _);

        Assert.Null(problem);
        Assert.Equal("op-1", Assert.Single(toApply).Id);
        List<AgentUndoEntry> undo = AgentProposalApplier.Apply(toApply);
        Assert.Equal(-3.0, staleAtReview.Find("A:right")!.Settings.GainDb);
        Assert.Single(undo);
    }

    [Fact]
    public void Prepare_RefusesManuallyTickedStaleRow_WhenFingerprintMovesFromGToH()
    {
        (AgentSessionSnapshot scene, AgentProposal proposal) = Scene();
        const string Package = "11111111-1111-1111-1111-111111111111";
        const string F = "aaaaaaaaaaaaaaaa";
        const string G = "bbbbbbbbbbbbbbbb";
        const string H = "cccccccccccccccc";
        proposal = proposal with { PackageId = Package };
        AgentSessionSnapshot staleAtReview = scene with
        {
            LastPackageId = Package,
            LastPackageFingerprint = F,
            Fingerprint = G
        };
        var ticked = new HashSet<string>(["op-1"]);

        Assert.False(AgentProposalValidator.Review(proposal, staleAtReview)
            .Verdicts.Single(verdict => verdict.Id == "op-1").Ticked);

        string? problem = AgentProposalApplier.Prepare(
            proposal, ticked, G, staleAtReview with { Fingerprint = H },
            out List<AgentOperationVerdict> toApply, out _);

        Assert.NotNull(problem);
        Assert.Contains("changed while the review was open", problem);
        Assert.Empty(toApply);
    }

    [Fact]
    public void Prepare_IgnoresARefusedRowThatSharesTheTickedRowsId()
    {
        (AgentSessionSnapshot session, AgentProposal proposal) = Scene();
        proposal = proposal with
        {
            Rejected = [new AgentRejectedOperation("op-1", "garbage", "Unsupported operation 'garbage'.")]
        };

        string? problem = AgentProposalApplier.Prepare(
            proposal, new HashSet<string>(["op-1"]), session.Fingerprint, session,
            out List<AgentOperationVerdict> toApply, out _);

        Assert.Null(problem);
        AgentOperationVerdict only = Assert.Single(toApply);
        Assert.IsType<SetGainOperation>(only.Operation);
    }

    [Fact]
    public void Prepare_WarnsAboutWhatTheTickedSubsetLeaves_WhenTheReviewJudgedTheWholeSet()
    {
        // Together clean, but unticking the bank leaves the 1 kHz bell in the new 1.2 kHz junction zone: the commit must say so.
        (AgentSessionSnapshot session, _) = Scene();
        VirtualCrossoverChannelSettings bLeft = session.Find("B:left")!.Settings;
        bLeft.PeqBands = [new PeqBand(1_000, 4, -3)];
        var proposal = new AgentProposal(null, "summary", [], [],
            [
                new SetCrossoverOperation("op-1", "B:left", "",
                    new AgentCrossover("BandPass", new AgentCrossoverEdge("LinkwitzRiley", 250, 24, null), new AgentCrossoverEdge("LinkwitzRiley", 2800, 24, null)),
                    new AgentCrossover("BandPass", new AgentCrossoverEdge("LinkwitzRiley", 250, 24, null), new AgentCrossoverEdge("LinkwitzRiley", 1200, 24, null))),
                new ReplacePeqBankOperation("op-2", "B:left", "",
                    AgentPeqHash.Compute(bLeft.PeqPreampDb, bLeft.PeqBands),
                    new AgentPeqBank(0, [new AgentPeqBand("Peaking", 300, 1, -2)]))
            ],
            []);

        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);
        Assert.All(review.Verdicts, verdict => Assert.DoesNotContain("junction zone", verdict.Message));

        Assert.Null(AgentProposalApplier.Prepare(
            proposal, new HashSet<string>(["op-1", "op-2"]), session.Fingerprint, session,
            out _, out List<string> both));
        Assert.Empty(both);

        Assert.Null(AgentProposalApplier.Prepare(
            proposal, new HashSet<string>(["op-1"]), session.Fingerprint, session,
            out List<AgentOperationVerdict> toApply, out List<string> crossoverOnly));
        Assert.Single(toApply);
        string warning = Assert.Single(crossoverOnly);
        Assert.StartsWith("B left:", warning);
        Assert.Contains("Band at 1000 Hz (Q 4)", warning);
        Assert.Contains("around the 1200 Hz crossover", warning);

        bLeft.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1200, 24);
        var gainOnly = new AgentProposal(null, "summary", [], [],
            [new SetGainOperation("op-3", "B:left", "", 0, -1)], []);
        Assert.Null(AgentProposalApplier.Prepare(
            gainOnly, new HashSet<string>(["op-3"]), session.Fingerprint, session,
            out _, out List<string> untouched));
        Assert.Empty(untouched);
    }

    [Fact]
    public void Apply_WritesOnlyTheTickedRows_AndRestoreBringsEverythingBack()
    {
        (AgentSessionSnapshot session, AgentProposal proposal) = Scene();
        VirtualCrossoverChannelSettings aRight = session.Find("A:right")!.Settings;
        VirtualCrossoverChannelSettings bLeft = session.Find("B:left")!.Settings;
        VirtualCrossoverChannelSettings before = AgentOperations.CloneEditable(bLeft);
        Assert.Null(AgentProposalApplier.Prepare(
            proposal, new HashSet<string>(["op-1", "op-3", "op-4"]),
            session.Fingerprint, session, out List<AgentOperationVerdict> toApply, out _));

        List<AgentUndoEntry> undo = AgentProposalApplier.Apply(toApply);

        Assert.Equal(-3.0, aRight.GainDb);
        Assert.True(bLeft.InvertPolarity);
        Assert.Equal(-1.0, bLeft.PeqPreampDb);
        Assert.Single(bLeft.PeqBands);
        Assert.Equal(AgentProposalApplier.PeqSourceName, bLeft.PeqSourceName);
        Assert.Equal(1.42, aRight.DelayMs);
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
    public void Restore_PutsBackAPhaseRotation_NoOperationEverWrote()
    {
        // Undo restores the whole editable chain, or a rotation dialled in between would survive it.
        (AgentSessionSnapshot session, AgentProposal proposal) = Scene();
        VirtualCrossoverChannelSettings aRight = session.Find("A:right")!.Settings;
        aRight.PhaseRotationDegrees = 56.25;
        Assert.Null(AgentProposalApplier.Prepare(
            proposal, new HashSet<string>(["op-1"]),
            session.Fingerprint, session, out List<AgentOperationVerdict> toApply, out _));

        List<AgentUndoEntry> undo = AgentProposalApplier.Apply(toApply);
        aRight.PhaseRotationDegrees = 90;

        AgentProposalApplier.Restore(undo);

        Assert.Equal(56.25, aRight.PhaseRotationDegrees);
    }

    [Fact]
    public void Apply_PutsBackWhatItWrote_WhenAWriteThrows()
    {
        (AgentSessionSnapshot session, AgentProposal proposal) = Scene();
        VirtualCrossoverChannelSettings aRight = session.Find("A:right")!.Settings;
        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);
        AgentOperationVerdict gain = review.Verdicts.Single(verdict => verdict.Id == "op-1");
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
                new AgentChannelSnapshot("A", AgentChannelSide.Left, aLeft, true, []),
                new AgentChannelSnapshot("A", AgentChannelSide.Right, aRight, true, []),
                new AgentChannelSnapshot("B", AgentChannelSide.Left, bLeft, true, [])
            ],
            96_000, 50, null,
            new AgentAutoDelaySettings(0.25, RightHandDrive: false, AdjustGains: false, 1.0, 15.0),
            VirtualCrossoverSpatialAverageMode.MovingMic,
            HybridTicked: false);
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
