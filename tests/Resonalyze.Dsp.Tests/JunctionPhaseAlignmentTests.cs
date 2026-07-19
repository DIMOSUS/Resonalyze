using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class JunctionPhaseAlignmentTests
{
    private const int SampleRate = 48_000;
    private const double CrossoverHz = 200.0;
    private const double BandLowHz = 100.0;
    private const double BandHighHz = 400.0;

    // A Linkwitz–Riley pair is phase-matched by construction: its low-pass and
    // high-pass legs share one phase response, so the junction of two chains
    // driven by the same impulse is the perfectly aligned reference.
    private static readonly CrossoverSpec LowPass = new(
        CrossoverKind.LowPass,
        LowPassEdge: new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley, CrossoverHz, 24));

    private static readonly CrossoverSpec HighPass = new(
        CrossoverKind.HighPass,
        HighPassEdge: new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley, CrossoverHz, 24));

    private static Complex[] Processed(DspChannelChain chain)
    {
        var impulse = new Complex[8_192];
        impulse[480] = Complex.One;
        return VirtualCrossoverAnalysis.ApplyChain(impulse, chain, SampleRate);
    }

    private static JunctionPhaseResult AnalyzeJunction(
        DspChannelChain lowerChain,
        DspChannelChain upperChain,
        double bandLowHz = BandLowHz,
        double bandHighHz = BandHighHz)
    {
        JunctionPhaseResult? result = JunctionPhaseAlignment.Analyze(
            Processed(lowerChain),
            Processed(upperChain),
            SampleRate,
            CrossoverHz,
            bandLowHz,
            bandHighHz);
        Assert.NotNull(result);
        return result!;
    }

    [Fact]
    public void Analyze_AlignedJunctionReadsInPhaseWithNoCorrection()
    {
        JunctionPhaseResult result = AnalyzeJunction(
            new DspChannelChain(Crossover: LowPass),
            new DspChannelChain(Crossover: HighPass));

        Assert.InRange(result.CurrentScore, 0.95, 1.0);
        Assert.InRange(result.PhaseAtCrossoverDeg, -5.0, 5.0);
        Assert.InRange(result.PhaseConsistency, 0.9, 1.0);
        Assert.InRange(result.BestExtraDelayMs, -0.05, 0.05);
        Assert.False(result.BestInvert);
        Assert.True(result.BestScore >= result.CurrentScore - 1e-9);
        Assert.InRange(result.FitDelayMs, -0.05, 0.05);
    }

    [Fact]
    public void Analyze_PhaseAtCrossoverTracksTheTrueHandoverPhaseUnderABentBand()
    {
        // A deep narrow notch inside the overlap band bends the band's phase —
        // and a straight-line fit's intercept extrapolates that bend into fc
        // (a real mid/tweeter junction read +158° that way while the handover
        // stood near -15°). The LOCAL φ must instead track the true cross-phase
        // of the chains at the crossover, which for clean synthetic inputs is
        // known in closed form from the chain responses.
        var lowerChain = new DspChannelChain(Crossover: LowPass);
        var upperChain = new DspChannelChain(
            Crossover: HighPass,
            Peq: new EqualizationCurve([new PeqBand(300, 20, -30)], 0));

        JunctionPhaseResult result = AnalyzeJunction(lowerChain, upperChain);

        System.Numerics.Complex trueCross =
            lowerChain.Response(CrossoverHz, SampleRate) *
            System.Numerics.Complex.Conjugate(
                upperChain.Response(CrossoverHz, SampleRate));
        double expectedDeg = trueCross.Phase * 180.0 / Math.PI;

        Assert.InRange(
            result.PhaseAtCrossoverDeg, expectedDeg - 5.0, expectedDeg + 5.0);
        Assert.InRange(result.PhaseConsistency, 0.9, 1.0);
    }

    [Fact]
    public void Analyze_LateUpperChannelRecommendsMatchingLowerDelay()
    {
        // The upper channel runs 2 ms late; the fix is to delay the lower one
        // by the same amount. 2 ms is under half the 5 ms crossover period, so
        // the true optimum, not a lobe, must win.
        JunctionPhaseResult result = AnalyzeJunction(
            new DspChannelChain(Crossover: LowPass),
            new DspChannelChain(DelayMs: 2.0, Crossover: HighPass));

        Assert.InRange(result.BestExtraDelayMs, 1.9, 2.1);
        Assert.False(result.BestInvert);
        Assert.InRange(result.BestScore, 0.95, 1.0);
        // The slope fit reads the same misalignment: the lower channel is
        // 2 ms EARLIER, so its residual "later" delay is negative.
        Assert.InRange(result.FitDelayMs, -2.1, -1.9);
        Assert.True(result.CurrentScore < result.BestScore);
    }

    [Fact]
    public void Analyze_InvertedLowerChannelRecommendsAFlipNotADelay()
    {
        JunctionPhaseResult result = AnalyzeJunction(
            new DspChannelChain(InvertPolarity: true, Crossover: LowPass),
            new DspChannelChain(Crossover: HighPass));

        // Half a turn at the crossover and a strongly negative score now.
        Assert.True(Math.Abs(result.PhaseAtCrossoverDeg) > 150.0);
        Assert.True(result.CurrentScore < -0.9);
        // A genuine inversion aligns the whole band flat: the recommendation is
        // a polarity flip at ~zero extra delay, NOT the half-period delay a
        // delay-only search would have chased. The flip beats that delay
        // decisively (a wide band separates the two hypotheses).
        Assert.True(result.BestInvert);
        Assert.InRange(result.BestExtraDelayMs, -0.05, 0.05);
        Assert.InRange(result.BestScore, 0.95, 1.0);
        Assert.True(result.LobeMargin!.Value > 0.1);
    }

    [Fact]
    public void Analyze_HalfPeriodDelayRecommendsADelayNotAFlip()
    {
        // The symmetric case to the inversion test: a normal-polarity channel
        // half a crossover period late reads the SAME ±180° at fc, but here the
        // honest fix is the delay, not a flip. φ alone cannot tell the two
        // apart — the whole-band score must, and it does because a delay
        // realigns the band while a flip would leave it sloping.
        double halfPeriodMs = 0.5 * 1000.0 / CrossoverHz;
        JunctionPhaseResult result = AnalyzeJunction(
            new DspChannelChain(Crossover: LowPass),
            new DspChannelChain(DelayMs: halfPeriodMs, Crossover: HighPass));

        Assert.True(Math.Abs(result.PhaseAtCrossoverDeg) > 150.0);
        Assert.False(result.BestInvert);
        Assert.InRange(result.BestExtraDelayMs, halfPeriodMs - 0.1, halfPeriodMs + 0.1);
        Assert.InRange(result.BestScore, 0.95, 1.0);
        Assert.True(result.LobeMargin!.Value > 0.1);
    }

    [Fact]
    public void Analyze_NarrowBandLowersTheLobeMargin()
    {
        // The same junction read over a whole octave and over a ±10% sliver:
        // the sliver cannot tell whole-period hops apart, so its rival lobe
        // climbs and the margin collapses. This is the resolution physics the
        // margin exists to expose.
        JunctionPhaseResult wide = AnalyzeJunction(
            new DspChannelChain(Crossover: LowPass),
            new DspChannelChain(Crossover: HighPass));
        JunctionPhaseResult narrow = AnalyzeJunction(
            new DspChannelChain(Crossover: LowPass),
            new DspChannelChain(Crossover: HighPass),
            bandLowHz: 180,
            bandHighHz: 220);

        Assert.NotNull(wide.LobeMargin);
        Assert.NotNull(narrow.LobeMargin);
        Assert.True(narrow.LobeMargin!.Value < wide.LobeMargin!.Value);
        Assert.True(wide.LobeMargin.Value > 0.1);
    }

    [Fact]
    public void Analyze_SilentChannelYieldsNull()
    {
        JunctionPhaseResult? result = JunctionPhaseAlignment.Analyze(
            Processed(new DspChannelChain(Crossover: LowPass)),
            new Complex[8_192],
            SampleRate,
            CrossoverHz,
            BandLowHz,
            BandHighHz);

        Assert.Null(result);
    }

    [Fact]
    public void Analyze_BandTooNarrowForAFitYieldsNull()
    {
        // A couple of bins cannot support a slope fit; the junction must
        // refuse instead of fabricating a readout.
        JunctionPhaseResult? result = JunctionPhaseAlignment.Analyze(
            Processed(new DspChannelChain(Crossover: LowPass)),
            Processed(new DspChannelChain(Crossover: HighPass)),
            SampleRate,
            CrossoverHz,
            200.0,
            203.0);

        Assert.Null(result);
    }

    [Fact]
    public void Analyze_SuppressesAJunctionAboveTheRealizableCornerRange()
    {
        // At 44.1 kHz the bilinear transform clamps any corner at/above
        // 0.499·SR ≈ 22 kHz, so a 23 kHz split is realized elsewhere and the
        // read-out must not label a junction with a frequency the DSP cannot
        // produce. (The crossover validator accepts corners up to 24 kHz.)
        const int rate = 44_100;
        var impulse = new Complex[8_192];
        impulse[480] = Complex.One;
        Complex[] lower = VirtualCrossoverAnalysis.ApplyChain(
            impulse,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 23_000, 24))),
            rate);
        Complex[] upper = VirtualCrossoverAnalysis.ApplyChain(
            impulse,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 23_000, 24))),
            rate);

        JunctionPhaseResult? result = JunctionPhaseAlignment.Analyze(
            lower, upper, rate, 23_000, 11_500, 20_000);

        Assert.Null(result);
    }

    [Fact]
    public void AnalyzeSpectra_RejectsForeignSpectrumLengths()
    {
        Assert.Throws<ArgumentException>(() =>
            JunctionPhaseAlignment.AnalyzeSpectra(
                new Complex[1_024],
                new Complex[1_024],
                SampleRate,
                CrossoverHz,
                BandLowHz,
                BandHighHz));
    }

    [Fact]
    public void Analyze_IsInvariantToACommonDelayShift()
    {
        // A delay added to EVERY channel is a pure translation of the scene:
        // the cross-phase, the sweep landscape and the fit must not move.
        // (Field-checked on real cabin IRs with +1.0 and +2.5 ms shifts —
        // every figure held to the last displayed digit.)
        JunctionPhaseResult reference = AnalyzeJunction(
            new DspChannelChain(DelayMs: 0.40, Crossover: LowPass),
            new DspChannelChain(DelayMs: 2.03, Crossover: HighPass));
        JunctionPhaseResult shifted = AnalyzeJunction(
            new DspChannelChain(DelayMs: 1.40, Crossover: LowPass),
            new DspChannelChain(DelayMs: 3.03, Crossover: HighPass));

        Assert.Equal(reference.CurrentScore, shifted.CurrentScore, 3);
        Assert.Equal(reference.BestScore, shifted.BestScore, 3);
        Assert.Equal(reference.BestExtraDelayMs, shifted.BestExtraDelayMs, 2);
        Assert.Equal(reference.BestInvert, shifted.BestInvert);
        Assert.Equal(
            reference.PhaseAtCrossoverDeg, shifted.PhaseAtCrossoverDeg, 0);
        Assert.Equal(reference.PhaseConsistency, shifted.PhaseConsistency, 3);
        Assert.Equal(reference.FitDelayMs, shifted.FitDelayMs, 2);
        Assert.Equal(reference.FitRmsDeg, shifted.FitRmsDeg, 0);
    }

    [Fact]
    public void Analyze_RecommendsTheSameFixAcrossSampleRates()
    {
        // The SAME physical scene — a broadband direct arrival plus a decaying
        // low-frequency modal tail, the upper channel 0.6 ms late — sampled at
        // six rates that straddle two power-of-two FFT-size boundaries (32k and
        // 88.2k double the FFT). With a fixed sample-count window, or with the
        // whole padded FFT used as the analyzed span, the physical window would
        // jump across those boundaries and drift the fix; the time-sized window
        // must recommend the same delay at every rate. Results are compared
        // directly, not just bracketed.
        const double delayMs = 0.6;
        int[] rates = { 32_000, 44_100, 48_000, 88_200, 96_000, 192_000 };
        var fixes = new List<double>();
        foreach (int rate in rates)
        {
            JunctionPhaseResult? result = JunctionPhaseAlignment.Analyze(
                DecayingScene(rate, extraDelayMs: 0),
                DecayingScene(rate, extraDelayMs: delayMs),
                rate, 200, 120, 340);
            Assert.NotNull(result);
            Assert.False(result!.BestInvert);
            fixes.Add(result.BestExtraDelayMs);
        }

        // The whole set agrees to a hundredth of a millisecond — the only
        // residual is the different bin grids sampling the response, not the
        // physical window changing — and every rate lands on the true 0.6 ms.
        Assert.True(fixes.Max() - fixes.Min() < 0.02,
            $"fix spread across rates too large: [{string.Join(", ", fixes.Select(v => v.ToString("0.0000")))}]");
        Assert.All(fixes, value => Assert.InRange(value, delayMs - 0.05, delayMs + 0.05));
    }

    // A one-second IR: a short broadband click at 10 ms, then a 200 Hz mode
    // decaying over ~180 ms — content the analysis window must capture whole
    // for the fix to be stable. extraDelayMs shifts the whole scene later.
    private static Complex[] DecayingScene(int sampleRate, double extraDelayMs)
    {
        var ir = new Complex[sampleRate];
        int start = (int)Math.Round((10.0 + extraDelayMs) * sampleRate / 1000.0);
        // Broadband click (a few-sample triangle) for wideband alignment.
        for (int i = -2; i <= 2; i++)
        {
            int index = start + i;
            if ((uint)index < (uint)ir.Length)
            {
                ir[index] += new Complex(3.0 - Math.Abs(i), 0);
            }
        }
        // Decaying 200 Hz mode.
        double tau = 0.18 * sampleRate;
        int modeLength = (int)(tau * 5);
        for (int i = 0; i < modeLength; i++)
        {
            int index = start + i;
            if ((uint)index >= (uint)ir.Length)
            {
                break;
            }

            ir[index] += new Complex(
                Math.Exp(-i / tau) * Math.Sin(Math.Tau * 200 * i / sampleRate), 0);
        }

        return ir;
    }

    [Fact]
    public void Analyze_TruncatedLongIrStillReadsTheAlignment()
    {
        // IRs longer than the analysis window (every real capture) are
        // tail-faded, not rejected; the readout must survive the crop.
        var longImpulse = new Complex[200_000];
        longImpulse[480] = Complex.One;
        Complex[] lower = VirtualCrossoverAnalysis.ApplyChain(
            longImpulse, new DspChannelChain(Crossover: LowPass), SampleRate);
        Complex[] upper = VirtualCrossoverAnalysis.ApplyChain(
            longImpulse, new DspChannelChain(Crossover: HighPass), SampleRate);

        JunctionPhaseResult? result = JunctionPhaseAlignment.Analyze(
            lower, upper, SampleRate, CrossoverHz, BandLowHz, BandHighHz);

        Assert.NotNull(result);
        Assert.InRange(result!.CurrentScore, 0.95, 1.0);
        Assert.InRange(result.BestExtraDelayMs, -0.05, 0.05);
    }
}
