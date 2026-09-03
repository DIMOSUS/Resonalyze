using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// The review of a <c>tuneJunction</c> request: the junction it names must be
/// one the package could have printed — two measured blocks on that side, in
/// the sum, in one group, neighbours along the spectrum that hand over to each
/// other — and its inputs must be ones the tuner can use. What it writes over,
/// and what writes over it, follows from the crossover being one filter for
/// both sides.
/// </summary>
public sealed class AgentJunctionTuneReviewTests
{
    private const string Package = "11111111-1111-1111-1111-111111111111";

    private static CrossoverEdge Edge(CrossoverFilterFamily family, double hz, int slope) =>
        new(family, hz, slope);

    // A three-way front with a subwoofer, and a rear fill: A sub (mono, LP 80),
    // B mid (BP 80–2000), C tweeter (HP 2000 at 48 dB/oct), D rear (HP 100,
    // its own group). Every stereo block carries both sides.
    private static AgentSessionSnapshot Session(
        bool cMeasured = true, bool cEnabled = true, bool bBypass = false, string? lastPackageId = Package)
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
                new AgentChannelSnapshot("B", AgentChannelSide.Left, B(), true, [], Bypass: bBypass),
                new AgentChannelSnapshot("B", AgentChannelSide.Right, B(), true, [], Bypass: bBypass),
                new AgentChannelSnapshot("C", AgentChannelSide.Left, C(), cMeasured, [], Enabled: cEnabled),
                new AgentChannelSnapshot("C", AgentChannelSide.Right, C(), cMeasured, [], Enabled: cEnabled),
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

    private static TuneJunctionOperation Tune(
        string junctionId, string id = "op-1", double? minHz = null, double? maxHz = null,
        IReadOnlyList<string>? families = null, IReadOnlyList<int>? slopes = null,
        bool? independentSlopes = null) =>
        new(id, "the right C-D will not sum", junctionId, minHz, maxHz, families, slopes, independentSlopes);

    [Fact]
    public void Review_TakesAJunctionThePackageCouldHavePrinted()
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(Tune("left:B-C")), Session()).Verdicts[0];

        Assert.True(verdict.Applicable);
        Assert.Equal(AgentVerdictStatus.Warning, verdict.Status);
        Assert.Equal("B/C", verdict.ChannelLabel);
        Assert.Equal("Junction tune", verdict.Parameter);
        Assert.Equal("B: LP BW48 2000 Hz; C: HP BW48 2000 Hz", verdict.Current);
        Assert.Equal("tune the junction with the tuner's own settings", verdict.Proposed);
        Assert.Contains("both sides", verdict.Message);
        Assert.Contains("keeps the current crossover unless", verdict.Message);
    }

    [Fact]
    public void Review_StatesOnlyTheInputsTheReplyGave()
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(Tune("right:B-C", minHz: 1_400, maxHz: 2_800,
                families: ["Butterworth", "LinkwitzRiley"], slopes: [36, 48], independentSlopes: false)),
            Session()).Verdicts[0];

        Assert.True(verdict.Applicable);
        Assert.Equal(
            "tune the junction: 1400 Hz to 2800 Hz, Butterworth/LinkwitzRiley, 36/48 dB/oct, " +
            "one slope for both edges",
            verdict.Proposed);
    }

    [Fact]
    public void Review_NamesTheSubJunction_ThroughTheMonoBlock()
    {
        // A mono subwoofer plays on both sides; the junction under the mid is
        // real on either.
        AgentProposalReview review = AgentProposalValidator.Review(
            Proposal(Tune("left:A-B"), Tune("right:A-B", "op-2")), Session());

        Assert.All(review.Verdicts, verdict => Assert.True(verdict.Applicable));
        Assert.Equal("A: LP LR24 80 Hz; B: HP LR24 80 Hz", review.Verdicts[0].Current);
    }

    [Theory]
    [InlineData("B-C", "is not a junction id")]
    [InlineData("left:B", "is not a junction id")]
    [InlineData("mono:A-B", "is not a junction id")]
    [InlineData("left:B-X", "Unknown junction")]
    [InlineData("left:A-C", "not neighbours along the spectrum")]
    [InlineData("left:C-B", "plays above")]
    [InlineData("left:C-D", "different groups")]
    public void Review_RefusesAJunctionTheSessionDoesNotHave(string junctionId, string reason)
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(Tune(junctionId)), Session()).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains(reason, verdict.Message);
    }

    [Fact]
    public void Review_RefusesAJunctionWhoseBlockCannotBeRead()
    {
        Assert.Contains("has no measurement", AgentProposalValidator.Review(
            Proposal(Tune("left:B-C")), Session(cMeasured: false)).Verdicts[0].Message);
        Assert.Contains("is disabled", AgentProposalValidator.Review(
            Proposal(Tune("left:B-C")), Session(cEnabled: false)).Verdicts[0].Message);
        Assert.Contains("is bypassed", AgentProposalValidator.Review(
            Proposal(Tune("left:B-C")), Session(bBypass: true)).Verdicts[0].Message);
    }

    [Theory]
    [InlineData(2_800.0, 1_400.0, null, null, "lower edge must not sit above")]
    [InlineData(10.0, null, null, null, "junction window's lower edge must sit between")]
    [InlineData(null, 60_000.0, null, null, "junction window's upper edge must sit between")]
    [InlineData(null, null, "Elliptic", null, "Unknown crossover family")]
    [InlineData(null, null, "LinkwitzRiley", 18, "No admitted family offers a 18 dB/oct")]
    public void Review_HoldsTheInputsToWhatTheTunerCanUse(
        double? minHz, double? maxHz, string? family, int? slope, string reason)
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(Tune("left:B-C", minHz: minHz, maxHz: maxHz,
                families: family == null ? null : [family],
                slopes: slope == null ? null : [slope.Value])),
            Session()).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains(reason, verdict.Message);
    }

    [Fact]
    public void Review_RefusesAJunctionTuneBesideTheWizard_WhichRewritesEveryJunction()
    {
        AgentProposalReview review = AgentProposalValidator.Review(
            Proposal(
                new RunAutoCrossoverOperation("op-1", "split them"),
                Tune("left:B-C", "op-2")),
            Session());

        Assert.True(review.Verdicts[0].Applicable);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Equal(
            "Would be overwritten by Auto crossover (op-1), which rewrites every junction of the chain.",
            review.Verdicts[1].Message);
    }

    [Fact]
    public void Review_RefusesAHandWrittenCrossoverOnEitherBlockOfTheJunction_EitherSide()
    {
        AgentCrossover current = new("HighPass", new AgentCrossoverEdge("Butterworth", 2_000, 48, null), null);
        AgentCrossover proposed = new("HighPass", new AgentCrossoverEdge("Butterworth", 2_200, 48, null), null);
        AgentProposalReview review = AgentProposalValidator.Review(
            Proposal(
                Tune("left:B-C"),
                new SetCrossoverOperation("op-2", "C:right", "", current, proposed),
                new SetGainOperation("op-3", "C:right", "", 0, -1.5),
                new SetCrossoverOperation("op-4", "D:left", "",
                    new AgentCrossover("HighPass", new AgentCrossoverEdge("LinkwitzRiley", 100, 24, null), null),
                    new AgentCrossover("HighPass", new AgentCrossoverEdge("LinkwitzRiley", 120, 24, null), null))),
            Session());

        Assert.True(review.Verdicts[0].Applicable);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Equal("Would be overwritten by Junction tune (op-1).", review.Verdicts[1].Message);
        // The tune leaves gains alone, and blocks outside the junction.
        Assert.True(review.Verdicts[2].Applicable);
        Assert.True(review.Verdicts[3].Applicable);
    }

    [Fact]
    public void Review_RunsATuneOncePerJunction_AndOtherJunctionsBesideIt()
    {
        AgentProposalReview review = AgentProposalValidator.Review(
            Proposal(Tune("left:B-C"), Tune("left:B-C", "op-2"), Tune("left:A-B", "op-3")),
            Session());

        Assert.True(review.Verdicts[0].Applicable);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Contains("Already requested by op-1", review.Verdicts[1].Message);
        Assert.True(review.Verdicts[2].Applicable);
    }

    [Fact]
    public void Review_RefusesTheTune_LikeEveryEngine_OnAPackageTheSessionCannotVouchFor()
    {
        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            Proposal(Tune("left:B-C")), Session(lastPackageId: null)).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains("copy a new package", verdict.Message);
    }

    [Fact]
    public void JunctionIds_ParseWhatThePackagePrints_AndNothingElse()
    {
        Assert.True(AgentJunctionIds.TryParse("left:B-C", out AgentChannelSide side, out string lower, out string upper));
        Assert.Equal(AgentChannelSide.Left, side);
        Assert.Equal("B", lower);
        Assert.Equal("C", upper);
        Assert.Equal("right:AA-AB", AgentJunctionIds.Format(AgentChannelSide.Right, "AA", "AB"));
        Assert.True(AgentJunctionIds.TryParse("right:AA-AB", out side, out lower, out upper));
        Assert.Equal((AgentChannelSide.Right, "AA", "AB"), (side, lower, upper));

        Assert.False(AgentJunctionIds.TryParse(null, out _, out _, out _));
        Assert.False(AgentJunctionIds.TryParse("", out _, out _, out _));
        Assert.False(AgentJunctionIds.TryParse("B-C", out _, out _, out _));
        Assert.False(AgentJunctionIds.TryParse("left:B", out _, out _, out _));
        Assert.False(AgentJunctionIds.TryParse("left:-C", out _, out _, out _));
        Assert.False(AgentJunctionIds.TryParse("left:B-", out _, out _, out _));
        Assert.False(AgentJunctionIds.TryParse("up:B-C", out _, out _, out _));
    }

    [Fact]
    public void DefaultJunctionWindow_IsHalfAnOctaveEachWay_InsideTheFields()
    {
        // 1414 and 2828 Hz, on the wizard's 50 Hz lattice above 1 kHz.
        (double minHz, double maxHz) = AgentProposalValidator.DefaultJunctionWindow(2_000);
        Assert.Equal(1_400, minHz);
        Assert.Equal(2_850, maxHz);
        Assert.Equal((250, 490), AgentProposalValidator.DefaultJunctionWindow(350));
        Assert.Equal(20, AgentProposalValidator.DefaultJunctionWindow(25).MinHz);
        Assert.Equal(20_000, AgentProposalValidator.DefaultJunctionWindow(18_000).MaxHz);
    }
}
