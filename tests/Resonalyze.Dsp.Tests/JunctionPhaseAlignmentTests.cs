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
        Assert.InRange(result.BestExtraDelayMs, -0.05, 0.05);
        Assert.True(result.BestScore >= result.CurrentScore - 1e-9);
        Assert.InRange(result.FitDelayMs, -0.05, 0.05);
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
        Assert.InRange(result.BestScore, 0.95, 1.0);
        // The slope fit reads the same misalignment: the lower channel is
        // 2 ms EARLIER, so its residual "later" delay is negative.
        Assert.InRange(result.FitDelayMs, -2.1, -1.9);
        Assert.True(result.CurrentScore < result.BestScore);
    }

    [Fact]
    public void Analyze_InvertedLowerChannelReadsHalfTurnNotADelay()
    {
        JunctionPhaseResult result = AnalyzeJunction(
            new DspChannelChain(InvertPolarity: true, Crossover: LowPass),
            new DspChannelChain(Crossover: HighPass));

        // The phase read-out must say "polarity", loudly: half a turn at the
        // crossover and a strongly negative coherence at the current settings.
        Assert.True(Math.Abs(result.PhaseAtCrossoverDeg) > 150.0);
        Assert.True(result.CurrentScore < -0.9);
        // The best a pure delay can do against a flip is a half-period shift.
        Assert.InRange(Math.Abs(result.BestExtraDelayMs), 1.5, 3.5);
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
    public void Analyze_TruncatedLongIrStillReadsTheAlignment()
    {
        // IRs longer than the analysis window (every real capture) are
        // tail-faded, not rejected; the readout must survive the crop.
        var longImpulse = new Complex[65_536];
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
