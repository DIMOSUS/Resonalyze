using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// The review of a <c>probe</c>: the one operation that writes nothing, so the
/// only thing to check is that the question can be answered — and that the
/// settings a variant states are ones a proposal could have written, since a
/// variant that reads well is meant to become one. It is offered ticked and
/// plain (no warning, nothing to be careful about) and it survives a package
/// the session can no longer vouch for, because what it reads is the session as
/// it is now.
/// </summary>
public sealed class AgentProbeReviewTests
{
    private const string Package = "11111111-1111-1111-1111-111111111111";

    private static CrossoverEdge Edge(CrossoverFilterFamily family, double hz, int slope) =>
        new(family, hz, slope);

    // B mid (BP 80–2000) into C tweeter (HP 2000), both stereo and measured;
    // A is a mono sub under B, D a rear fill in its own group.
    private static AgentSessionSnapshot Session(string? lastPackageId = Package)
    {
        var a = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = Edge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)
        };
        VirtualCrossoverChannelSettings B() => new()
        {
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = Edge(CrossoverFilterFamily.LinkwitzRiley, 80, 24),
            LowPassEdge = Edge(CrossoverFilterFamily.Butterworth, 2_000, 48),
            PeqBands = [new PeqBand(820, 2.1, -2.4)]
        };
        VirtualCrossoverChannelSettings C() => new()
        {
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = Edge(CrossoverFilterFamily.Butterworth, 2_000, 48)
        };
        VirtualCrossoverChannelSettings D() => new()
        {
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = Edge(CrossoverFilterFamily.LinkwitzRiley, 100, 24)
        };
        return new AgentSessionSnapshot(
            [
                new AgentChannelSnapshot("A", AgentChannelSide.Mono, a, true, [], VirtualCrossoverZone.Sub),
                new AgentChannelSnapshot("B", AgentChannelSide.Left, B(), true, []),
                new AgentChannelSnapshot("B", AgentChannelSide.Right, B(), true, []),
                new AgentChannelSnapshot("C", AgentChannelSide.Left, C(), true, []),
                new AgentChannelSnapshot("C", AgentChannelSide.Right, C(), true, []),
                new AgentChannelSnapshot("D", AgentChannelSide.Left, D(), true, [], VirtualCrossoverZone.Rear),
                new AgentChannelSnapshot("D", AgentChannelSide.Right, D(), true, [], VirtualCrossoverZone.Rear)
            ],
            96_000,
            50,
            lastPackageId,
            new AgentAutoDelaySettings(0.25, RightHandDrive: false, AdjustGains: false, 1.0, 15.0),
            VirtualCrossoverSpatialAverageMode.Off,
            HybridTicked: false);
    }

    private static AgentProposal Proposal(params AgentOperation[] operations) =>
        new(Package, "summary", [], [], operations, []);

    private static AgentProbeChange Change(
        string channelId = "C:left",
        double? gainDb = null,
        double? delayMs = null,
        bool? invertPolarity = null,
        AgentCrossover? crossover = null,
        AgentPeqBank? peq = null) =>
        new(channelId, gainDb, delayMs, invertPolarity, crossover, peq);

    private static ProbeOperation Junction(
        string id = "op-1", string junctionId = "left:B-C", params AgentProbeVariant[] variants) =>
        new(id, "which of these sums better", AgentProtocol.JunctionProbe, junctionId,
            variants.Length > 0
                ? variants
                : [new AgentProbeVariant("steeper", [Change(delayMs: 2.5)])]);

    private static AgentCrossover Crossover(string family, double hz, int slope) =>
        new("HighPass", new AgentCrossoverEdge(family, hz, slope, null), null);

    [Fact]
    public void Review_OffersAProbeTicked_AndSaysItWritesNothing()
    {
        AgentProposalReview review = AgentProposalValidator.Review(
            Proposal(
                Junction(variants: new AgentProbeVariant("LR24 at 2 kHz",
                    [Change(crossover: Crossover("LinkwitzRiley", 2_000, 24))])),
                new ProbeOperation("op-2", "what would a delay find",
                    AgentProtocol.JunctionDelayProbe, "left:B-C", null),
                new ProbeOperation("op-3", "how much is not the PEQ's to fix",
                    AgentProtocol.ExcessGroupDelayProbe, null, null)),
            Session());

        Assert.All(review.Verdicts, verdict =>
        {
            Assert.Equal(AgentVerdictStatus.Valid, verdict.Status);
            Assert.True(verdict.Applicable);
            Assert.True(verdict.Ticked);
            Assert.Equal("Probe", verdict.Parameter);
            Assert.Contains("Reads only", verdict.Message);
            Assert.Contains("nothing in the tune is changed", verdict.Message);
        });
        Assert.Equal(["B/C", "B/C", AgentProposalValidator.AllChannels],
            review.Verdicts.Select(verdict => verdict.ChannelLabel));
        Assert.Equal(
            [
                "read this junction under 1 variant (crossover) beside the settings it has now",
                "read what a delay search would find at this junction",
                "read every measured channel's excess group delay"
            ],
            review.Verdicts.Select(verdict => verdict.Proposed));
        Assert.Equal("B: LP BW48 2000 Hz; C: HP BW48 2000 Hz", review.Verdicts[0].Current);
        Assert.Equal("every measured channel", review.Verdicts[2].Current);
        Assert.Contains("copy of the settings", review.Verdicts[0].Message);
    }

    [Fact]
    public void Review_NamesWhatTheVariantsTouch()
    {
        var bank = new AgentPeqBank(0, []);
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(Junction(variants:
                [
                    new AgentProbeVariant("no bank", [Change(peq: bank)]),
                    new AgentProbeVariant("and 1 dB down, flipped",
                        [Change(peq: bank), Change("B:left", gainDb: -1, invertPolarity: true)])
                ])),
            Session()).Verdicts[0];

        Assert.Equal(
            "read this junction under 2 variants (PEQ, gain, polarity) beside the settings it has now",
            verdict.Proposed);
    }

    [Fact]
    public void Review_KeepsAProbe_OnAPackageTheSessionCannotVouchFor()
    {
        // Every engine is refused there; a probe is not. It writes nothing, and
        // what it reads is the session as it is now — which is exactly what a
        // reader of a package that has gone stale needs.
        AgentProposalReview review = AgentProposalValidator.Review(
            Proposal(
                Junction(),
                new RunAutoDelayOperation("op-2", "", null, null, null, null, null)),
            Session(lastPackageId: null));

        Assert.True(review.Verdicts[0].Applicable);
        Assert.True(review.Verdicts[0].Ticked);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Single(review.Warnings);
    }

    [Fact]
    public void Review_LetsAProbeStandBesideEveryOtherRow_SinceItOverwritesNothing()
    {
        AgentCrossover current = new("HighPass", new AgentCrossoverEdge("Butterworth", 2_000, 48, null), null);
        AgentCrossover proposed = new("HighPass", new AgentCrossoverEdge("Butterworth", 2_200, 48, null), null);
        AgentProposalReview review = AgentProposalValidator.Review(
            Proposal(
                Junction(),
                new SetCrossoverOperation("op-2", "C:left", "", current, proposed),
                new TuneJunctionOperation("op-3", "", "left:A-B", null, null, null, null, null)),
            Session());

        Assert.All(review.Verdicts, verdict => Assert.True(verdict.Applicable));
    }

    [Theory]
    [InlineData("left:B-X", "Unknown junction")]
    [InlineData("left:B-D", "different groups")]
    [InlineData("B-C", "is not a junction id")]
    public void Review_RefusesAProbeForAJunctionTheSessionDoesNotHave(string junctionId, string reason)
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(Junction(junctionId: junctionId)), Session()).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains(reason, verdict.Message);
    }

    [Fact]
    public void Review_HoldsAVariantsSettingsToTheLimitsAProposalIsHeldTo()
    {
        string Judge(params AgentProbeVariant[] variants) =>
            AgentProposalValidator.Review(
                Proposal(Junction(variants: variants)), Session()).Verdicts[0].Message;

        Assert.Contains("names no variant", AgentProposalValidator.Review(
            Proposal(new ProbeOperation("op-1", "", AgentProtocol.JunctionProbe, "left:B-C", [])),
            Session()).Verdicts[0].Message);
        Assert.Contains("changes nothing", Judge(new AgentProbeVariant("empty", [])));
        Assert.Contains("states no setting",
            Judge(new AgentProbeVariant(null, [Change()])));
        Assert.Contains("is not one of the junction's two channels",
            Judge(new AgentProbeVariant(null, [Change("A:mono", gainDb: -1)])));
        // The side matters: the junction named is the left one.
        Assert.Contains("is not one of the junction's two channels",
            Judge(new AgentProbeVariant(null, [Change("B:right", gainDb: -1)])));
        Assert.Contains("states C:left twice",
            Judge(new AgentProbeVariant(null, [Change(gainDb: -1), Change(delayMs: 1)])));
        Assert.Contains("changes at most 2 channels",
            Judge(new AgentProbeVariant(null,
                [Change(gainDb: -1), Change("B:left", gainDb: -1), Change("C:left", delayMs: 1)])));
        // The settings limits themselves, through the very path a proposal takes.
        Assert.Contains("Gain must be between", Judge(new AgentProbeVariant(null, [Change(gainDb: 500)])));
        Assert.Contains("Delay must be a multiple", Judge(new AgentProbeVariant(null, [Change(delayMs: 1.234)])));
        Assert.Contains("LinkwitzRiley offers slopes of",
            Judge(new AgentProbeVariant(null, [Change(crossover: Crossover("LinkwitzRiley", 2_000, 18))])));
        Assert.Contains("below the processor's Nyquist",
            Judge(new AgentProbeVariant(null, [Change(crossover: Crossover("LinkwitzRiley", 60_000, 24))])));
        Assert.Contains($"at most {AgentProtocol.MaxProbeVariantsPerImport} probe variants",
            Judge(Enumerable.Range(0, AgentProtocol.MaxProbeVariantsPerImport + 1)
                .Select(index => new AgentProbeVariant($"v{index}", [Change(gainDb: -0.1 * index)]))
                .ToArray()));
    }

    [Fact]
    public void Review_TakesTheDiagnosticPassAsOneVariant_WithNothingApplied()
    {
        // What the guide used to spend three replies and an undo on: read the
        // junction with the bank cleared, beside the junction as it stands.
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(Junction(variants: new AgentProbeVariant(
                "both banks cleared",
                [
                    Change("B:left", peq: new AgentPeqBank(0, [])),
                    Change("C:left", peq: new AgentPeqBank(0, []))
                ]))),
            Session()).Verdicts[0];

        Assert.True(verdict.Applicable);
        Assert.Equal(AgentVerdictStatus.Valid, verdict.Status);
    }

    [Fact]
    public void Review_RefusesAProbeThisBuildDoesNotCompute()
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(new ProbeOperation("op-1", "", "waterfall", "left:B-C", null)),
            Session()).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains("limits.probes", verdict.Message);
        Assert.Equal(["junction", "junctionDelay", "excessGroupDelay"], AgentProtocol.Probes);
    }

    [Fact]
    public void Review_LetsAReplyAskTheSameJunctionSeveralQuestions()
    {
        // A probe writes nothing, so a second one on the same junction is
        // another question about it — not a second run of an engine, which is
        // what the once-per-import rule exists to stop.
        AgentProposalReview review = AgentProposalValidator.Review(
            Proposal(
                Junction(),
                Junction("op-2", variants: new AgentProbeVariant("and this", [Change(delayMs: 3)])),
                Junction("op-3", "left:A-B",
                    new AgentProbeVariant("sub 1 dB down", [Change("A:mono", gainDb: -1)])),
                new ProbeOperation("op-4", "", AgentProtocol.JunctionDelayProbe, "left:B-C", null)),
            Session());

        Assert.All(review.Verdicts, verdict => Assert.True(verdict.Applicable));
    }

    [Fact]
    public void Review_BudgetsTheVariantsOverTheWholeImport_NotPerProbe()
    {
        // The budget is the user's wait and the size of the text they paste, so
        // it cannot be dodged by splitting one long list into two probes.
        AgentProbeVariant[] Variants(int count, int from) =>
            Enumerable.Range(from, count)
                .Select(index => new AgentProbeVariant($"v{index}", [Change(gainDb: -0.1 * index)]))
                .ToArray();

        int budget = AgentProtocol.MaxProbeVariantsPerImport;
        AgentProposalReview review = AgentProposalValidator.Review(
            Proposal(
                Junction(variants: Variants(budget - 1, 0)),
                Junction("op-2", variants: Variants(2, 100)),
                Junction("op-3", variants: Variants(1, 200))),
            Session());

        Assert.True(review.Verdicts[0].Applicable);
        // Two more would pass the budget; one still fits after it.
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Contains($"{budget - 1} are already asked for above", review.Verdicts[1].Message);
        Assert.True(review.Verdicts[2].Applicable);
    }

    [Fact]
    public void ApplyProbeChange_WritesTheVariantOntoTheCopy_AndLeavesTheChannelAlone()
    {
        AgentSessionSnapshot session = Session();
        AgentChannelSnapshot channel = session.Find("C:left")!;
        VirtualCrossoverChannelSettings copy = AgentOperations.CloneEditable(channel.Settings);

        string? problem = AgentProposalValidator.ApplyProbeChange(
            Change(
                gainDb: -3.5,
                delayMs: 2.5,
                invertPolarity: true,
                crossover: Crossover("LinkwitzRiley", 2_500, 36),
                peq: new AgentPeqBank(-1, [new AgentPeqBand("Peaking", 900, 2, -3)])),
            session,
            copy);

        Assert.Null(problem);
        Assert.Equal(-3.5, copy.GainDb);
        Assert.Equal(2.5, copy.DelayMs);
        Assert.True(copy.InvertPolarity);
        Assert.Equal(CrossoverKind.HighPass, copy.CrossoverKind);
        Assert.Equal(Edge(CrossoverFilterFamily.LinkwitzRiley, 2_500, 36), copy.HighPassEdge);
        Assert.Equal(-1, copy.PeqPreampDb);
        Assert.Single(copy.PeqBands);
        // The channel the copy came from is untouched, which is the whole point.
        Assert.Equal(0, channel.Settings.GainDb);
        Assert.Equal(0, channel.Settings.DelayMs);
        Assert.False(channel.Settings.InvertPolarity);
        Assert.Equal(Edge(CrossoverFilterFamily.Butterworth, 2_000, 48), channel.Settings.HighPassEdge);
        Assert.Empty(channel.Settings.PeqBands);
    }
}
