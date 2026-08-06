using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The predicted-arrival probe against fronts that are NOT impulses. The
/// prediction measures its chain term on a flat reference impulse, so the
/// question these pin is how far that transfers to a real driver's shaped
/// front — and, where it does not, that the shortfall can never manufacture
/// a conviction.
/// </summary>
public sealed class ShapedFrontProbe
{
    private const int SampleRate = 48_000;
    private const int Length = 65_536;
    private const int Position = 8_192;

    private sealed class Channel : IAlignmentChannel
    {
        public string Name => "probe";
        public int SampleRate => ShapedFrontProbe.SampleRate;
    }

    private static CrossoverEdge Edge(
        CrossoverFilterFamily family, double hz, int slope) => new(family, hz, slope);

    private static DspChannelChain LowPass(
        double hz, int slope, CrossoverFilterFamily family =
            CrossoverFilterFamily.Butterworth) =>
        new(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass, Edge(family, hz, slope)));

    private static DspChannelChain HighPass(
        double hz, int slope, CrossoverFilterFamily family =
            CrossoverFilterFamily.Butterworth) =>
        new(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: Edge(family, hz, slope)));

    private static DspChannelChain BandPass(
        double highPassHz, double lowPassHz, int slope) =>
        new(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            Edge(CrossoverFilterFamily.Butterworth, lowPassHz, slope),
            Edge(CrossoverFilterFamily.Butterworth, highPassHz, slope)));

    // A front SHAPED by an independent acoustic response, then processed by
    // the channel's own chain — the arrangement the prediction has to survive.
    private static (AlignmentSnapshot Snapshot, double MeasuredMs) Shaped(
        DspChannelChain source,
        DspChannelChain chain,
        double lowHz,
        double highHz)
    {
        var impulse = new Complex[Length];
        impulse[Position] = Complex.One;
        Complex[] bypassed = VirtualCrossoverAnalysis.ApplyChain(
            impulse, source, SampleRate);
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            bypassed, chain, SampleRate);
        var snapshot = new AlignmentSnapshot(
            new Channel(),
            processed,
            VirtualCrossoverAnalysis.FindPeakIndex(processed),
            default,
            chain,
            bypassed);
        double measuredMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            processed, SampleRate, lowHz, highHz).FirstArrivalDelayMilliseconds;
        return (snapshot, measuredMs);
    }

    public static TheoryData<string, int, int, double, double> RealisticFronts()
    {
        var data = new TheoryData<string, int, int, double, double>();
        foreach ((string driver, int hz, int slope) in new[]
        {
            ("driver HP 60", 60, 12), ("driver HP 120", 120, 12)
        })
        {
            data.Add(driver, hz, slope, 40, 160);
            data.Add(driver, hz, slope, 100, 400);
        }

        return data;
    }

    // A driver's own roll-off is a gentle second-order high-pass below or
    // near the junction band, and there the impulse-derived chain term
    // transfers: the prediction lands within a millisecond of the arrival the
    // detector actually reports for the processed response.
    [Theory]
    [MemberData(nameof(RealisticFronts))]
    public void PredictedArrival_TransfersToARealisticDriverFront(
        string driverName, int driverHz, int driverSlope,
        double lowHz, double highHz)
    {
        foreach (DspChannelChain chain in new[]
        {
            HighPass(80, 48, CrossoverFilterFamily.LinkwitzRiley),
            BandPass(70, 200, 36),
            BandPass(180, 1_500, 36)
        })
        {
            (AlignmentSnapshot snapshot, double measuredMs) = Shaped(
                HighPass(driverHz, driverSlope), chain, lowHz, highHz);

            AutoAlignmentEngine.PredictionState state =
                AutoAlignmentEngine.GradeAgainstPrediction(
                    snapshot, measuredMs, lowHz, highHz, out double predictedMs);

            Assert.Equal(AutoAlignmentEngine.PredictionState.Verified, state);
            Assert.True(Math.Abs(measuredMs - predictedMs) < 1.0,
                $"{driverName} in {lowHz:0}-{highHz:0} Hz: predicted " +
                $"{predictedMs:0.000} against a measured {measuredMs:0.000} ms");
        }
    }

    // Where the source has strong structure INSIDE the band — a steep
    // low-pass that leaves the channel barely radiating there, or an
    // all-pass twisting its phase — the impulse-derived term does NOT
    // transfer, and the prediction can be several milliseconds out. That is a
    // real limit of the estimator, so what has to hold is the safety
    // property: such a shortfall may never be mistaken for a modal latch.
    // (The first row is the review's own counterexample, which lands ~5 ms
    // out.)
    [Theory]
    [InlineData("BW24 LP 80 over 40-160", 40, 160)]
    [InlineData("BW24 LP 80 over 100-400", 100, 400)]
    public void PredictedArrival_NeverConvictsForSourceShapingAlone(
        string caseName, double lowHz, double highHz)
    {
        (AlignmentSnapshot snapshot, double measuredMs) = Shaped(
            LowPass(80, 24),
            HighPass(80, 48, CrossoverFilterFamily.LinkwitzRiley),
            lowHz, highHz);

        AutoAlignmentEngine.PredictionState state =
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs, lowHz, highHz, out double predictedMs);

        Assert.True(
            state != AutoAlignmentEngine.PredictionState.Latched,
            $"{caseName}: source shaping alone convicted the read " +
            $"(measured {measuredMs:0.000}, predicted {predictedMs:0.000} ms)");
    }

    [Fact]
    public void PredictedArrival_NeverConvictsForASourceAllPass()
    {
        (AlignmentSnapshot snapshot, double measuredMs) = Shaped(
            new DspChannelChain(
                AllPass: new AllPassSpec(AllPassType.SecondOrder, 150, 2.0)),
            HighPass(80, 48, CrossoverFilterFamily.LinkwitzRiley),
            40, 160);

        Assert.NotEqual(
            AutoAlignmentEngine.PredictionState.Latched,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs, 40, 160, out _));
    }

    // Two shaped fronts through two DIFFERENT chains — the junction case.
    // Each side may be VERIFIED on its own while their residuals differ, and
    // it is the DIFFERENCE that the timeline stores; the pair anchor is only
    // as good as that difference (review find).
    [Fact]
    public void PredictionResiduals_AreWhatTheJunctionAnchorInherits()
    {
        (AlignmentSnapshot lower, double lowerMs) = Shaped(
            HighPass(60, 12), BandPass(70, 200, 36), 100, 400);
        (AlignmentSnapshot upper, double upperMs) = Shaped(
            HighPass(120, 12), BandPass(180, 1_500, 36), 100, 400);

        AutoAlignmentEngine.GradeAgainstPrediction(
            lower, lowerMs, 100, 400, out double lowerPredicted);
        AutoAlignmentEngine.GradeAgainstPrediction(
            upper, upperMs, 100, 400, out double upperPredicted);
        double differentialResidualMs = Math.Abs(
            (lowerMs - lowerPredicted) - (upperMs - upperPredicted));

        // Realistic fronts: the residuals largely cancel, which is what makes
        // the pair anchor usable at all.
        Assert.True(differentialResidualMs < 1.0,
            $"differential residual {differentialResidualMs:0.000} ms");
    }
}
