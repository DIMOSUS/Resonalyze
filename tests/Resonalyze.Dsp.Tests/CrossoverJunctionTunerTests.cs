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
    public void Tune_ReadsEverySide_AfterItsBestDelay_AndRanksOnTheirMean()
    {
        // A junction tune is followed by re-aligning the delays, so a side that is merely late is not a crossover
        // problem: it is read after the delay that re-alignment would give it, and the delay is reported.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [
                Side("left", LowPassChain(lr), HighPassChain(lr)),
                // Half a period late at the corner: a delay mends it, the crossover does not have to.
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
        // Nothing to retune: the textbook pair stays once the late side is read where it will be.
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
        // A perfect impulse is a flat driver, so acoustic IS electrical here: asked acoustic LR24, the answer is an
        // LR24 pair. Every candidate on offer sums losslessly, so nothing but the stated slope can decide.
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

        // The report compares like with like: both slopes are fitted over the same region.
        JunctionAcousticFit fit = result.Best.Sides[0].Acoustic!;
        Assert.NotNull(fit.TargetSlopeDbPerOctave);
        Assert.Equal(fit.TargetSlopeDbPerOctave!.Value, fit.LowerSlopeDbPerOctave!.Value, 1.5);
        Assert.Equal(fit.TargetSlopeDbPerOctave!.Value, fit.UpperSlopeDbPerOctave!.Value, 1.5);
        Assert.InRange(fit.ChargeDb, 0, 0.5);
        Assert.True(
            result.Current.Sides[0].Acoustic!.ChargeDb > fit.ChargeDb + 1,
            "the steeper pair should be charged for the skirt the EQ could not lift.");
        // A flat driver falls nowhere by itself, so any slope was reachable.
        JunctionDriverSlopes driver = Assert.Single(result.DriverSlopes);
        Assert.True(CrossoverJunctionTuner.IsReachable(
            driver.LowerDbPerOctave, fit.TargetSlopeDbPerOctave));
    }

    [Fact]
    public void ADriverAlreadyFallingSteeperThanAsked_SaysSo_RatherThanPretending()
    {
        // The drivers' own roll-off is an LR24 at 1 kHz. A filter multiplies, so acoustic LR12 is not on offer at any
        // corner - and the report has to say that instead of quietly picking the softest filter.
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
        // And the verdict itself comes off the lattice, not off the regression: no allowed filter comes near.
        Assert.False(CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb));
        Assert.True(result.ClosestAcousticCostDb > CrossoverJunctionTuner.AcousticReachedCostDb);
        // Steeper than asked is charged, and the sign says the EQ would have to LIFT the skirt, which it refuses.
        Assert.True(result.Best.Sides[0].Acoustic!.ResidualDb < 0);
        // An unfitted figure is not a refusal.
        Assert.True(CrossoverJunctionTuner.IsReachable(null, asked));
        Assert.True(CrossoverJunctionTuner.IsReachable(driver.LowerDbPerOctave, null));
    }

    [Fact]
    public void AnAcousticSlopeTheSumCannotAfford_IsNotBought()
    {
        // Asked: acoustic Bessel 24. On a flat driver that is a Bessel 24 pair exactly, and a Bessel pair does not
        // sum flat at any delay or polarity - measured, it ranks well outside the corridor even re-aligned. The
        // stated slope chooses INSIDE what the sum calls equivalent, so the answer stays Linkwitz-Riley and the
        // report shows the deviation it could not spend.
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
        // And the corridor is what held it: the sum score of the winner is the best on offer.
        Assert.True(result.Best.RankingScoreDb <= result.Current.RankingScoreDb + options.SumSlackDb);

        // The lattice says the target WAS reachable - a Bessel pair draws it exactly - so the report can tell
        // "your drivers cannot" from "the summation would not pay for it".
        Assert.True(CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb));
        Assert.True(
            result.ClosestAcousticCostDb < result.Best.AcousticCostDb - 0.5,
            $"closest {result.ClosestAcousticCostDb:0.00} dB against chosen {result.Best.AcousticCostDb:0.00} dB.");
    }

    [Fact]
    public void AStatedSlopeThatSumsAsWellOnceReAligned_IsTaken()
    {
        // Asked: acoustic Butterworth 12, with an LR24 on screen. Every 12 dB/oct pair is 180 degrees apart through
        // the handover, so at the delays set for the LR24 it sums with a hole at the corner (a BW12 pair: -33 dB) and
        // was refused. With the upper channel inverted, as re-aligning finds, an LR12 pair sums flat and draws the
        // asked BW12 within a fraction of a decibel - so it is taken, and the report says the inversion it needs.
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
        // What re-aligning takes is reported: the upper channel inverted.
        Assert.True(Assert.Single(result.BestAfterDelay).InvertUpper);
    }

    [Fact]
    public void TheAcousticReadIgnoresTheBankThatIsAboutToBeRefitted_WhileTheSumStillHearsIt()
    {
        // A bell right at the junction. The bank will be refitted the moment this tune lands, so choosing a filter
        // against it would make the answer depend on the tune's history - the acoustic read takes the PEQ out. The
        // summation read keeps it, because that one is the chain as it actually plays. The asymmetry is deliberate.
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
    public void ANarrowNotchInThePlant_CostsFarLessThanASlopeThatIsSystematicallyWrong()
    {
        // The objective keeps its own resolution: a spatial notch a twelfth of an octave wide is the seat, not the
        // crossover, while half an octave of wrong slope is the crossover. Charging them alike would let the room
        // choose the filter. Plant curves are supplied here, which is how a channel with a spatial average arrives.
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

        // A twelve-decibel notch one twelfth of an octave wide must read almost like the flat plant it sits in.
        Assert.Equal(flat, notched, 0.2);
        // And a slope error the eye would call mild must read as the expensive one of the two.
        Assert.True(
            tilted > notched + 0.5,
            $"the tilt charged {tilted:0.00} dB against the notch's {notched:0.00} dB (flat reads {flat:0.00}).");
    }

    [Fact]
    public void ADeficitAcrossTheWholeSkirt_IsNotTrimmedAway()
    {
        // The other side of the trim: it must drop the seat's narrow damage, not a systematic error. Three decibels
        // too steep everywhere is exactly what the mode exists to notice.
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
        // The goal a tune writes is a family and a slope at the ELECTRICAL corner of each edge, and the EQ stage
        // then aims every edge at its own corner. So the tune must judge a split pair the same way: an LR24
        // low-pass at 900 Hz and an LR24 high-pass at 1100 Hz on flat drivers ARE acoustic LR24 on both edges.
        // Judged against one shared corner, the high-pass reads a sixth of an octave too steep and is charged
        // for a slope it draws exactly.
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
    public void FreeingTheCorners_ReadsPairsHeldApartAndPairsOverlapped()
    {
        // A split is a signed offset from the junction, refined on the corners the sweep settled: positive holds
        // them apart, which takes a bump off the junction; negative overlaps them, which fills a dip. As a second
        // lattice dimension it would square the candidate count, so it is a pass of its own.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneSide side = Side("left", LowPassChain(lr), HighPassChain(lr));
        JunctionTuneOptions matched = Options(
            950, 1_050, slopes: [24], independentSlopes: false, CrossoverFilterFamily.LinkwitzRiley);

        JunctionTuneResult without = CrossoverJunctionTuner.Tune([side], matched);
        JunctionTuneResult with = CrossoverJunctionTuner.Tune([side], matched with { SplitCorners = true });

        Assert.True(
            with.CandidatesEvaluated > without.CandidatesEvaluated,
            $"{with.CandidatesEvaluated} candidates against {without.CandidatesEvaluated}.");
        // A textbook matched pair is still what wins on a flat driver: a split has to earn its place on the sum
        // like any other candidate.
        Assert.Equal(
            with.Best.LowerLowPass!.Value.FrequencyHz,
            with.Best.UpperHighPass!.Value.FrequencyHz,
            1e-9);
        // And the pass really did read spread pairs: at least one reported candidate has its corners apart.
        Assert.Contains(
            with.RunnersUp,
            candidate => Math.Abs(
                candidate.LowerLowPass!.Value.FrequencyHz - candidate.UpperHighPass!.Value.FrequencyHz) > 1);
    }

    [Fact]
    public void EveryCornerItOffers_IsOneTheCardCanShowAndTheProcessorCanTake()
    {
        // The channel card states the crossover frequency with no decimals. A split taken as a pure ratio of
        // the corner lands between hertz (1 kHz held a twelfth apart is 971.532 / 1029.302), and the panel
        // would then show 972 and 1029 while the chain ran the fractions - a filter the user cannot read back,
        // let alone type into a processor.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneSide side = Side("left", LowPassChain(lr), HighPassChain(lr));
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [24], independentSlopes: false, CrossoverFilterFamily.LinkwitzRiley)
            with { SplitCorners = true };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune([side], options);

        List<JunctionTuneCandidate> reported = result.RunnersUp.Append(result.Best).ToList();
        // Without a split among them the test would be watching nothing.
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
}
