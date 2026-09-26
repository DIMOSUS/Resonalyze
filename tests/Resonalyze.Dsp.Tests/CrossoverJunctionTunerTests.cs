using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class CrossoverJunctionTunerTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void ReplacingAnEdge_MovesAPhaseRotationStatedAtIt_AndLeavesTheOtherOneAlone()
    {
        // A phase control stated at the moved corner moves its all-pass with it; judging against the old one scores an unbuildable filter.
        var subwoofer = new DspChannelChain(
            Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)),
            PhaseRotation: new PhaseRotationSpec(90, 80, ReferenceIsLowPass: true));

        DspChannelChain moved = CrossoverJunctionTuner.WithLowPass(
            subwoofer, new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 65, 24));

        Assert.Equal(65, moved.PhaseRotation.ReferenceHz);
        Assert.Equal(90, moved.PhaseRotation.Degrees);

        // A midbass states its angle at its HIGH-pass, so moving its low-pass leaves the rotation alone.
        var midbass = new DspChannelChain(
            Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 350, 24),
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)),
            PhaseRotation: new PhaseRotationSpec(90, 80, ReferenceIsLowPass: false));

        DspChannelChain untouched = CrossoverJunctionTuner.WithLowPass(
            midbass, new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 300, 24));

        Assert.Equal(80, untouched.PhaseRotation.ReferenceHz);

        DspChannelChain highMoved = CrossoverJunctionTuner.WithHighPass(
            midbass, new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 65, 24));

        Assert.Equal(65, highMoved.PhaseRotation.ReferenceHz);
    }

    [Fact]
    public void ReplacingAnEdge_MovesAReferenceSittingOnAFilterTheChannelDoesNotYetUse()
    {
        // No low-pass yet: the spec must say which corner the rotation follows, or candidates keep the old all-pass frequency.
        var subwoofer = new DspChannelChain(
            Crossover: CrossoverSpec.Off,
            PhaseRotation: new PhaseRotationSpec(90, 80, ReferenceIsLowPass: true));

        DspChannelChain moved = CrossoverJunctionTuner.WithLowPass(
            subwoofer, new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 65, 24));

        Assert.Equal(65, moved.PhaseRotation.ReferenceHz);
        Assert.Equal(CrossoverKind.LowPass, moved.Crossover!.Kind);

        var tweeter = new DspChannelChain(
            Crossover: CrossoverSpec.Off,
            PhaseRotation: new PhaseRotationSpec(90, 2_000));

        DspChannelChain highMoved = CrossoverJunctionTuner.WithHighPass(
            tweeter, new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_500, 24));

        Assert.Equal(3_500, highMoved.PhaseRotation.ReferenceHz);
    }

    private static Complex[] Impulse(int position = 480, double amplitude = 1.0)
    {
        var impulse = new Complex[16_384];
        impulse[position] = amplitude;
        return impulse;
    }

    private static CrossoverEdge Edge(CrossoverFilterFamily family, double hz, int slope) =>
        new(family, hz, slope);

    private static DspChannelChain LowPassChain(CrossoverEdge edge, double delayMs = 0, EqualizationCurve? peq = null) =>
        new(Crossover: new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge), DelayMs: delayMs, Peq: peq);

    private static DspChannelChain HighPassChain(
        CrossoverEdge edge, double delayMs = 0, EqualizationCurve? peq = null) =>
        new(Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge), DelayMs: delayMs, Peq: peq);

    private static JunctionTuneSide Side(
        string name, DspChannelChain lower, DspChannelChain upper) =>
        new(name, Impulse(), lower, Impulse(), upper, SampleRate);

    private static JunctionTuneOptions Options(
        double minHz, double maxHz,
        IReadOnlyList<int>? slopes = null,
        bool independentSlopes = true,
        params CrossoverFilterFamily[] families) =>
        new(
            families.Length > 0 ? families : [CrossoverFilterFamily.LinkwitzRiley],
            slopes,
            minHz,
            maxHz,
            independentSlopes,
            SampleRate);

    [Fact]
    [Trait("Category", "Slow")]
    public void Tune_KeepsAJunctionThatIsAlreadyTextbook()
    {
        // A Linkwitz-Riley pair at one corner is lossless: nothing beats it by the keep margin.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side("left", LowPassChain(lr), HighPassChain(lr))],
            Options(700, 1_400));

        Assert.False(result.Changed);
        Assert.InRange(result.Current.Sides[0].LossDb, -0.2, 0.0);
        Assert.InRange(result.Current.Sides[0].RippleDb, 0.0, 0.3);
        Assert.Equal(lr, result.Current.LowerLowPass);
        Assert.Equal(lr, result.Current.UpperHighPass);
        Assert.True(result.CandidatesEvaluated > 10);
        Assert.Equal(CrossoverJunctionTuner.RunnersUpReported, result.RunnersUp.Count);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Tune_ClosesAWideGentleOverlap_OntoOneCorner()
    {
        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side("left",
                LowPassChain(Edge(CrossoverFilterFamily.Butterworth, 700, 12)),
                HighPassChain(Edge(CrossoverFilterFamily.Butterworth, 1_400, 12)))],
            Options(500, 2_000));

        Assert.True(result.Changed);
        Assert.True(result.Best.RankingScoreDb < result.Current.RankingScoreDb - CrossoverJunctionTuner.DefaultKeepMarginDb);
        Assert.True(result.Best.ScoreDb <= result.Current.ScoreDb);
        Assert.True(result.Best.Sides[0].RippleDb < result.Current.Sides[0].RippleDb);
        // An octave outside the 500-2000 Hz window.
        Assert.Equal(250, result.RankingBandLowHz);
        Assert.Equal(4_000, result.RankingBandHighHz);
        Assert.Equal(2, result.Best.Sides.Count + result.Best.RankingSides.Count);
        Assert.Equal(CrossoverFilterFamily.LinkwitzRiley, result.Best.LowerLowPass!.Value.Family);
        Assert.Equal(result.Best.LowerLowPass!.Value.FrequencyHz, result.Best.UpperHighPass!.Value.FrequencyHz);
        Assert.InRange(result.Best.LowerLowPass!.Value.FrequencyHz, 500, 2_000);
        Assert.Equal(
            CrossoverAutoSetup.RoundToLattice(result.Best.LowerLowPass!.Value.FrequencyHz),
            result.Best.LowerLowPass!.Value.FrequencyHz);
        Assert.Single(result.CurrentAfterDelay);
        Assert.Single(result.BestAfterDelay);
        Assert.Equal("left", result.BestAfterDelay[0].Side);
    }

    [Fact]
    public void Tune_HonoursTheSlopesAndTheWindowItWasGiven()
    {
        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side("left",
                LowPassChain(Edge(CrossoverFilterFamily.Butterworth, 700, 12)),
                HighPassChain(Edge(CrossoverFilterFamily.Butterworth, 1_400, 12)))],
            Options(900, 1_100, slopes: [48], independentSlopes: false,
                CrossoverFilterFamily.Butterworth, CrossoverFilterFamily.Bessel));

        foreach (JunctionTuneCandidate candidate in result.RunnersUp.Prepend(result.Best))
        {
            Assert.Equal(48, candidate.LowerLowPass!.Value.SlopeDbPerOctave);
            Assert.Equal(48, candidate.UpperHighPass!.Value.SlopeDbPerOctave);
            Assert.InRange(candidate.LowerLowPass!.Value.FrequencyHz, 900, 1_100);
            Assert.NotEqual(CrossoverFilterFamily.LinkwitzRiley, candidate.LowerLowPass!.Value.Family);
            Assert.Equal(candidate.LowerLowPass!.Value.Family, candidate.UpperHighPass!.Value.Family);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Tune_ReadsEverySide_AfterItsBestDelay_AndRanksOnTheirMean()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [
                Side("left", LowPassChain(lr), HighPassChain(lr)),
                Side("right", LowPassChain(lr), HighPassChain(lr, delayMs: 0.5))
            ],
            Options(700, 1_400));

        Assert.Equal(["left", "right"], result.Current.Sides.Select(side => side.Side));
        Assert.Equal(result.Current.Sides[0].LossDb, result.Current.Sides[1].LossDb, 1);
        Assert.Equal(
            result.Current.Sides.Average(side => side.ScoreDb), result.Current.ScoreDb, 9);
        JunctionTuneAlignment right = Assert.Single(
            result.CurrentAfterDelay, alignment => alignment.Side == "right");
        Assert.InRange(right.ExtraDelayMs, -0.6, -0.4);
        Assert.False(result.Changed);
    }

    [Fact]
    public void WithLowPass_AndWithHighPass_ReplaceOnlyTheFacingEdge()
    {
        var peq = new EqualizationCurve([new PeqBand(820, 2.1, -2.4)], -1.0);
        var lower = new DspChannelChain(
            GainDb: -3, DelayMs: 1.25, InvertPolarity: true,
            Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                Edge(CrossoverFilterFamily.Butterworth, 1_800, 48),
                Edge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)),
            Peq: peq);
        var upper = new DspChannelChain(GainDb: -6, DelayMs: 0.3);

        DspChannelChain lowerTuned = CrossoverJunctionTuner.WithLowPass(
            lower, Edge(CrossoverFilterFamily.Bessel, 1_600, 36));
        DspChannelChain upperTuned = CrossoverJunctionTuner.WithHighPass(
            upper, Edge(CrossoverFilterFamily.Bessel, 1_600, 36));

        Assert.Equal(lower with { Crossover = null }, lowerTuned with { Crossover = null });
        Assert.Equal(CrossoverKind.BandPass, lowerTuned.Crossover!.Kind);
        Assert.Equal(Edge(CrossoverFilterFamily.LinkwitzRiley, 80, 24), lowerTuned.Crossover.HighPassEdge);
        Assert.Equal(Edge(CrossoverFilterFamily.Bessel, 1_600, 36), lowerTuned.Crossover.LowPassEdge);
        Assert.Same(peq, lowerTuned.Peq);
        Assert.Equal(upper with { Crossover = null }, upperTuned with { Crossover = null });
        Assert.Equal(CrossoverKind.HighPass, upperTuned.Crossover!.Kind);
        Assert.Equal(Edge(CrossoverFilterFamily.Bessel, 1_600, 36), upperTuned.Crossover.HighPassEdge);
        Assert.Null(upperTuned.Crossover.LowPassEdge);
    }

    [Fact]
    public void Probe_ReadsEveryVariantOnItsOwnBandAndOnTheSharedOne_AndWritesNothing()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        DspChannelChain lowerChain = LowPassChain(lr);
        DspChannelChain upperChain = HighPassChain(lr);
        JunctionTuneSide side = Side("left", lowerChain, upperChain);
        CrossoverEdge wide = Edge(CrossoverFilterFamily.Butterworth, 500, 12);
        var variants = new List<JunctionProbeVariant>
        {
            new("current", [new JunctionProbeChains(lowerChain, upperChain)]),
            new("BW12 500", [new JunctionProbeChains(
                CrossoverJunctionTuner.WithLowPass(lowerChain, wide),
                CrossoverJunctionTuner.WithHighPass(upperChain, wide))])
        };

        JunctionProbeResult result = CrossoverJunctionTuner.Probe([side], SampleRate, variants);

        Assert.Equal(["current", "BW12 500"], result.Entries.Select(entry => entry.Label));
        Assert.Equal(250, result.SharedBandLowHz);
        Assert.Equal(2_000, result.SharedBandHighHz);
        Assert.Equal((500.0, 2_000.0), (result.Entries[0].BandLowHz, result.Entries[0].BandHighHz));
        Assert.Equal((250.0, 1_000.0), (result.Entries[1].BandLowHz, result.Entries[1].BandHighHz));
        foreach (JunctionProbeEntry entry in result.Entries)
        {
            Assert.Null(entry.Unavailable);
            Assert.Single(entry.Sides);
            Assert.Single(entry.SharedBandSides);
            Assert.Single(entry.AfterDelay);
            Assert.Single(entry.Phase);
            Assert.NotEqual(entry.Sides[0].LossDb, entry.SharedBandSides[0].LossDb);
        }
        Assert.InRange(result.Entries[0].Sides[0].LossDb, -0.2, 0.0);
        Assert.NotNull(result.Entries[0].Phase[0].Result);
        Assert.InRange(result.Entries[0].Phase[0].Result!.CurrentScore, 0.9, 1.0);
        Assert.True(result.Entries[1].Sides[0].RippleDb > result.Entries[0].Sides[0].RippleDb);
        Assert.Equal(lr, side.LowerChain.Crossover!.LowPassEdge);
        Assert.Equal(lr, side.UpperChain.Crossover!.HighPassEdge);
    }

    [Fact]
    public void Probe_TakesTheTwoEdgesApart_AndReadsBetweenThem()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 4_000, 24);
        DspChannelChain lowerChain = LowPassChain(lr);
        DspChannelChain upperChain = HighPassChain(lr);
        JunctionTuneSide side = Side("left", lowerChain, upperChain);
        CrossoverEdge low = Edge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
        CrossoverEdge high = Edge(CrossoverFilterFamily.Butterworth, 8_000, 48);

        JunctionProbeResult result = CrossoverJunctionTuner.Probe([side], SampleRate,
            [
                new JunctionProbeVariant("current", [new JunctionProbeChains(lowerChain, upperChain)]),
                new JunctionProbeVariant("apart", [new JunctionProbeChains(
                    CrossoverJunctionTuner.WithLowPass(lowerChain, low),
                    CrossoverJunctionTuner.WithHighPass(upperChain, high))])
            ]);

        Assert.Equal(low, result.Entries[1].LowerLowPass);
        Assert.Equal(high, result.Entries[1].UpperHighPass);
        // The band centres on the handover between the edges (√(2000·8000) = 4000).
        Assert.Equal(4_000, result.Entries[1].CornerHz, 6);
        Assert.Equal((2_000.0, 8_000.0), (result.Entries[1].BandLowHz, result.Entries[1].BandHighHz));
        Assert.True(result.SharedBandHighHz >= 8_000 * 2 - 1e-6);
        Assert.Equal(1_000, result.SharedBandLowHz);
        Assert.All(result.Entries, entry => Assert.Null(entry.Unavailable));
        Assert.True(result.Entries[1].Sides[0].RippleDb > result.Entries[0].Sides[0].RippleDb);
    }

    [Fact]
    public void Probe_ReadsABankChangeWithoutApplyingIt()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        var bank = new EqualizationCurve([new PeqBand(1_000, 1.0, -9)], 0);
        DspChannelChain lowerChain = LowPassChain(lr, peq: bank);
        DspChannelChain upperChain = HighPassChain(lr);
        JunctionTuneSide side = Side("left", lowerChain, upperChain);

        JunctionProbeResult result = CrossoverJunctionTuner.Probe([side], SampleRate,
            [
                new JunctionProbeVariant("current", [new JunctionProbeChains(lowerChain, upperChain)]),
                new JunctionProbeVariant("cleared",
                    [new JunctionProbeChains(lowerChain with { Peq = null }, upperChain)])
            ]);

        Assert.Equal(result.Entries[0].BandLowHz, result.Entries[1].BandLowHz);
        Assert.True(
            result.Entries[1].Sides[0].RippleDb < result.Entries[0].Sides[0].RippleDb,
            "clearing the bell should flatten the junction's own sum");
        Assert.Same(bank, side.LowerChain.Peq);
    }

    [Fact]
    public void ProbeAlignment_FindsTheDelayThatWouldBeApplied_WithoutApplyingIt()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneSide side = Side("left", LowPassChain(lr), HighPassChain(lr, delayMs: 0.4));

        IReadOnlyList<JunctionDelayProbeSide> read =
            CrossoverJunctionTuner.ProbeAlignment([side], SampleRate);

        JunctionDelayProbeSide left = Assert.Single(read);
        Assert.Null(left.Unavailable);
        Assert.Equal((500.0, 2_000.0), (left.BandLowHz, left.BandHighHz));
        Assert.NotEmpty(left.Candidates);
        JunctionDelayProbeCandidate chosen = Assert.Single(left.Candidates, candidate => candidate.Chosen);
        Assert.InRange(chosen.ExtraDelayMs, -0.5, -0.3);
        Assert.False(chosen.InvertUpper);
        Assert.InRange(chosen.LossDb, -0.3, 0.0);
        Assert.Equal(0.4, side.UpperChain.DelayMs);
    }

    [Fact]
    public void ProbeAlignment_ReportsTheUpperChannelsRESULTINGPolarity()
    {
        // The report states what the CHANNEL ends up as, not 'flip the response', or a reply proposes the opposite polarity.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneSide inverted = Side(
            "left", LowPassChain(lr), HighPassChain(lr) with { InvertPolarity = true });

        JunctionDelayProbeCandidate chosen = Assert.Single(
            Assert.Single(CrossoverJunctionTuner.ProbeAlignment([inverted], SampleRate)).Candidates,
            candidate => candidate.Chosen);

        Assert.False(chosen.InvertUpper);
        Assert.InRange(chosen.LossDb, -0.3, 0.0);

        JunctionDelayProbeCandidate normal = Assert.Single(
            Assert.Single(CrossoverJunctionTuner.ProbeAlignment(
                [Side("left", LowPassChain(lr), HighPassChain(lr))], SampleRate)).Candidates,
            candidate => candidate.Chosen);
        Assert.False(normal.InvertUpper);
    }

    [Fact]
    public void AStatedAcousticSlope_PicksTheElectricalFilterThatLandsIt()
    {
        // A perfect impulse is a flat driver: acoustic is electrical.
        CrossoverEdge steep = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 48);
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [12, 24, 48], independentSlopes: false,
            CrossoverFilterFamily.LinkwitzRiley) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side("left", LowPassChain(steep), HighPassChain(steep))], options);

        Assert.True(result.Changed, "an LR48 pair does not answer a request for acoustic LR24.");
        Assert.Equal(24, result.Best.LowerLowPass!.Value.SlopeDbPerOctave);
        Assert.Equal(24, result.Best.UpperHighPass!.Value.SlopeDbPerOctave);

        JunctionAcousticFit fit = result.Best.Sides[0].Acoustic!;
        Assert.NotNull(fit.TargetSlopeDbPerOctave);
        Assert.Equal(fit.TargetSlopeDbPerOctave!.Value, fit.LowerSlopeDbPerOctave!.Value, 1.5);
        Assert.Equal(fit.TargetSlopeDbPerOctave!.Value, fit.UpperSlopeDbPerOctave!.Value, 1.5);
        Assert.InRange(fit.ChargeDb, 0, 0.5);
        Assert.True(
            result.Current.Sides[0].Acoustic!.ChargeDb > fit.ChargeDb + 1,
            "the steeper pair should be charged for the skirt the EQ could not lift.");
        JunctionDriverSlopes driver = Assert.Single(result.DriverSlopes);
        Assert.True(CrossoverJunctionTuner.IsReachable(
            driver.LowerDbPerOctave, fit.TargetSlopeDbPerOctave));
    }

    [Fact]
    public void ADriverAlreadyFallingSteeperThanAsked_SaysSo_RatherThanPretending()
    {
        // The drivers' own roll-off is an LR24 at 1 kHz.
        CrossoverEdge own = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        Complex[] rolledOff = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), LowPassChain(own), SampleRate, SampleRate);
        Complex[] rolledOn = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), HighPassChain(own), SampleRate, SampleRate);
        var bare = new DspChannelChain(Crossover: CrossoverSpec.Off);
        var side = new JunctionTuneSide("left", rolledOff, bare, rolledOn, bare, SampleRate);
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [12], independentSlopes: false,
            CrossoverFilterFamily.LinkwitzRiley) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 12)
        };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune([side], options);

        JunctionDriverSlopes driver = Assert.Single(result.DriverSlopes);
        double asked = result.Best.Sides[0].Acoustic!.TargetSlopeDbPerOctave!.Value;
        Assert.True(
            driver.LowerDbPerOctave > asked + CrossoverJunctionTuner.SlopeReachToleranceDbPerOctave,
            $"the driver falls {driver.LowerDbPerOctave:0.0} dB/oct where {asked:0.0} was asked.");
        Assert.False(CrossoverJunctionTuner.IsReachable(driver.LowerDbPerOctave, asked));
        Assert.False(CrossoverJunctionTuner.IsReachable(driver.UpperDbPerOctave, asked));
        Assert.False(CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb));
        Assert.True(result.ClosestAcousticCostDb > CrossoverJunctionTuner.AcousticReachedCostDb);
        Assert.True(CrossoverJunctionTuner.IsReachable(null, asked));
        Assert.True(CrossoverJunctionTuner.IsReachable(driver.LowerDbPerOctave, null));
    }

    [Fact]
    public void AnAcousticSlopeTheSumCannotAfford_IsNotBought()
    {
        // A Bessel pair draws the goal exactly but sums outside the corridor at any delay.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [24], independentSlopes: false,
            CrossoverFilterFamily.LinkwitzRiley, CrossoverFilterFamily.Bessel) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.Bessel, 24)
        };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side("left", LowPassChain(lr), HighPassChain(lr))], options);

        Assert.Equal(CrossoverFilterFamily.LinkwitzRiley, result.Best.LowerLowPass!.Value.Family);
        Assert.True(
            result.Best.Sides[0].Acoustic!.ChargeDb > 0.5,
            "the answer cannot draw the asked edge, and the report must say so rather than hide it.");
        Assert.True(result.Best.RankingScoreDb <= result.Current.RankingScoreDb + options.SumSlackDb);

        Assert.True(CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb));
        Assert.True(
            result.ClosestAcousticCostDb < result.Best.AcousticCostDb - 0.5,
            $"closest {result.ClosestAcousticCostDb:0.00} dB against chosen {result.Best.AcousticCostDb:0.00} dB.");
    }

    [Fact]
    public void AStatedSlopeThatSumsAsWellOnceReAligned_IsTaken()
    {
        // A 12 dB/oct pair sums flat only with the upper channel inverted.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [12, 24], independentSlopes: false,
            CrossoverFilterFamily.LinkwitzRiley, CrossoverFilterFamily.Butterworth) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 12)
        };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side("left", LowPassChain(lr), HighPassChain(lr))], options);

        Assert.True(result.Changed);
        Assert.Equal(12, result.Best.LowerLowPass!.Value.SlopeDbPerOctave);
        Assert.True(CrossoverJunctionTuner.WasAcousticTargetReached(result.Best.AcousticCostDb));
        Assert.True(Assert.Single(result.BestAfterDelay).InvertUpper);
    }

    [Fact]
    public void TheAcousticReadIgnoresTheBankThatIsAboutToBeRefitted_WhileTheSumStillHearsIt()
    {
        // A bell at the junction: the acoustic read drops the PEQ, the summation keeps it.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        var bell = new EqualizationCurve([new PeqBand(1_000, 1.0, -6)], preampDb: 0);
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [24], independentSlopes: false,
            CrossoverFilterFamily.LinkwitzRiley) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };

        JunctionTuneResult plain = CrossoverJunctionTuner.Tune(
            [Side("left", LowPassChain(lr), HighPassChain(lr))], options);
        JunctionTuneResult equalised = CrossoverJunctionTuner.Tune(
            [Side("left", LowPassChain(lr, peq: bell), HighPassChain(lr))], options);

        Assert.Equal(
            plain.Current.Sides[0].Acoustic!.ChargeDb,
            equalised.Current.Sides[0].Acoustic!.ChargeDb,
            1e-9);
        Assert.Equal(
            plain.Current.Sides[0].Acoustic!.LowerSlopeDbPerOctave!.Value,
            equalised.Current.Sides[0].Acoustic!.LowerSlopeDbPerOctave!.Value,
            1e-9);
        Assert.NotEqual(
            plain.Current.Sides[0].RippleDb,
            equalised.Current.Sides[0].RippleDb,
            1e-3);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void ANarrowNotchInThePlant_CostsFarLessThanASlopeThatIsSystematicallyWrong()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [24], independentSlopes: false,
            CrossoverFilterFamily.LinkwitzRiley) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };

        double flat = ChargeWithPlant(lr, options, Plant(notchHz: null, tiltDbPerOctave: 0));
        double notched = ChargeWithPlant(lr, options, Plant(notchHz: 1_150, tiltDbPerOctave: 0));
        double tilted = ChargeWithPlant(lr, options, Plant(notchHz: null, tiltDbPerOctave: -1.5));

        Assert.Equal(flat, notched, 0.2);
        Assert.True(
            tilted > notched + 0.5,
            $"the tilt charged {tilted:0.00} dB against the notch's {notched:0.00} dB (flat reads {flat:0.00}).");
    }

    [Fact]
    public void ADeficitAcrossTheWholeSkirt_IsNotTrimmedAway()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [24], independentSlopes: false,
            CrossoverFilterFamily.LinkwitzRiley) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };

        double flat = ChargeWithPlant(lr, options, Plant(notchHz: null, tiltDbPerOctave: 0));
        double steep = ChargeWithPlant(lr, options, Plant(notchHz: null, tiltDbPerOctave: -6));

        Assert.True(steep > flat + 1.5, $"a whole-skirt error charged only {steep:0.00} dB against {flat:0.00}.");
    }

    // A flat plant with one narrow dip or one systematic tilt through the junction; levels are arbitrary.
    private static List<SignalPoint> Plant(double? notchHz, double tiltDbPerOctave)
    {
        var curve = new List<SignalPoint>();
        for (double hz = 250; hz <= 4_000; hz *= Math.Pow(2, 1.0 / 48.0))
        {
            double db = tiltDbPerOctave * Math.Log2(hz / 1_000);
            if (notchHz is { } centre && Math.Abs(Math.Log2(hz / centre)) < 1.0 / 24.0)
            {
                db -= 12;
            }

            curve.Add(new SignalPoint(hz, db));
        }

        return curve;
    }

    private static double ChargeWithPlant(
        CrossoverEdge current, JunctionTuneOptions options, List<SignalPoint> plant)
    {
        var side = new JunctionTuneSide(
            "left", Impulse(), LowPassChain(current), Impulse(), HighPassChain(current), SampleRate,
            plant, plant);
        return CrossoverJunctionTuner.Tune([side], options).Current.Sides[0].Acoustic!.ChargeDb;
    }

    [Fact]
    public void ASplitJunction_IsJudgedAgainstTheAskedEdgeAtEachOfItsOwnCorners()
    {
        // LR24 at 900 Hz under LR24 at 1100 Hz on flat drivers is acoustic LR24 on both edges.
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [24], independentSlopes: false, CrossoverFilterFamily.LinkwitzRiley) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side(
                "left",
                LowPassChain(Edge(CrossoverFilterFamily.LinkwitzRiley, 900, 24)),
                HighPassChain(Edge(CrossoverFilterFamily.LinkwitzRiley, 1_100, 24)))],
            options);

        Assert.InRange(result.Current.AcousticCostDb!.Value, 0, 0.5);
    }
    [Fact]
    public void WithNoAcousticTargetStated_NothingIsReportedAndTheScoreIsTheSumAlone()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);

        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side("left", LowPassChain(lr), HighPassChain(lr))], Options(700, 1_400));

        JunctionTuneReading reading = result.Current.Sides[0];
        Assert.Null(reading.Acoustic);
        Assert.Empty(result.DriverSlopes);
        Assert.Equal(
            -reading.LossDb +
                CrossoverJunctionTuner.DipPenaltyWeight * (reading.LossDb - reading.DipDb) +
                CrossoverJunctionTuner.RippleWeight * reading.RippleDb,
            reading.ScoreDb,
            1e-9);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void FreeingTheCorners_ReadsPairsHeldApartAndPairsOverlapped()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneSide side = Side("left", LowPassChain(lr), HighPassChain(lr));
        JunctionTuneOptions matched = Options(
            950, 1_050, slopes: [24], independentSlopes: false, CrossoverFilterFamily.LinkwitzRiley);

        JunctionTuneResult without = CrossoverJunctionTuner.Tune([side], matched);
        JunctionTuneResult with = CrossoverJunctionTuner.Tune([side], matched with { SplitCorners = true });

        Assert.True(
            with.CandidatesEvaluated > without.CandidatesEvaluated,
            $"{with.CandidatesEvaluated} candidates against {without.CandidatesEvaluated}.");
        Assert.Equal(
            with.Best.LowerLowPass!.Value.FrequencyHz,
            with.Best.UpperHighPass!.Value.FrequencyHz,
            1e-9);
        Assert.Contains(
            with.RunnersUp,
            candidate => Math.Abs(
                candidate.LowerLowPass!.Value.FrequencyHz - candidate.UpperHighPass!.Value.FrequencyHz) > 1);
    }

    [Fact]
    public void EveryCornerItOffers_IsOneTheCardCanShowAndTheProcessorCanTake()
    {
        // 1 kHz held a twelfth apart is 971.532 / 1029.302 Hz as a pure ratio.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneSide side = Side("left", LowPassChain(lr), HighPassChain(lr));
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [24], independentSlopes: false, CrossoverFilterFamily.LinkwitzRiley)
            with { SplitCorners = true };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune([side], options);

        List<JunctionTuneCandidate> reported = result.RunnersUp.Append(result.Best).ToList();
        Assert.Contains(
            reported,
            candidate => Math.Abs(
                candidate.LowerLowPass!.Value.FrequencyHz -
                candidate.UpperHighPass!.Value.FrequencyHz) > 1);
        foreach (JunctionTuneCandidate candidate in reported)
        {
            foreach (double corner in new[]
            {
                candidate.LowerLowPass!.Value.FrequencyHz,
                candidate.UpperHighPass!.Value.FrequencyHz
            })
            {
                Assert.Equal(Math.Round(corner), corner);
            }
        }
    }

    [Fact]
    public void Tune_RefusesAnEmptyFamilyList_AndAnInvertedWindow()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneSide side = Side("left", LowPassChain(lr), HighPassChain(lr));

        Assert.Throws<ArgumentException>(() => CrossoverJunctionTuner.Tune(
            [side], new JunctionTuneOptions([], null, 700, 1_400, true, SampleRate)));
        Assert.Throws<ArgumentException>(() => CrossoverJunctionTuner.Tune(
            [side], Options(1_400, 700)));
        Assert.Throws<ArgumentException>(() => CrossoverJunctionTuner.Tune(
            [], Options(700, 1_400)));
    }

    [Fact]
    public void SidesRunningDifferentCrossovers_AreRefused_SinceOneCrossoverIsWrittenToBoth()
    {
        CrossoverEdge left = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        CrossoverEdge right = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_100, 24);

        ArgumentException refused = Assert.Throws<ArgumentException>(() => CrossoverJunctionTuner.Tune(
            [
                Side("left", LowPassChain(left), HighPassChain(left)),
                Side("right", LowPassChain(left), HighPassChain(right))
            ],
            Options(700, 1_400)));

        Assert.Contains("different crossovers", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARippleFigureOnlyAChebyshevReads_IsNoDifferenceBetweenTheSides()
    {
        CrossoverEdge left = new(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24, RippleDb: 1.0);
        CrossoverEdge right = left with { RippleDb = 0.5 };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [
                Side("left", LowPassChain(left), HighPassChain(left)),
                Side("right", LowPassChain(right), HighPassChain(right))
            ],
            Options(1_000, 1_000, slopes: [24], independentSlopes: false));

        Assert.Equal(2, result.Current.Sides.Count);
    }

    [Fact]
    public void WithOneAlignmentForAllSides_EverySideIsReadAtOneShift()
    {
        // The right side is half a period late at the corner.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneSide[] sides =
        [
            Side("left", LowPassChain(lr), HighPassChain(lr)),
            Side("right", LowPassChain(lr), HighPassChain(lr, delayMs: 0.5))
        ];

        // Only the current junction is read, so the search is held to its own corner.
        JunctionTuneOptions options = Options(1_000, 1_000, slopes: [24], independentSlopes: false);
        JunctionTuneResult apart = CrossoverJunctionTuner.Tune(sides, options);
        JunctionTuneResult joint = CrossoverJunctionTuner.Tune(
            sides, options with { OneAlignmentForAllSides = true });

        Assert.Equal(2, apart.CurrentAfterDelay.Count);
        Assert.NotEqual(apart.CurrentAfterDelay[0].ExtraDelayMs, apart.CurrentAfterDelay[1].ExtraDelayMs, 2);
        Assert.Equal(2, joint.CurrentAfterDelay.Count);
        Assert.Equal(joint.CurrentAfterDelay[0].ExtraDelayMs, joint.CurrentAfterDelay[1].ExtraDelayMs);
        Assert.Equal(joint.CurrentAfterDelay[0].InvertUpper, joint.CurrentAfterDelay[1].InvertUpper);
        Assert.Equal(joint.Current.Sides[1].LossDb, joint.CurrentAfterDelay[1].LossDb, 6);
        Assert.True(
            joint.Current.Sides[1].LossDb < apart.Current.Sides[1].LossDb - 0.1,
            $"joint {joint.Current.Sides[1].LossDb:0.00} dB against apart {apart.Current.Sides[1].LossDb:0.00} dB.");
    }

    [Fact]
    public void MirrorImageShapes_AreChargedAlike_OnTheLowPassAndTheHighPassSide()
    {
        // Each driver adds its own BW12 an octave past the corner, mirror images on a log axis.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        Complex[] woofer = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), LowPassChain(Edge(CrossoverFilterFamily.Butterworth, 2_000, 12)), SampleRate, SampleRate);
        Complex[] tweeter = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), HighPassChain(Edge(CrossoverFilterFamily.Butterworth, 500, 12)), SampleRate, SampleRate);
        var side = new JunctionTuneSide(
            "left", woofer, LowPassChain(lr), tweeter, HighPassChain(lr), SampleRate);
        JunctionTuneOptions options = Options(
            1_000, 1_000, slopes: [24], independentSlopes: false, CrossoverFilterFamily.LinkwitzRiley) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };

        JunctionAcousticFit fit = CrossoverJunctionTuner.Tune([side], options).Current.Sides[0].Acoustic!;

        Assert.True(fit.LowerChargeDb > 0.3, $"the woofer's own fall was charged {fit.LowerChargeDb:0.00} dB.");
        Assert.Equal(fit.LowerChargeDb!.Value, fit.UpperChargeDb!.Value, 0.1);
    }

    [Fact]
    public void InsideTheBudget_ACandidateLandingEveryChannel_BeatsOneWithTheBetterAverage()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneCandidate missesOne = Charged(1.0, 0.1, 0.1, 0.1, 3.0);
        JunctionTuneCandidate landsAll = Charged(1.1, 1.0, 1.0, 1.0, 1.5);
        JunctionTuneCandidate tooDear = Charged(3.0, 0, 0, 0, 0);

        List<JunctionTuneCandidate> ordered = CrossoverJunctionTuner.OrderForGoal(
            [missesOne, landsAll, tooDear], sumSlackDb: 1.0);

        Assert.True(missesOne.AcousticCostDb < landsAll.AcousticCostDb);
        Assert.Equal([landsAll, missesOne, tooDear], ordered);

        JunctionTuneCandidate Charged(double ripple, double leftLow, double leftHigh, double rightLow, double rightHigh) =>
            new(lr, lr, [],
                [
                    new JunctionTuneReading("left", 0, 0, ripple, new JunctionAcousticFit(leftLow, leftHigh, null, null, null)),
                    new JunctionTuneReading("right", 0, 0, ripple, new JunctionAcousticFit(rightLow, rightHigh, null, null, null))
                ],
                500, 2_000);
    }

    [Fact]
    public void TheNearestAnyFilterComes_IsReadAtTheWorstChannel()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        Complex[] tweeter = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), HighPassChain(Edge(CrossoverFilterFamily.Butterworth, 500, 12)), SampleRate, SampleRate);
        var side = new JunctionTuneSide("left", Impulse(), LowPassChain(lr), tweeter, HighPassChain(lr), SampleRate);
        JunctionTuneOptions options = Options(
            1_000, 1_000, slopes: [24], independentSlopes: false, CrossoverFilterFamily.LinkwitzRiley) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune([side], options);

        Assert.True(result.Best.WorstAcousticChannel!.Upper);
        Assert.Equal(result.Best.WorstAcousticCostDb, result.ClosestAcousticCostDb);
        Assert.True(
            result.ClosestAcousticCostDb > result.Best.AcousticCostDb + 0.1,
            $"nearest {result.ClosestAcousticCostDb:0.00} dB against an average of {result.Best.AcousticCostDb:0.00}.");
    }

    [Fact]
    public void AChannelThePlantCannotRead_IsUnknown_NotALanding()
    {
        // The tweeter's curve stops above the corner, so its skirt has no points.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        List<SignalPoint> full = Plant(notchHz: null, tiltDbPerOctave: 0);
        List<SignalPoint> aboveCorner = full.Where(point => point.X > 1_100).ToList();
        var side = new JunctionTuneSide(
            "left", Impulse(), LowPassChain(lr), Impulse(), HighPassChain(lr), SampleRate, full, aboveCorner);
        JunctionTuneOptions options = Options(
            1_000, 1_000, slopes: [24], independentSlopes: false, CrossoverFilterFamily.LinkwitzRiley) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune([side], options);

        Assert.InRange(result.Best.WorstAcousticCostDb!.Value, 0, 0.5);
        Assert.False(result.Best.AcousticReadInFull);
        Assert.False(result.Best.AcousticGoalLands);
        Assert.Null(result.ClosestAcousticCostDb);
    }

    [Fact]
    public void InsideTheBudget_ACandidateReadInFull_BeatsOneWithAChannelUnread()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        var halfRead = new JunctionTuneCandidate(lr, lr, [],
            [new JunctionTuneReading("left", 0, 0, 1.0, new JunctionAcousticFit(0.1, null, null, null, null))],
            500, 2_000);
        var missing = new JunctionTuneCandidate(lr, lr, [],
            [new JunctionTuneReading("left", 0, 0, 1.1, new JunctionAcousticFit(1.0, 2.5, null, null, null))],
            500, 2_000);

        Assert.Equal([missing, halfRead], CrossoverJunctionTuner.OrderForGoal([halfRead, missing], sumSlackDb: 1.0));
    }

    [Fact]
    public void TheWorstChannel_IsNamed_WhereAnAverageWouldHideIt()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        var candidate = new JunctionTuneCandidate(
            lr, lr, [],
            [
                new JunctionTuneReading("left", 0, 0, 0, new JunctionAcousticFit(0.2, 0.3, null, null, null)),
                new JunctionTuneReading("right", 0, 0, 0, new JunctionAcousticFit(0.4, 3.7, null, null, null))
            ],
            500, 2_000);

        Assert.Equal(1.15, candidate.AcousticCostDb!.Value, 6);
        Assert.Equal(new JunctionAcousticMiss("right", true, 3.7), candidate.WorstAcousticChannel);
        Assert.Equal(3.7, candidate.WorstAcousticCostDb);
        Assert.False(CrossoverJunctionTuner.WasAcousticTargetReached(candidate.WorstAcousticCostDb));
    }
}
