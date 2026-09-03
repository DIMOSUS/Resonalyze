using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class CrossoverJunctionTunerTests
{
    private const int SampleRate = 48_000;

    // A flat driver: a unit impulse, so everything the junction measures is the
    // crossover's own doing.
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
        // Two flat drivers through a Linkwitz-Riley pair at one corner sum to a
        // flat, lossless junction: nothing in the window can beat it by the
        // keep margin, so the current crossover stands.
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
        // A 12 dB/oct low-pass at 700 Hz against a 12 dB/oct high-pass at
        // 1400 Hz: the drivers overlap over an octave with a hump where both
        // play. The tuner, allowed the Linkwitz-Riley family over 500–2000 Hz,
        // should put both edges on one corner and flatten the sum.
        JunctionTuneResult result = CrossoverJunctionTuner.Tune(
            [Side("left",
                LowPassChain(Edge(CrossoverFilterFamily.Butterworth, 700, 12)),
                HighPassChain(Edge(CrossoverFilterFamily.Butterworth, 1_400, 12)))],
            Options(500, 2_000));

        Assert.True(result.Changed);
        Assert.True(result.Best.RankingScoreDb < result.Current.RankingScoreDb - CrossoverJunctionTuner.DefaultKeepMarginDb);
        Assert.True(result.Best.ScoreDb <= result.Current.ScoreDb);
        Assert.True(result.Best.Sides[0].RippleDb < result.Current.Sides[0].RippleDb);
        // The shared band holds every candidate's overlap region: an octave
        // outside the 500–2000 Hz window.
        Assert.Equal(250, result.RankingBandLowHz);
        Assert.Equal(4_000, result.RankingBandHighHz);
        Assert.Equal(2, result.Best.Sides.Count + result.Best.RankingSides.Count);
        Assert.Equal(CrossoverFilterFamily.LinkwitzRiley, result.Best.LowerLowPass!.Value.Family);
        Assert.Equal(result.Best.LowerLowPass!.Value.FrequencyHz, result.Best.UpperHighPass!.Value.FrequencyHz);
        Assert.InRange(result.Best.LowerLowPass!.Value.FrequencyHz, 500, 2_000);
        // The corner is one the wizard could have proposed.
        Assert.Equal(
            CrossoverAutoSetup.RoundToLattice(result.Best.LowerLowPass!.Value.FrequencyHz),
            result.Best.LowerLowPass!.Value.FrequencyHz);
        // The read after the best delay is reported for both, on every side.
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
                // The right side's upper channel arrives half a period late at
                // the corner: a junction the crossover alone cannot mend.
                Side("right", LowPassChain(lr), HighPassChain(lr, delayMs: 0.5))
            ],
            Options(700, 1_400));

        Assert.Equal(["left", "right"], result.Current.Sides.Select(side => side.Side));
        Assert.True(result.Current.Sides[1].LossDb < result.Current.Sides[0].LossDb - 1.0);
        Assert.Equal(
            result.Current.Sides.Average(side => side.ScoreDb), result.Current.ScoreDb, 9);
        // The after-delay read says what timing would take back on the right.
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
        // An upper channel with no crossover gains a high-pass and nothing else.
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
        // The shared band spans both corners' overlap regions; each entry also
        // carries its own, an octave each side of its own corner.
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
        // The textbook pair is lossless and in phase; the wide 12 dB/oct one is
        // neither, which is the whole point of asking.
        Assert.InRange(result.Entries[0].Sides[0].LossDb, -0.2, 0.0);
        Assert.NotNull(result.Entries[0].Phase[0].Result);
        Assert.InRange(result.Entries[0].Phase[0].Result!.CurrentScore, 0.9, 1.0);
        Assert.True(result.Entries[1].Sides[0].RippleDb > result.Entries[0].Sides[0].RippleDb);
        // Nothing about the inputs was touched.
        Assert.Equal(lr, side.LowerChain.Crossover!.LowPassEdge);
        Assert.Equal(lr, side.UpperChain.Crossover!.HighPassEdge);
    }

    [Fact]
    public void Probe_ReadsABankChangeWithoutApplyingIt()
    {
        // A deep bell right at the corner on the lower channel: the junction
        // reads one way with it and another without, and the probe answers both
        // without anything being applied.
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

        // Same corner, so the two are read on the same band and compare directly.
        Assert.Equal(result.Entries[0].BandLowHz, result.Entries[1].BandLowHz);
        Assert.True(
            result.Entries[1].Sides[0].RippleDb < result.Entries[0].Sides[0].RippleDb,
            "clearing the bell should flatten the junction's own sum");
        Assert.Same(bank, side.LowerChain.Peq);
    }

    [Fact]
    public void ProbeAlignment_FindsTheDelayThatWouldBeApplied_WithoutApplyingIt()
    {
        // The upper channel arrives 0.4 ms late; the search should offer to take
        // that back on it, and say what the loss would be there.
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
