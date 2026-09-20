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
    public void Tune_ReadsEverySide_AndRanksOnTheirMean()
    {
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [
                Side("left", LowPassChain(lr), HighPassChain(lr)),
                // Half a period late at the corner: the crossover alone cannot mend it.
                Side("right", LowPassChain(lr), HighPassChain(lr, delayMs: 0.5))
            ],
            Options(700, 1_400));

        Assert.Equal(["left", "right"], result.Current.Sides.Select(side => side.Side));
        Assert.True(result.Current.Sides[1].LossDb < result.Current.Sides[0].LossDb - 1.0);
        Assert.Equal(
            result.Current.Sides.Average(side => side.ScoreDb), result.Current.ScoreDb, 9);
        JunctionTuneAlignment right = Assert.Single(
            result.CurrentAfterDelay, alignment => alignment.Side == "right");
        Assert.InRange(right.ExtraDelayMs, -0.6, -0.4);
        Assert.True(right.LossDb > result.Current.Sides[1].LossDb + 1.0);
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
        // Asked: acoustic Butterworth 12. On a flat driver that is a BW12 pair exactly - which sums with a bump, and
        // a bump costs far more than the corridor allows. The stated slope chooses INSIDE what the sum calls
        // equivalent, so the answer stays Linkwitz-Riley and the report shows the deviation it could not spend.
        CrossoverEdge lr = Edge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneOptions options = Options(
            950, 1_050, slopes: [12, 24], independentSlopes: false,
            CrossoverFilterFamily.LinkwitzRiley, CrossoverFilterFamily.Butterworth) with
        {
            AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 12)
        };

        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side("left", LowPassChain(lr), HighPassChain(lr))], options);

        Assert.Equal(CrossoverFilterFamily.LinkwitzRiley, result.Best.LowerLowPass!.Value.Family);
        Assert.True(
            result.Best.Sides[0].Acoustic!.ChargeDb > 0.5,
            "the answer cannot draw the asked edge, and the report must say so rather than hide it.");
        // And the corridor is what held it: the sum score of the winner is the best on offer.
        Assert.True(result.Best.RankingScoreDb <= result.Current.RankingScoreDb + options.SumSlackDb);

        // The lattice says the target WAS reachable - a Butterworth pair draws it exactly - so the report can tell
        // "your drivers cannot" from "the summation would not pay for it".
        Assert.True(CrossoverJunctionTuner.WasAcousticTargetReached(result.ClosestAcousticCostDb));
        Assert.True(
            result.ClosestAcousticCostDb < result.Best.AcousticCostDb - 0.5,
            $"closest {result.ClosestAcousticCostDb:0.00} dB against chosen {result.Best.AcousticCostDb:0.00} dB.");
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
