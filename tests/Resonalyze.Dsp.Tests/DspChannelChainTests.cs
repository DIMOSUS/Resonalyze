using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class DspChannelChainTests
{
    private const double SampleRate = 48_000;

    private static double MagnitudeDb(Complex response) =>
        20.0 * Math.Log10(response.Magnitude);

    [Fact]
    public void IdentityChain_IsUnity()
    {
        Complex response = DspChannelChain.Identity.Response(1_000, SampleRate);

        Assert.Equal(1.0, response.Real, 12);
        Assert.Equal(0.0, response.Imaginary, 12);
    }

    [Fact]
    public void Gain_ScalesTheMagnitudeWithoutPhase()
    {
        var chain = new DspChannelChain(GainDb: 6.0);

        Complex response = chain.Response(1_000, SampleRate);

        Assert.Equal(6.0, MagnitudeDb(response), 6);
        Assert.Equal(0.0, response.Phase, 12);
    }

    [Fact]
    public void PolarityInversion_FlipsThePhase()
    {
        var chain = new DspChannelChain(InvertPolarity: true);

        Complex response = chain.Response(1_000, SampleRate);

        Assert.Equal(-1.0, response.Real, 12);
        Assert.Equal(0.0, response.Imaginary, 12);
    }

    [Fact]
    public void Delay_IsALinearPhaseTerm()
    {
        // 0.5 ms at 1 kHz is exactly half a period: the phase term is e^{-j pi}.
        var chain = new DspChannelChain(DelayMs: 0.5);

        Complex response = chain.Response(1_000, SampleRate);

        Assert.Equal(1.0, response.Magnitude, 12);
        Assert.Equal(-1.0, response.Real, 12);
        Assert.Equal(0.0, response.Imaginary, 12);
    }

    [Fact]
    public void Crossover_ShapesTheResponse()
    {
        var chain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24)));

        Assert.Equal(-6.0206, MagnitudeDb(chain.Response(1_000, SampleRate)), 2);
        Assert.Equal(0.0, MagnitudeDb(chain.Response(20, SampleRate)), 2);
    }

    [Fact]
    public void OffCrossover_IsIgnored()
    {
        var chain = new DspChannelChain(Crossover: CrossoverSpec.Off);

        Assert.Equal(1.0, chain.Response(1_000, SampleRate).Magnitude, 12);
    }

    [Fact]
    public void Peq_AppliesBandGainAtItsCenter()
    {
        // The RBJ peaking biquad is designed to hit the band gain exactly at the
        // (prewarped) center frequency.
        var chain = new DspChannelChain(
            Peq: new EqualizationCurve([new PeqBand(1_000, 1.0, 6.0)]));

        Assert.Equal(6.0, MagnitudeDb(chain.Response(1_000, SampleRate)), 4);
    }

    [Fact]
    public void Peq_PreampShiftsEverything()
    {
        var chain = new DspChannelChain(
            Peq: new EqualizationCurve([], preampDb: -3.0));

        Assert.Equal(-3.0, MagnitudeDb(chain.Response(1_000, SampleRate)), 6);
        Assert.Equal(-3.0, MagnitudeDb(chain.Response(10_000, SampleRate)), 6);
    }

    [Fact]
    public void Peq_SkipsDegenerateBands()
    {
        var chain = new DspChannelChain(Peq: new EqualizationCurve(
        [
            new PeqBand(1_000, 0.0, 6.0),
            new PeqBand(0.0, 1.0, 6.0),
            new PeqBand(1_000, 1.0, 0.0)
        ]));

        Complex response = chain.Response(1_000, SampleRate);

        Assert.Equal(1.0, response.Magnitude, 9);
    }

    [Fact]
    public void FullChain_MultipliesEveryStage()
    {
        var chain = new DspChannelChain(
            GainDb: -3.0,
            DelayMs: 0.25,
            InvertPolarity: true,
            Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.Butterworth, 100, 12)),
            Peq: new EqualizationCurve([new PeqBand(5_000, 2.0, 4.0)], preampDb: -1.0));

        Complex expected =
            Math.Pow(10.0, -3.0 / 20.0) * -1.0 *
            Complex.Exp(new Complex(0, -Math.Tau * 1_000 * 0.25 / 1_000.0)) *
            CrossoverFilter.Response(
                new CrossoverSpec(
                    CrossoverKind.HighPass,
                    HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.Butterworth, 100, 12)),
                1_000,
                SampleRate) *
            Math.Pow(10.0, -1.0 / 20.0) *
            BiquadResponse.Evaluate(
                PeakingBiquad.Compute(new PeqBand(5_000, 2.0, 4.0), SampleRate),
                1_000,
                SampleRate);

        Complex actual = chain.Response(1_000, SampleRate);

        Assert.Equal(expected.Real, actual.Real, 10);
        Assert.Equal(expected.Imaginary, actual.Imaginary, 10);
    }

    [Fact]
    public void WeightedMeanGroupDelay_OfAPureDelayIsThatDelay()
    {
        var chain = new DspChannelChain(DelayMs: 3.5);

        Assert.Equal(
            3.5, chain.WeightedMeanGroupDelayMs(100, 400, SampleRate), 6);
    }

    [Fact]
    public void WeightedMeanGroupDelay_IgnoresGainAndPolarity()
    {
        var plain = new DspChannelChain(
            Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 200, 36)));
        DspChannelChain loudAndFlipped =
            plain with { GainDb = -9.0, InvertPolarity = true };

        Assert.Equal(
            plain.WeightedMeanGroupDelayMs(100, 400, SampleRate),
            loudAndFlipped.WeightedMeanGroupDelayMs(100, 400, SampleRate),
            9);
    }

    // The quantity the arrival honesty probe credits: a steep low-pass runs
    // several milliseconds slower BELOW its corner than above it, so a band
    // straddling the corner reads later than its own upper half with no room
    // mode involved. The |H|² weighting is what keeps the figure honest —
    // the stopband above the corner holds the chain's fastest group delay
    // and almost none of its output.
    [Fact]
    public void WeightedMeanGroupDelay_FallsAcrossASteepLowPassCorner()
    {
        var chain = new DspChannelChain(
            Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 200, 36),
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 70, 36)));

        double full = chain.WeightedMeanGroupDelayMs(100, 400, SampleRate);
        double upperHalf = chain.WeightedMeanGroupDelayMs(200, 400, SampleRate);

        Assert.True(full - upperHalf > 1.0,
            $"expected the corner-straddling band to run over 1 ms slower " +
            $"than its upper half; got {full:0.000} vs {upperHalf:0.000} ms");
    }

    [Fact]
    public void WeightedMeanGroupDelay_RefusesADegenerateBand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DspChannelChain.Identity.WeightedMeanGroupDelayMs(
                0, 400, SampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DspChannelChain.Identity.WeightedMeanGroupDelayMs(
                400, 400, SampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DspChannelChain.Identity.WeightedMeanGroupDelayMs(100, 400, 0));
    }
}
