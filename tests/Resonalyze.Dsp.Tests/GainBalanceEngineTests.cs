using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The cut-only gain balance: eligibility (octave rule over the crossover
/// band), 1/f-weighted band levels, the requested L-R level difference
/// (LEFT minus RIGHT) with its clamp, and the joint cut-only solve (board
/// levelling + L/R tilt as one system, shifted so the quietest participant
/// lands at 0 dB of cut). Synthetic spectra are scaled delta impulses (flat
/// by construction), so every expected level is plain arithmetic.
/// </summary>
public sealed class GainBalanceEngineTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 4_096;

    private sealed class TestChannel(string name) : IAlignmentChannel
    {
        public string Name { get; } = name;
        public int SampleRate => GainBalanceEngineTests.SampleRate;
    }

    private static Complex[] Impulse(double amplitude)
    {
        var ir = new Complex[IrLength];
        ir[100] = amplitude;
        return ir;
    }

    private static GainBalanceInput Input(
        string name,
        double amplitude,
        double currentGainDb = 0,
        double bandLowHz = 300,
        double bandHighHz = 3_000,
        bool hasCrossover = true,
        bool mono = false,
        bool rightSide = false,
        IAlignmentChannel? leftPeer = null) =>
        new(
            new TestChannel(name), Impulse(amplitude), SampleRate,
            currentGainDb, bandLowHz, bandHighHz, hasCrossover, mono,
            rightSide, leftPeer);

    [Fact]
    public void LevelDifferenceDb_PassesTheRequestThroughAndClamps()
    {
        // The tilt is the tuner's own figure now: inside the range it is
        // taken verbatim, with no derivation from the scene offset.
        Assert.Equal(0.0, GainBalanceEngine.LevelDifferenceDb(0), 9);
        Assert.Equal(2.0, GainBalanceEngine.LevelDifferenceDb(2.0), 9);
        Assert.Equal(-1.5, GainBalanceEngine.LevelDifferenceDb(-1.5), 9);
        // Past the range a "level difference" is one side switched off.
        Assert.Equal(6.0, GainBalanceEngine.LevelDifferenceDb(40.0), 9);
        Assert.Equal(-6.0, GainBalanceEngine.LevelDifferenceDb(-40.0), 9);
        // A non-finite request must not poison every target with NaN.
        Assert.Equal(0.0, GainBalanceEngine.LevelDifferenceDb(double.NaN), 9);
    }

    [Fact]
    public void Compute_ReadsTheDifferenceAsLeftMinusRight()
    {
        // The one sign the whole feature hangs on: the request is L-R, so the
        // typical left-hand-drive figure (-1 dB) must land on the LEFT board
        // as the cut — reading it as R-L would attenuate the far side and
        // push the image the wrong way.
        var leftMid = Input("mid L", 1.0);
        GainBalanceInput rightMid = Input(
            "mid R", 1.0, rightSide: true, leftPeer: leftMid.Channel);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid], levelDifferenceDb: -1.0, log);

        Assert.Equal(-1.0, results[0].ProposedGainDb, 1);
        Assert.Equal(0.0, results[1].ProposedGainDb, 1);
        // The log is written in the current culture (unlike the report, which
        // is invariant), so the expectation is formatted the same way.
        Assert.Contains(
            $"L-R level difference {-1.0:+0.00;-0.00} dB (positive: left side louder)",
            log.ToString());
    }

    [Fact]
    public void SkipReason_OctaveRuleAndGates()
    {
        // 80-3000 Hz: 3.3 of 5.2 octaves above 300 — eligible.
        Assert.Null(GainBalanceEngine.SkipReason(80, 3_000, true, false));
        // 60-500 Hz: 0.74 of 3.06 octaves above 300 (24 %) — a LINEAR-Hz
        // fraction would read 45 % and wrongly qualify it.
        Assert.NotNull(GainBalanceEngine.SkipReason(60, 500, true, false));
        // Entirely below the floor.
        Assert.NotNull(GainBalanceEngine.SkipReason(40, 250, true, false));
        // No crossover: the 20-20000 fallback band would qualify anything.
        Assert.NotNull(GainBalanceEngine.SkipReason(20, 20_000, false, false));
        // A shared mono channel's gain moves both boards at once.
        Assert.NotNull(GainBalanceEngine.SkipReason(300, 3_000, true, true));
    }

    [Fact]
    public void WeightedBandLevelDb_FlatSpectrumReadsItsAmplitude()
    {
        var power = new double[2_049];
        Array.Fill(power, 4.0); // amplitude 2 everywhere
        double level = GainBalanceEngine.WeightedBandLevelDb(
            power, binWidthHz: 10, lowHz: 300, highHz: 3_000);
        Assert.Equal(10.0 * Math.Log10(4.0), level, 3);
    }

    [Fact]
    public void WeightedBandLevelDb_WeighsPerOctaveNotPerHz()
    {
        // Octave 100-200 Hz at power 1, octave 200-400 Hz at power 0.01: the
        // 1/f weight gives each octave an equal vote -> mean (1+0.01)/2. An
        // unweighted per-Hz mean would give the upper octave twice the bins.
        var power = new double[512];
        for (int bin = 100; bin < 200; bin++)
        {
            power[bin] = 1.0;
        }
        for (int bin = 200; bin <= 400; bin++)
        {
            power[bin] = 0.01;
        }

        double level = GainBalanceEngine.WeightedBandLevelDb(
            power, binWidthHz: 1, lowHz: 100, highHz: 400);
        Assert.InRange(level, 10.0 * Math.Log10(0.505) - 0.2, 10.0 * Math.Log10(0.505) + 0.2);
    }

    [Fact]
    public void RobustSpreadDb_IgnoresASingleOutlier()
    {
        Assert.Equal(
            0.0,
            GainBalanceEngine.RobustSpreadDb(
                [3.0, 3.0, 3.0, 3.0, 3.0, 3.0, 3.0, 3.0]),
            9);
        // One narrow surviving artifact must not dominate the figure.
        Assert.Equal(
            0.0,
            GainBalanceEngine.RobustSpreadDb([0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 10.0]),
            9);
        double spread = GainBalanceEngine.RobustSpreadDb(
            [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0]);
        Assert.InRange(spread, 2.8, 3.1); // IQR 4 / 1.349
    }

    [Fact]
    public void RobustSpreadDb_TooFewSamplesIsNotStability()
    {
        // A handful of identical points means the band was too narrow to
        // measure, not that the measurement was perfectly stable — NaN maps
        // to Low confidence downstream.
        Assert.True(double.IsNaN(
            GainBalanceEngine.RobustSpreadDb([3.0, 3.0, 3.0, 3.0])));
        Assert.Equal(
            AlignmentConfidence.Low,
            GainBalanceEngine.ConfidenceOf(
                GainBalanceEngine.RobustSpreadDb([0.0, 0.0])));
    }

    [Fact]
    public void ConfidenceOf_MapsSpreadBands()
    {
        Assert.Equal(AlignmentConfidence.High, GainBalanceEngine.ConfidenceOf(1.0));
        Assert.Equal(AlignmentConfidence.Medium, GainBalanceEngine.ConfidenceOf(3.0));
        Assert.Equal(AlignmentConfidence.Low, GainBalanceEngine.ConfidenceOf(8.0));
        Assert.Equal(AlignmentConfidence.Low, GainBalanceEngine.ConfidenceOf(double.NaN));
    }

    [Fact]
    public void Compute_LevelsTheBoardCutOnly()
    {
        var log = new StringBuilder();
        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [
                Input("A", 1.0),     //   0 dB
                Input("B", 0.5),     //  -6 dB
                Input("C", 0.25)     // -12 dB — the cut-only floor
            ],
            levelDifferenceDb: 0,
            log);

        Assert.All(results, result => Assert.True(result.Adjusted));
        Assert.All(results, result => Assert.True(result.ProposedGainDb <= 0));
        Assert.InRange(results[0].ProposedGainDb, -12.2, -11.9);
        Assert.InRange(results[1].ProposedGainDb, -6.2, -5.9);
        Assert.Equal(0.0, results[2].ProposedGainDb, 1);
        // Delta impulses are flat: the level is well-defined in-band.
        Assert.All(results, result =>
            Assert.Equal(AlignmentConfidence.High, result.Confidence));
    }

    [Fact]
    public void Compute_LevelDifferenceAttenuatesTheNearSide()
    {
        var leftMid = Input("mid L", 1.0);
        GainBalanceInput rightMid = Input(
            "mid R", 1.0, rightSide: true, leftPeer: leftMid.Channel);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid], levelDifferenceDb: -2.0, log);

        // L-R = -2 dB asks for the left side 2 dB BELOW the right; cut-only,
        // so the LEFT board takes the -2 dB and the right stays at 0.
        Assert.Equal(-2.0, results[0].ProposedGainDb, 1);
        Assert.Equal(0.0, results[1].ProposedGainDb, 1);
    }

    [Fact]
    public void Compute_RightHandDriveMirrorsTheTilt()
    {
        var leftMid = Input("mid L", 1.0);
        GainBalanceInput rightMid = Input(
            "mid R", 1.0, rightSide: true, leftPeer: leftMid.Channel);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid], levelDifferenceDb: 2.0, log);

        Assert.Equal(0.0, results[0].ProposedGainDb, 1);
        Assert.Equal(-2.0, results[1].ProposedGainDb, 1);
    }

    [Fact]
    public void Compute_QuietRightForcesTheLeftDown()
    {
        // The right mid is physically 3 dB quieter while the tuner wants it
        // 2 dB LOUDER than the left: sequential "level left, then match right"
        // would need a +boost. The joint solve cuts the left instead.
        var leftMid = Input("mid L", 1.0);
        GainBalanceInput rightMid = Input(
            "mid R", Math.Pow(10, -3.0 / 20), rightSide: true,
            leftPeer: leftMid.Channel);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid], levelDifferenceDb: -2.0, log);

        Assert.Equal(-5.0, results[0].ProposedGainDb, 1);
        Assert.Equal(0.0, results[1].ProposedGainDb, 1);
    }

    [Fact]
    public void Compute_ProposalIsAbsoluteNotIncremental()
    {
        // Channel B's response already carries a -6 dB gain; the balance must
        // subtract it back out and propose absolute gains, or repeated runs
        // would keep stacking cuts.
        var log = new StringBuilder();
        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [
                Input("A", 1.0),
                Input("B", 0.5, currentGainDb: -6.02)
            ],
            levelDifferenceDb: 0,
            log);

        Assert.Equal(0.0, results[0].ProposedGainDb, 1);
        Assert.Equal(0.0, results[1].ProposedGainDb, 1);
    }

    [Fact]
    public void Compute_HalfEligiblePairIsKeptTogether()
    {
        // The right mid's band (60-500 Hz per its own crossover) fails the
        // octave rule while the left qualifies: adjusting the left alone
        // would break the promised L/R relation for the pair — cut-only
        // forbids the boost that could restore it — so BOTH sides are kept,
        // each naming the twin's reason.
        var leftMid = Input("mid L", 1.0);
        GainBalanceInput rightMid = Input(
            "mid R", 1.0, bandLowHz: 60, bandHighHz: 500,
            rightSide: true, leftPeer: leftMid.Channel);
        var tweeter = Input("twr", 0.5, bandLowHz: 2_000, bandHighHz: 20_000);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid, tweeter], levelDifferenceDb: -2.0, log);

        Assert.False(results[0].Adjusted);
        Assert.Contains("right side ineligible", results[0].SkipReason);
        Assert.False(results[1].Adjusted);
        // The unpaired tweeter still levels normally (alone -> no cut).
        Assert.True(results[2].Adjusted);
        Assert.Equal(0.0, results[2].ProposedGainDb, 1);
    }

    [Fact]
    public void Compute_NoSharedBandReadsLowGainConfidence()
    {
        // Both sides qualify individually but their crossover bands do not
        // overlap: the L-R relation the right gain equalizes was never
        // measured, so its confidence must read Low instead of silently
        // grading the channel's own in-band flatness.
        var leftMid = Input("mid L", 1.0, bandLowHz: 2_000, bandHighHz: 20_000);
        GainBalanceInput rightMid = Input(
            "mid R", 1.0, bandLowHz: 300, bandHighHz: 600,
            rightSide: true, leftPeer: leftMid.Channel);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid], levelDifferenceDb: 0, log);

        Assert.True(results[1].Adjusted);
        Assert.Equal(AlignmentConfidence.Low, results[1].Confidence);
        Assert.Contains("L-R band", results[1].Detail);
        Assert.True(double.IsNaN(results[1].SpreadDb));
    }

    [Fact]
    public void Compute_DeadChannelCannotDragTheBoardDown()
    {
        // A noise-only/broken capture still reads a FINITE level (-80 dB
        // here) and, being the quietest eligible channel, would become the
        // cut-only target for the whole board - proposing ~-80 dB gains that
        // the settings model (|GainDb| <= 60) would refuse to save. The
        // credibility gate must skip it instead.
        var mid = Input("mid", 1.0);
        var dead = Input("dead", 1e-4); // -80 dB, flat -> "stable"
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [mid, dead], levelDifferenceDb: 0, log);

        Assert.False(results[1].Adjusted);
        Assert.Contains("below the loudest", results[1].SkipReason);
        Assert.True(results[0].Adjusted);
        Assert.Equal(0.0, results[0].ProposedGainDb, 1);
        // The independent invariant guard: whatever happens upstream, a
        // proposal never leaves the settings model's range.
        Assert.All(results, result =>
            Assert.True(result.ProposedGainDb >= -GainBalanceEngine.MaxProposedCutDb));
    }

    [Fact]
    public void Compute_ClampsProposalToSupportedGainRange()
    {
        // A 70 dB level split: the credibility gate skips the quiet capture
        // outright (first line of defense), and independently no proposal
        // may ever leave the chain gain range — the ONE shared constant the
        // project validator enforces, so an applied proposal can never
        // produce a project that refuses to save.
        var loud = Input("loud", 1.0);
        var quiet = Input("quiet", Math.Pow(10, -70.0 / 20));
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [loud, quiet], levelDifferenceDb: 0, log);

        Assert.All(results, result => Assert.True(
            result.ProposedGainDb >= -GainBalanceEngine.MaxProposedCutDb &&
            result.ProposedGainDb <= 0,
            $"{result.Channel.Name}: {result.ProposedGainDb} dB out of range"));
        Assert.False(results[1].Adjusted);
        Assert.Equal(
            DspChannelChain.MaximumGainDb, GainBalanceEngine.MaxProposedCutDb, 9);
    }

    [Fact]
    public void Compute_DeadRightSideSkipsItsPairToo()
    {
        // The gate and the pair rule compose: a dead right capture is gated,
        // and its healthy left twin must not be levelled alone - the pair's
        // L/R relation could not follow.
        var leftMid = Input("mid L", 1.0);
        GainBalanceInput rightMid = Input(
            "mid R", 1e-4, rightSide: true, leftPeer: leftMid.Channel);
        var tweeter = Input("twr", 0.9, bandLowHz: 2_000, bandHighHz: 20_000);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid, tweeter], levelDifferenceDb: -2.0, log);

        Assert.False(results[1].Adjusted);
        Assert.Contains("below the loudest", results[1].SkipReason);
        Assert.False(results[0].Adjusted);
        Assert.Contains("right side ineligible", results[0].SkipReason);
        Assert.True(results[2].Adjusted);
    }

    [Fact]
    public void Compute_SkippedChannelsKeepTheirGain()
    {
        var log = new StringBuilder();
        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [
                Input("mid", 1.0),
                Input("sub", 4.0, currentGainDb: -3, bandLowHz: 30,
                    bandHighHz: 80, mono: true),
                Input("raw", 1.0, currentGainDb: 1.5, bandLowHz: 20,
                    bandHighHz: 20_000, hasCrossover: false)
            ],
            levelDifferenceDb: 0,
            log);

        Assert.False(results[1].Adjusted);
        Assert.Equal(-3.0, results[1].ProposedGainDb, 9);
        Assert.Null(results[1].Confidence);
        Assert.False(results[2].Adjusted);
        Assert.Equal(1.5, results[2].ProposedGainDb, 9);
        // The eligible channel still levels normally (alone -> no cut).
        Assert.True(results[0].Adjusted);
        Assert.Equal(0.0, results[0].ProposedGainDb, 1);
    }
}
