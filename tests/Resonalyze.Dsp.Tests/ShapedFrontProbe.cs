using System.Collections.Concurrent;
using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>The predicted-arrival probe against shaped (non-impulse) fronts: how far the impulse-measured chain term transfers,
/// and that a shortfall never manufactures a conviction.</summary>
[Trait("Category", "Slow")]
public sealed class ShapedFrontProbe
{
    private const int SampleRate = 48_000;
    private const int Length = 65_536;
    private const int Position = 8_192;

    private sealed class Channel : IAlignmentChannel
    {
        public string Name => "probe";
        public int SampleRate => ShapedFrontProbe.SampleRate;
        public int ProcessorSampleRate => SampleRate;
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

    private static (AlignmentSnapshot Snapshot, double MeasuredMs) Shaped(
        DspChannelChain source,
        DspChannelChain chain,
        double lowHz,
        double highHz)
    {
        var impulse = new Complex[Length];
        impulse[Position] = Complex.One;
        // Built as production builds it: the bypassed response carries ApplyChain's ValidSampleRange.
        Complex[] bypassed = VirtualCrossoverAnalysis.ApplyChain(
            impulse, source, SampleRate, SampleRate, out ValidSampleRange bypassedRange);
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            bypassed, chain, SampleRate, SampleRate, out ValidSampleRange processedRange);
        var snapshot = new AlignmentSnapshot(
            new Channel(),
            processed,
            VirtualCrossoverAnalysis.FindPeakIndex(processed),
            processedRange,
            chain,
            bypassed,
            bypassedRange);
        double measuredMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            processed, SampleRate, lowHz, highHz, processedRange)
            .FirstArrivalDelayMilliseconds;
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

    // A gentle second-order roll-off near the band: the chain term transfers to within a millisecond.
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

    // Strong in-band source structure (steep LP, all-pass) breaks transfer by several ms; that must never read as a latch.
    // BW24 is the counterexample (~5 ms); BW48 doubles the steepness.
    [Theory]
    [InlineData("BW24 LP 80 over 40-160", 24, 40, 160)]
    [InlineData("BW48 LP 80 over 40-160", 48, 40, 160)]
    public void PredictedArrival_NeverConvictsForSourceShapingAlone(
        string caseName, int sourceSlope, double lowHz, double highHz)
    {
        (AlignmentSnapshot snapshot, double measuredMs) = Shaped(
            LowPass(80, sourceSlope),
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

    // Extreme shaping (LP 80 graded at 100-400 Hz) honestly exceeds the conviction factor; the conviction must be corrective,
    // moving the anchor toward the bypassed front. See AcausalPedestalTests for the earlier artifact.
    [Fact]
    public void PredictedArrival_ExtremeShapingConviction_PullsTowardTheFront()
    {
        (AlignmentSnapshot snapshot, double measuredMs) = Shaped(
            LowPass(80, 24),
            HighPass(80, 48, CrossoverFilterFamily.LinkwitzRiley),
            100, 400);
        double bareMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            snapshot.BypassedImpulseResponse!, SampleRate, 100, 400,
            snapshot.BypassedValidRange).FirstArrivalDelayMilliseconds;

        AutoAlignmentEngine.PredictionState state =
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs, 100, 400, out double predictedMs);

        Assert.Equal(AutoAlignmentEngine.PredictionState.Latched, state);
        Assert.True(
            predictedMs < measuredMs && Math.Abs(predictedMs - bareMs) < 2.5,
            $"the conviction must pull toward the front: bare {bareMs:0.000}, " +
            $"predicted {predictedMs:0.000}, measured {measuredMs:0.000} ms");
    }

    [Fact]
    public void PredictedArrival_NeverConvictsForASourceAllPass()
    {
        (AlignmentSnapshot snapshot, double measuredMs) = Shaped(
            new DspChannelChain(
                Peq: new EqualizationCurve(
                    [new PeqBand(150, 2.0, 0, PeqBandType.AllPassSecondOrder)])),
            HighPass(80, 48, CrossoverFilterFamily.LinkwitzRiley),
            40, 160);

        Assert.NotEqual(
            AutoAlignmentEngine.PredictionState.Latched,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs, 40, 160, out _));
    }

    // A bypassed response's array is twice its measured content; the predictor must use the reported range. Asserts the precondition.
    [Theory]
    [InlineData("driver HP 60", 100, 400)]
    [InlineData("driver HP 120", 40, 160)]
    public void PredictedArrival_UsesTheMeasuredContentWindow(
        string sourceName, double lowHz, double highHz)
    {
        var raw = new Complex[Length];
        raw[Position] = Complex.One;
        Complex[] bypassed = VirtualCrossoverAnalysis.ApplyChain(
            raw, SourceNamed(sourceName), SampleRate, SampleRate,
            out ValidSampleRange bypassedRange);
        DspChannelChain chain = BandPass(70, 200, 36);
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            bypassed, chain, SampleRate, SampleRate, out ValidSampleRange processedRange);

        int contentLength = bypassedRange.EndSample - bypassedRange.StartSample;
        Assert.True(bypassedRange.IsKnown, "the fixture must carry a known range");
        Assert.True(bypassed.Length > contentLength,
            $"the fixture must exercise the padded/content gap: array " +
            $"{bypassed.Length}, content {contentLength}");

        var snapshot = new AlignmentSnapshot(
            new Channel(), processed,
            VirtualCrossoverAnalysis.FindPeakIndex(processed),
            processedRange, chain, bypassed, bypassedRange);
        double measuredMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            processed, SampleRate, lowHz, highHz, processedRange)
            .FirstArrivalDelayMilliseconds;

        AutoAlignmentEngine.PredictionState state =
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs, lowHz, highHz, out double predictedMs);

        Assert.Equal(AutoAlignmentEngine.PredictionState.Verified, state);
        Assert.True(Math.Abs(measuredMs - predictedMs) < 1.0,
            $"{sourceName} in {lowHz:0}-{highHz:0} Hz: predicted " +
            $"{predictedMs:0.000} against a measured {measuredMs:0.000} ms");
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
            Peq: new EqualizationCurve(
                [new PeqBand(150, 2.0, 0, PeqBandType.AllPassSecondOrder)])),
        "all-pass 220 Q6" => new DspChannelChain(
            Peq: new EqualizationCurve(
                [new PeqBand(220, 6.0, 0, PeqBandType.AllPassSecondOrder)])),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // The probe's credited skew is not protected by the conviction factor: over-credit must stay under half a base allowance.
    [Theory]
    [MemberData(nameof(ToleranceSources))]
    public void ArrivalProbeTolerance_OverCreditsNoSourceByMoreThanHalfTheBase(
        string sourceName)
    {
        foreach ((double lowHz, double highHz, double honestSkewMs,
            double toleranceMs, double baseToleranceMs) in TolerancesFor(sourceName))
        {
            double overCreditMs =
                toleranceMs - baseToleranceMs - Math.Max(0, honestSkewMs);

            // Asserting against the clamp's ceiling would prove nothing.
            Assert.True(overCreditMs < 0.5 * baseToleranceMs,
                $"{sourceName} in {lowHz:0}-{highHz:0} Hz: over-credited " +
                $"{overCreditMs:0.000} ms (tolerance {toleranceMs:0.000}, " +
                $"base {baseToleranceMs:0.000}, honest skew {honestSkewMs:0.000} ms)");
        }
    }

    // The slow grid sits in its own class so xUnit runs it beside the rest; it runs one class's tests in turn.
    [Trait("Category", "Slow")]
    public sealed class CreditedWindow
    {
        // Skews landing between the base allowance and the clamped ceiling: the probe declines to convict there.
        [Fact]
        public void ArrivalProbeTolerance_DoesNotConvictInsideTheCreditedWindow()
        {
            const double LowHz = 100;
            const double HighHz = 400;
            double probeLowHz = Math.Sqrt(LowHz * HighHz);
            double baseToleranceMs = Math.Max(1.0, 500.0 / probeLowHz);

            // Only a near, weak build-up lands in the 2.5-5 ms window (5-6.5 ms, level under 0.15 on a 2-9 ms, 0.05-2 sweep);
            // the grid is that corner alone, so most of it must land.
            var landed = new List<(double DelayMs, double Level, double SkewMs)>();
            int gridPoints = 0;
            for (double modeDelayMs = 5.0; modeDelayMs <= 6.5; modeDelayMs += 0.5)
            {
                for (double level = 0.05; level <= 0.15; level *= 1.69)
                {
                    gridPoints++;
                    (AlignmentSnapshot snapshot,
                        TimeAlignmentAnalysisResult full,
                        TimeAlignmentAnalysisResult probe) = ModeFixture(
                            modeDelayMs, level, LowHz, probeLowHz, HighHz);
                    double skewMs = full.FirstArrivalDelayMilliseconds -
                        probe.FirstArrivalDelayMilliseconds;
                    if (skewMs <= baseToleranceMs || skewMs >= 2.0 * baseToleranceMs)
                    {
                        continue;
                    }

                    landed.Add((modeDelayMs, level, skewMs));
                    // A 200-400 Hz probe resolves ~5 ms: energy 3-4 ms behind the front may be dispersion. Field latches run 7 ms and up.
                    double toleranceMs = AutoAlignmentEngine.ArrivalProbeToleranceMs(
                        snapshot, full.FirstArrivalDelayMilliseconds,
                        probe.FirstArrivalDelayMilliseconds,
                        LowHz, probeLowHz, HighHz);
                    Assert.Equal(
                        AutoAlignmentEngine.ArrivalCertificate.Verified,
                        AutoAlignmentEngine.ClassifyArrival(full, probe, toleranceMs));
                    // The credit may never push the tolerance past the probe's resolution.
                    Assert.True(toleranceMs <= 1000.0 / probeLowHz,
                        $"mode {modeDelayMs:0.0} ms at {level:0.00}: tolerance " +
                        $"{toleranceMs:0.000} ms exceeds the probe's resolution " +
                        $"({1000.0 / probeLowHz:0.000} ms)");
                }
            }

            Assert.True(2 * landed.Count > gridPoints,
                $"only {landed.Count} of {gridPoints} build-ups put the skew inside the credited window " +
                $"({baseToleranceMs:0.000}-{2.0 * baseToleranceMs:0.000} ms) — " +
                "the grid no longer sits on the corner it asserts");
        }

        private static (AlignmentSnapshot Snapshot,
            TimeAlignmentAnalysisResult Full, TimeAlignmentAnalysisResult Probe)
            ModeFixture(
                double modeDelayMs, double modeLevel,
                double lowHz, double probeLowHz, double highHz)
        {
            DspChannelChain chain = BandPass(70, 200, 36);
            var impulse = new Complex[Length];
            impulse[Position] = Complex.One;
            Complex[] bypassed = VirtualCrossoverAnalysis.ApplyChain(
                impulse, HighPass(60, 12), SampleRate, SampleRate);
            int start = Position + (int)(modeDelayMs / 1_000.0 * SampleRate);
            double peak = bypassed.Max(sample => sample.Magnitude);
            for (int i = start; i < bypassed.Length; i++)
            {
                double t = (i - start) / (double)SampleRate;
                bypassed[i] += modeLevel * peak *
                    (1 - Math.Exp(-t / 0.008)) * Math.Exp(-t / 0.1) *
                    Math.Sin(2 * Math.PI * 120 * t);
            }

            Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                bypassed, chain, SampleRate, SampleRate, out ValidSampleRange processedRange);
            return (
                new AlignmentSnapshot(
                    new Channel(), processed,
                    VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    processedRange, chain, bypassed),
                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                    processed, SampleRate, lowHz, highHz, processedRange),
                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                    processed, SampleRate, probeLowHz, highHz, processedRange));
        }
    }

    // For a driver roll-off the allowance must cover the skew, or the probe convicts a channel for its own crossover.
    [Theory]
    [InlineData("driver HP 60")]
    [InlineData("driver HP 120")]
    public void ArrivalProbeTolerance_CoversARealisticShapedSkew(string sourceName)
    {
        foreach ((double lowHz, double highHz, double honestSkewMs,
            double toleranceMs, _) in TolerancesFor(sourceName))
        {
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

    private static readonly ConcurrentDictionary<string, Lazy<(double LowHz, double HighHz,
        double HonestSkewMs, double ToleranceMs, double BaseToleranceMs)[]>> Tolerances = new();

    private static (double LowHz, double HighHz, double HonestSkewMs,
        double ToleranceMs, double BaseToleranceMs)[] TolerancesFor(string sourceName) =>
        Tolerances.GetOrAdd(sourceName, name => new(() => ToleranceCases()
            .Select(c =>
            {
                (double honestSkewMs, double toleranceMs, double baseToleranceMs) =
                    MeasureTolerance(name, c.Chain, c.LowHz, c.HighHz);
                return (c.LowHz, c.HighHz, honestSkewMs, toleranceMs, baseToleranceMs);
            })
            .ToArray())).Value;

    private static (double HonestSkewMs, double ToleranceMs, double BaseToleranceMs)
        MeasureTolerance(
            string sourceName, DspChannelChain chain, double lowHz, double highHz)
    {
        double probeLowHz = Math.Sqrt(lowHz * highHz);
        (AlignmentSnapshot clean, double fullMs) = Shaped(
            SourceNamed(sourceName), chain, lowHz, highHz);
        double probeMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            clean.ImpulseResponse, SampleRate, probeLowHz, highHz,
            clean.ValidRange).FirstArrivalDelayMilliseconds;
        return (
            fullMs - probeMs,
            AutoAlignmentEngine.ArrivalProbeToleranceMs(
                clean, fullMs, probeMs, lowHz, probeLowHz, highHz),
            Math.Max(1.0, 500.0 / probeLowHz));
    }

    // The credit excuses a channel's own dispersion, never a room mode.
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
            impulse, SourceNamed(sourceName), SampleRate, SampleRate);
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
            withMode, chain, SampleRate, SampleRate);
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
                    snapshot, full.FirstArrivalDelayMilliseconds,
                    probe.FirstArrivalDelayMilliseconds,
                    LowHz, probeLowHz, HighHz)));
    }

    // The timeline stores the DIFFERENCE of the two sides' residuals; the pair anchor is only as good as that.
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

        Assert.True(differentialResidualMs < 1.0,
            $"differential residual {differentialResidualMs:0.000} ms");
    }
}
