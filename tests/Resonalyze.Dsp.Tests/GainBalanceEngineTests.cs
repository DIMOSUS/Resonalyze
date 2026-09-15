using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp.Tests;

/// <summary>Cut-only gain balance: octave eligibility, 1/f-weighted levels, L-R difference (LEFT minus RIGHT), joint solve.</summary>
public sealed class GainBalanceEngineTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 4_096;

    private sealed class TestChannel(string name) : IAlignmentChannel
    {
        public string Name { get; } = name;
        public int SampleRate => GainBalanceEngineTests.SampleRate;
        public int ProcessorSampleRate => SampleRate;
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
        Assert.Equal(0.0, GainBalanceEngine.LevelDifferenceDb(0), 9);
        Assert.Equal(2.0, GainBalanceEngine.LevelDifferenceDb(2.0), 9);
        Assert.Equal(-1.5, GainBalanceEngine.LevelDifferenceDb(-1.5), 9);
        // Past the range a 'level difference' is one side switched off.
        Assert.Equal(6.0, GainBalanceEngine.LevelDifferenceDb(40.0), 9);
        Assert.Equal(-6.0, GainBalanceEngine.LevelDifferenceDb(-40.0), 9);
        Assert.Equal(0.0, GainBalanceEngine.LevelDifferenceDb(double.NaN), 9);
    }

    [Fact]
    public void Compute_ReadsTheDifferenceAsLeftMinusRight()
    {
        // The request is L-R: -1 dB must cut the LEFT board, or the image moves the wrong way.
        var leftMid = Input("mid L", 1.0);
        GainBalanceInput rightMid = Input(
            "mid R", 1.0, rightSide: true, leftPeer: leftMid.Channel);
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [leftMid, rightMid], levelDifferenceDb: -1.0, log);

        Assert.Equal(-1.0, results[0].ProposedGainDb, 1);
        Assert.Equal(0.0, results[1].ProposedGainDb, 1);
        // The log uses the current culture (the report is invariant).
        Assert.Contains(
            $"L-R level difference {-1.0:+0.00;-0.00} dB (positive: left side louder)",
            log.ToString());
    }

    [Fact]
    public void SkipReason_OctaveRuleAndGates()
    {
        Assert.Null(GainBalanceEngine.SkipReason(80, 3_000, true, false));
        // 0.74 of 3.06 octaves above 300 (24 %); a linear-Hz fraction would read 45 %.
        Assert.NotNull(GainBalanceEngine.SkipReason(60, 500, true, false));
        Assert.NotNull(GainBalanceEngine.SkipReason(40, 250, true, false));
        // No crossover: the 20-20000 fallback band would qualify anything.
        Assert.NotNull(GainBalanceEngine.SkipReason(20, 20_000, false, false));
        // A shared mono channel's gain moves both boards.
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
        // 1/f weight gives each octave an equal vote; a per-Hz mean would double the upper octave.
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
        // Identical points mean a band too narrow to measure: NaN maps to Low confidence.
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
        // Sequential levelling would need a boost; the joint solve cuts the left instead.
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
        // Existing chain gain must be subtracted, or repeated runs stack cuts.
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
        // One twin fails the octave rule: both are kept, since cut-only cannot restore the pair relation.
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
        Assert.True(results[2].Adjusted);
        Assert.Equal(0.0, results[2].ProposedGainDb, 1);
    }

    [Fact]
    public void Compute_NoSharedBandReadsLowGainConfidence()
    {
        // Non-overlapping crossover bands: the L-R relation was never measured, so Low confidence.
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
        // A dead capture reads a finite -80 dB and would become the cut target (beyond the |GainDb| <= 60 model).
        var mid = Input("mid", 1.0);
        var dead = Input("dead", 1e-4); // -80 dB, flat -> "stable"
        var log = new StringBuilder();

        IReadOnlyList<GainBalanceResult> results = GainBalanceEngine.Compute(
            [mid, dead], levelDifferenceDb: 0, log);

        Assert.False(results[1].Adjusted);
        Assert.Contains("below the loudest", results[1].SkipReason);
        Assert.True(results[0].Adjusted);
        Assert.Equal(0.0, results[0].ProposedGainDb, 1);
        Assert.All(results, result =>
            Assert.True(result.ProposedGainDb >= -GainBalanceEngine.MaxProposedCutDb));
    }

    [Fact]
    public void Compute_ClampsProposalToSupportedGainRange()
    {
        // No proposal may leave the chain gain range the project validator enforces.
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
        // Gate and pair rule compose: a gated right capture keeps its left twin unlevelled.
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
        Assert.True(results[0].Adjusted);
        Assert.Equal(0.0, results[0].ProposedGainDb, 1);
    }
}
