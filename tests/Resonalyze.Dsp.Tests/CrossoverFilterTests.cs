using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class CrossoverFilterTests
{
    private const double SampleRate = 48_000;

    private static double MagnitudeDb(Complex response) =>
        20.0 * Math.Log10(response.Magnitude);

    private static CrossoverSpec LowPass(
        CrossoverFilterFamily family,
        double frequencyHz,
        int slope) =>
        new(CrossoverKind.LowPass, new CrossoverEdge(family, frequencyHz, slope));

    private static CrossoverSpec HighPass(
        CrossoverFilterFamily family,
        double frequencyHz,
        int slope) =>
        new(CrossoverKind.HighPass, HighPassEdge: new CrossoverEdge(family, frequencyHz, slope));

    [Fact]
    public void OffCrossover_IsUnity()
    {
        Complex response = CrossoverFilter.Response(CrossoverSpec.Off, 1_000, SampleRate);

        Assert.Equal(1.0, response.Real, 12);
        Assert.Equal(0.0, response.Imaginary, 12);
    }

    [Fact]
    public void MaxGroupDelay_GrowsWithOrderAndFallsWithFrequency()
    {
        var lr24At250 = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 250, 24);
        var lr48At250 = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 250, 48);
        var lr48At75 = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 75, 48);

        double gd24 = CrossoverFilter.MaxGroupDelaySeconds(lr24At250, highPass: false, SampleRate);
        double gd48 = CrossoverFilter.MaxGroupDelaySeconds(lr48At250, highPass: false, SampleRate);
        double gd48Low = CrossoverFilter.MaxGroupDelaySeconds(lr48At75, highPass: false, SampleRate);

        // A steeper slope adds more delay; the same slope lower down adds much
        // more (group delay scales ≈ 1/f_c, so 250 → 75 Hz is ≈ 3.3×).
        Assert.True(gd48 > gd24);
        Assert.True(gd48Low > gd48);
        Assert.Equal(250.0 / 75.0, gd48Low / gd48, 1);

        // Actual figures the auto-crossover budget is calibrated against.
        Assert.InRange(gd48 * 1000, 4.0, 6.0);   // ~5 ms — fine at a woofer/mid handover
        Assert.InRange(gd48Low * 1000, 15.0, 18.0); // ~17 ms — too much at a sub/woofer handover
    }

    [Fact]
    public void MaxGroupDelay_IsTheSameForLowPassAndHighPass()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 36);
        double lowPass = CrossoverFilter.MaxGroupDelaySeconds(edge, highPass: false, SampleRate);
        double highPass = CrossoverFilter.MaxGroupDelaySeconds(edge, highPass: true, SampleRate);

        Assert.Equal(lowPass, highPass, 4);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(18)]
    [InlineData(24)]
    [InlineData(36)]
    [InlineData(48)]
    public void Butterworth_IsMinus3DbAtCorner(int slope)
    {
        // The RBJ sections prewarp at the corner, so every Butterworth order sits
        // exactly at -3.01 dB there — low-pass and high-pass alike.
        double lowPassDb = MagnitudeDb(CrossoverFilter.Response(
            LowPass(CrossoverFilterFamily.Butterworth, 1_000, slope), 1_000, SampleRate));
        double highPassDb = MagnitudeDb(CrossoverFilter.Response(
            HighPass(CrossoverFilterFamily.Butterworth, 1_000, slope), 1_000, SampleRate));

        Assert.Equal(-3.0103, lowPassDb, 2);
        Assert.Equal(-3.0103, highPassDb, 2);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(48)]
    public void LinkwitzRiley_IsMinus6DbAtCorner(int slope)
    {
        double lowPassDb = MagnitudeDb(CrossoverFilter.Response(
            LowPass(CrossoverFilterFamily.LinkwitzRiley, 1_000, slope), 1_000, SampleRate));
        double highPassDb = MagnitudeDb(CrossoverFilter.Response(
            HighPass(CrossoverFilterFamily.LinkwitzRiley, 1_000, slope), 1_000, SampleRate));

        Assert.Equal(-6.0206, lowPassDb, 2);
        Assert.Equal(-6.0206, highPassDb, 2);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(24)]
    [InlineData(48)]
    public void Butterworth_RollsOffAtNominalSlope(int slope)
    {
        // One octave well into the stopband (800 Hz -> 1.6 kHz for a 200 Hz corner)
        // attenuates by the nominal slope. The corner is kept low so the bilinear
        // Nyquist compression stays negligible over the measured octave.
        CrossoverSpec spec = LowPass(CrossoverFilterFamily.Butterworth, 200, slope);

        double at800 = MagnitudeDb(CrossoverFilter.Response(spec, 800, SampleRate));
        double at1600 = MagnitudeDb(CrossoverFilter.Response(spec, 1_600, SampleRate));

        Assert.Equal(slope, at800 - at1600, 0.5);
    }

    [Fact]
    public void LinkwitzRiley24_PairSumsToAllpass()
    {
        // The defining LR property: the low-pass and high-pass halves are exactly
        // in phase, so their complex sum has unit magnitude at every frequency.
        // The bilinear transform preserves the algebraic identity, so it holds for
        // the digital cascade too.
        CrossoverSpec lowPass = LowPass(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        CrossoverSpec highPass = HighPass(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);

        foreach (double frequency in new[] { 100.0, 500.0, 1_000.0, 2_000.0, 10_000.0 })
        {
            Complex sum =
                CrossoverFilter.Response(lowPass, frequency, SampleRate) +
                CrossoverFilter.Response(highPass, frequency, SampleRate);
            Assert.Equal(0.0, MagnitudeDb(sum), 6);
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(18)]
    [InlineData(24)]
    [InlineData(36)]
    [InlineData(48)]
    public void Bessel_IsMinus3DbAtCorner(int slope)
    {
        // The prototype table is normalized to the -3 dB frequency, so every
        // order lands there; the table's four-digit precision allows ~0.05 dB.
        double lowPassDb = MagnitudeDb(CrossoverFilter.Response(
            LowPass(CrossoverFilterFamily.Bessel, 1_000, slope), 1_000, SampleRate));
        double highPassDb = MagnitudeDb(CrossoverFilter.Response(
            HighPass(CrossoverFilterFamily.Bessel, 1_000, slope), 1_000, SampleRate));

        Assert.Equal(-3.0103, lowPassDb, 0.05);
        Assert.Equal(-3.0103, highPassDb, 0.05);
    }

    [Fact]
    public void Bessel_ReachesTheNominalSlopeFarIntoTheStopband()
    {
        // Bessel rolls off more gently near the corner than Butterworth, but the
        // asymptotic slope is the same 6 dB/octave per order.
        CrossoverSpec spec = LowPass(CrossoverFilterFamily.Bessel, 200, 24);

        double at1600 = MagnitudeDb(CrossoverFilter.Response(spec, 1_600, SampleRate));
        double at3200 = MagnitudeDb(CrossoverFilter.Response(spec, 3_200, SampleRate));

        Assert.Equal(24, at1600 - at3200, 1.5);
    }

    [Fact]
    public void Bessel_GroupDelayIsFlatterThanButterworth()
    {
        // The defining Bessel property: near-constant group delay through the
        // passband, where Butterworth's delay peaks toward the corner.
        double BesselVariation() => GroupDelayVariation(
            LowPass(CrossoverFilterFamily.Bessel, 1_000, 24));
        double ButterworthVariation() => GroupDelayVariation(
            LowPass(CrossoverFilterFamily.Butterworth, 1_000, 24));

        Assert.True(
            BesselVariation() < ButterworthVariation() / 3,
            $"Bessel GD variation {BesselVariation():0.000} should be well below " +
            $"Butterworth's {ButterworthVariation():0.000}.");
    }

    // Relative group-delay spread across the passband (100..800 Hz for a 1 kHz
    // corner), from the numeric phase derivative.
    private static double GroupDelayVariation(CrossoverSpec spec)
    {
        double GroupDelay(double frequency)
        {
            const double df = 1.0;
            double phaseAbove = CrossoverFilter
                .Response(spec, frequency + df, SampleRate).Phase;
            double phaseBelow = CrossoverFilter
                .Response(spec, frequency - df, SampleRate).Phase;
            double delta = phaseAbove - phaseBelow;
            // Re-wrap the local difference; df is small so a real jump is 2 pi.
            delta = Math.Atan2(Math.Sin(delta), Math.Cos(delta));
            return -delta / (Math.Tau * 2 * df);
        }

        double minDelay = double.PositiveInfinity;
        double maxDelay = double.NegativeInfinity;
        foreach (double frequency in new[] { 100.0, 200.0, 400.0, 600.0, 800.0 })
        {
            double delay = GroupDelay(frequency);
            minDelay = Math.Min(minDelay, delay);
            maxDelay = Math.Max(maxDelay, delay);
        }

        return (maxDelay - minDelay) / maxDelay;
    }

    [Fact]
    public void BandPass_IsTheProductOfItsEdges()
    {
        var highPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 300, 24);
        var lowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 3_000, 18);
        var bandPass = new CrossoverSpec(CrossoverKind.BandPass, lowPassEdge, highPassEdge);

        foreach (double frequency in new[] { 100.0, 300.0, 1_000.0, 3_000.0, 12_000.0 })
        {
            Complex expected =
                CrossoverFilter.Response(
                    new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: highPassEdge),
                    frequency,
                    SampleRate) *
                CrossoverFilter.Response(
                    new CrossoverSpec(CrossoverKind.LowPass, lowPassEdge),
                    frequency,
                    SampleRate);
            Complex actual = CrossoverFilter.Response(bandPass, frequency, SampleRate);

            Assert.Equal(expected.Real, actual.Real, 10);
            Assert.Equal(expected.Imaginary, actual.Imaginary, 10);
        }
    }

    [Fact]
    public void LowPass_VanishesAtNyquist_AndPassesDc()
    {
        CrossoverSpec spec = LowPass(CrossoverFilterFamily.Butterworth, 1_000, 24);

        // The bilinear transform pins a zero at Nyquist — exactly what a real DSP
        // running these biquads does, unlike the analog prototype.
        Assert.Equal(0.0, CrossoverFilter.Response(spec, SampleRate / 2, SampleRate).Magnitude, 9);
        Assert.Equal(1.0, CrossoverFilter.Response(spec, 0, SampleRate).Magnitude, 9);
    }

    [Fact]
    public void BuildSections_RejectsUnsupportedSlopeAndCorner()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CrossoverFilter.BuildSections(
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 18),
            highPass: false,
            SampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrossoverFilter.BuildSections(
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 0, 24),
            highPass: false,
            SampleRate));
    }

    [Fact]
    public void CornerAtOrAboveNyquist_IsClampedToAStableFilter()
    {
        // The UI allows corners up to 24 kHz while a measurement may run at
        // 44.1 kHz; instead of throwing, the corner clamps just below Nyquist —
        // the way DSP hardware limits its frequency entry.
        CrossoverSpec spec = LowPass(CrossoverFilterFamily.Butterworth, 24_000, 24);
        const double sampleRate = 44_100;

        foreach (double frequency in new[] { 100.0, 1_000.0, 10_000.0, 20_000.0 })
        {
            Complex response = CrossoverFilter.Response(spec, frequency, sampleRate);
            Assert.True(double.IsFinite(response.Real));
            Assert.True(double.IsFinite(response.Imaginary));
        }

        // Well below the clamped corner the low-pass still passes cleanly.
        Assert.Equal(0.0, MagnitudeDb(CrossoverFilter.Response(spec, 1_000, sampleRate)), 1);
    }

    [Fact]
    public void Response_ThrowsWhenARequiredEdgeIsMissing()
    {
        var missingLowPass = new CrossoverSpec(CrossoverKind.LowPass);
        var missingHighPass = new CrossoverSpec(CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 24));

        Assert.Throws<InvalidOperationException>(
            () => CrossoverFilter.Response(missingLowPass, 1_000, SampleRate));
        Assert.Throws<InvalidOperationException>(
            () => CrossoverFilter.Response(missingHighPass, 1_000, SampleRate));
    }
}
