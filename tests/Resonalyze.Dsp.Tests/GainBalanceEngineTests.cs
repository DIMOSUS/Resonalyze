using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The cut-only gain balance: eligibility (octave rule over the crossover
/// band), 1/f-weighted band levels, the scene tilt with its clamp, and the
/// joint cut-only solve (board levelling + L/R tilt as one system, shifted so
/// the quietest participant lands at 0 dB of cut). Synthetic spectra are
/// scaled delta impulses (flat by construction), so every expected level is
/// plain arithmetic.
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
    public void SceneTiltDb_InterpolatesAndClamps()
    {
        Assert.Equal(0.0, GainBalanceEngine.SceneTiltDb(0), 9);
        Assert.Equal(2.0, GainBalanceEngine.SceneTiltDb(0.27), 9);
        Assert.Equal(-2.0, GainBalanceEngine.SceneTiltDb(-0.27), 9);
        Assert.Equal(1.0, GainBalanceEngine.SceneTiltDb(0.135), 9);
        // The offset control reaches ±5 ms; the trade must not extrapolate to
        // ±37 dB of board attenuation.
        Assert.Equal(6.0, GainBalanceEngine.SceneTiltDb(5.0), 9);
        Assert.Equal(-6.0, GainBalanceEngine.SceneTiltDb(-5.0), 9);
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
            sceneOffsetMs: 0,
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
    public void Compute_SceneTiltAttenuatesTheNearSide()
    {
        var leftMid = Input("mid L", 1.0);
        GainBalanceInput rightMid = Input(
            "mid R", 1.0, rightSide: true, leftPeer: leftMid.Channel);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid], sceneOffsetMs: 0.27, log);

        // +0.27 ms scene offset = the right side louder by 2 dB; cut-only, so
        // the LEFT board takes the -2 dB and the right stays at 0.
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
            [leftMid, rightMid], sceneOffsetMs: -0.27, log);

        Assert.Equal(0.0, results[0].ProposedGainDb, 1);
        Assert.Equal(-2.0, results[1].ProposedGainDb, 1);
    }

    [Fact]
    public void Compute_QuietRightForcesTheLeftDown()
    {
        // The right mid is physically 3 dB quieter while the scene wants it
        // 2 dB LOUDER than the left: sequential "level left, then match right"
        // would need a +boost. The joint solve cuts the left instead.
        var leftMid = Input("mid L", 1.0);
        GainBalanceInput rightMid = Input(
            "mid R", Math.Pow(10, -3.0 / 20), rightSide: true,
            leftPeer: leftMid.Channel);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid], sceneOffsetMs: 0.27, log);

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
            sceneOffsetMs: 0,
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
            [leftMid, rightMid, tweeter], sceneOffsetMs: 0.27, log);

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
            [leftMid, rightMid], sceneOffsetMs: 0, log);

        Assert.True(results[1].Adjusted);
        Assert.Equal(AlignmentConfidence.Low, results[1].Confidence);
        Assert.Contains("L-R band", results[1].Detail);
        Assert.True(double.IsNaN(results[1].SpreadDb));
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
            sceneOffsetMs: 0,
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
