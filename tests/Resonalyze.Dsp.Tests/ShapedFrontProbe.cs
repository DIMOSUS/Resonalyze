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

    public static TheoryData<string> ToleranceSources()
    {
        var data = new TheoryData<string>();
        foreach (string name in new[]
        {
            "driver HP 60", "driver HP 120", "BW24 LP 80", "BW48 LP 80",
            "BP 40-200", "all-pass 150 Q2", "all-pass 220 Q6"
        })
        {
            data.Add(name);
        }

        return data;
    }

    private static DspChannelChain SourceNamed(string name) => name switch
    {
        "driver HP 60" => HighPass(60, 12),
        "driver HP 120" => HighPass(120, 12),
        "BW24 LP 80" => LowPass(80, 24),
        "BW48 LP 80" => LowPass(80, 48),
        "BP 40-200" => BandPass(40, 200, 24),
        "all-pass 150 Q2" => new DspChannelChain(
            AllPass: new AllPassSpec(AllPassType.SecondOrder, 150, 2.0)),
        "all-pass 220 Q6" => new DspChannelChain(
            AllPass: new AllPassSpec(AllPassType.SecondOrder, 220, 6.0)),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // The upper-half probe's allowance is built from the SAME estimator and
    // is NOT protected by the conviction factor — the credited skew is added
    // to the tolerance directly, so whatever it over-credits is room a real
    // modal latch could hide in (review find).
    //
    // What this pins is the size of that room. The credit may never exceed
    // the skew the clean front honestly shows by more than one base
    // allowance, whatever the source does — so the window this PR opens is
    // bounded by the physics it is meant to cover, not by the estimator's
    // luck. Held across sources the estimator handles well and sources it
    // handles badly alike.
    [Theory]
    [MemberData(nameof(ToleranceSources))]
    public void ArrivalProbeTolerance_NeverCreditsMoreThanTheHonestSkew(
        string sourceName)
    {
        foreach ((DspChannelChain chain, double lowHz, double highHz) in
            ToleranceCases())
        {
            (double honestSkewMs, double toleranceMs, double baseToleranceMs) =
                MeasureTolerance(sourceName, chain, lowHz, highHz);
            double creditMs = toleranceMs - baseToleranceMs;

            Assert.True(creditMs <= Math.Max(0, honestSkewMs) + baseToleranceMs,
                $"{sourceName} in {lowHz:0}-{highHz:0} Hz: credited " +
                $"{creditMs:0.000} ms against an honest skew of " +
                $"{honestSkewMs:0.000} ms (base {baseToleranceMs:0.000} ms)");
        }
    }

    // And for a front the estimator DOES handle — a driver's own roll-off —
    // the allowance must actually cover the skew, or the probe convicts a
    // channel for its own crossover. That is the defect this PR started
    // from, restated against a shaped front rather than an impulse.
    [Theory]
    [InlineData("driver HP 60")]
    [InlineData("driver HP 120")]
    public void ArrivalProbeTolerance_CoversARealisticShapedSkew(string sourceName)
    {
        foreach ((DspChannelChain chain, double lowHz, double highHz) in
            ToleranceCases())
        {
            (double honestSkewMs, double toleranceMs, _) =
                MeasureTolerance(sourceName, chain, lowHz, highHz);

            Assert.True(honestSkewMs <= toleranceMs,
                $"{sourceName} in {lowHz:0}-{highHz:0} Hz: honest skew " +
                $"{honestSkewMs:0.000} ms exceeds the allowance " +
                $"{toleranceMs:0.000} ms");
        }
    }

    private static IEnumerable<(DspChannelChain Chain, double LowHz, double HighHz)>
        ToleranceCases()
    {
        yield return (BandPass(70, 200, 36), 100.0, 400.0);
        yield return (BandPass(180, 1_500, 36), 100.0, 400.0);
        yield return (
            HighPass(80, 48, CrossoverFilterFamily.LinkwitzRiley), 40.0, 160.0);
    }

    private static (double HonestSkewMs, double ToleranceMs, double BaseToleranceMs)
        MeasureTolerance(
            string sourceName, DspChannelChain chain, double lowHz, double highHz)
    {
        double probeLowHz = Math.Sqrt(lowHz * highHz);
        (AlignmentSnapshot clean, double fullMs) = Shaped(
            SourceNamed(sourceName), chain, lowHz, highHz);
        double probeMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            clean.ImpulseResponse, SampleRate, probeLowHz, highHz)
            .FirstArrivalDelayMilliseconds;
        return (
            fullMs - probeMs,
            AutoAlignmentEngine.ArrivalProbeToleranceMs(
                clean, lowHz, probeLowHz, highHz),
            Math.Max(1.0, 500.0 / probeLowHz));
    }

    // A real late mode on a shaped front must still be convicted by the
    // upper-half probe, credited allowance and all — the credit exists to
    // excuse a channel's own dispersion, never a room's.
    [Theory]
    [InlineData("driver HP 60")]
    [InlineData("driver HP 120")]
    public void ArrivalProbeTolerance_StillConvictsALateModeOnAShapedFront(
        string sourceName)
    {
        const double LowHz = 100;
        const double HighHz = 400;
        double probeLowHz = Math.Sqrt(LowHz * HighHz);
        DspChannelChain chain = BandPass(70, 200, 36);

        var impulse = new Complex[Length];
        impulse[Position] = Complex.One;
        Complex[] front = VirtualCrossoverAnalysis.ApplyChain(
            impulse, SourceNamed(sourceName), SampleRate);
        Complex[] withMode = (Complex[])front.Clone();
        int start = Position + (int)(0.012 * SampleRate);
        double peak = front.Max(sample => sample.Magnitude);
        for (int i = start; i < withMode.Length; i++)
        {
            double t = (i - start) / (double)SampleRate;
            withMode[i] += 0.8 * peak *
                (1 - Math.Exp(-t / 0.008)) * Math.Exp(-t / 0.1) *
                Math.Sin(2 * Math.PI * 120 * t);
        }

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            withMode, chain, SampleRate);
        var snapshot = new AlignmentSnapshot(
            new Channel(), processed,
            VirtualCrossoverAnalysis.FindPeakIndex(processed),
            default, chain, withMode);
        TimeAlignmentAnalysisResult full =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                processed, SampleRate, LowHz, HighHz);
        TimeAlignmentAnalysisResult probe =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                processed, SampleRate, probeLowHz, HighHz);

        // The fixture has to latch before the assertion means anything.
        Assert.True(
            full.FirstArrivalDelayMilliseconds -
                probe.FirstArrivalDelayMilliseconds > 5.0,
            $"{sourceName}: the fixture did not latch (full " +
            $"{full.FirstArrivalDelayMilliseconds:0.000}, probe " +
            $"{probe.FirstArrivalDelayMilliseconds:0.000} ms)");
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Latched,
            AutoAlignmentEngine.ClassifyArrival(
                full, probe,
                AutoAlignmentEngine.ArrivalProbeToleranceMs(
                    snapshot, LowHz, probeLowHz, HighHz)));
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
