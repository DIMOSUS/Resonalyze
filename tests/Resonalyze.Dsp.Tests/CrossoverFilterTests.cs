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
        // The prototype table is normalized to the -3 dB frequency, so every order
        // lands there; the table's four-digit FSF/Q precision leaves ~0.06 dB of slack.
        double lowPassDb = MagnitudeDb(CrossoverFilter.Response(
            LowPass(CrossoverFilterFamily.Bessel, 1_000, slope), 1_000, SampleRate));
        double highPassDb = MagnitudeDb(CrossoverFilter.Response(
            HighPass(CrossoverFilterFamily.Bessel, 1_000, slope), 1_000, SampleRate));

        Assert.Equal(-3.0103, lowPassDb, 0.07);
        Assert.Equal(-3.0103, highPassDb, 0.07);
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
    public void Chebyshev_HitsMinus3DbAtCorner_AcrossTheMatrix()
    {
        // Each section's frequency scale factor is applied in the bilinear (prewarped)
        // domain, so the corner lands at -3 dB for every rate, corner, order, ripple and
        // side — including the high, steep tweeter high-passes a raw digital multiply
        // used to throw many dB off. This matrix is the regression guard for that fix.
        double[] sampleRates = [44_100, 48_000, 96_000];
        double[] corners = [200, 1_000, 5_000, 10_000];
        int[] slopes = [6, 12, 18, 24, 30, 36, 42, 48];
        double[] ripples = [0.1, 0.5, 1.0, 3.0];

        var failures = new List<string>();
        foreach (double sampleRate in sampleRates)
        foreach (double corner in corners)
        {
            if (corner >= sampleRate * 0.5)
            {
                continue;
            }

            foreach (int slope in slopes)
            foreach (double ripple in ripples)
            foreach (bool highPass in new[] { false, true })
            {
                var edge = new CrossoverEdge(CrossoverFilterFamily.Chebyshev, corner, slope, ripple);
                CrossoverSpec spec = highPass
                    ? new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge)
                    : new CrossoverSpec(CrossoverKind.LowPass, edge);
                double db = MagnitudeDb(CrossoverFilter.Response(spec, corner, sampleRate));
                if (Math.Abs(db - -3.0103) > 0.05)
                {
                    failures.Add(
                        $"Fs={sampleRate} Fc={corner} {slope}dB/oct r={ripple} " +
                        $"{(highPass ? "HP" : "LP")} -> {db:0.000} dB");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures.Take(12)));
    }

    [Fact]
    public void Chebyshev_PassbandStaysWithinTheRippleBand()
    {
        // Below the passband edge the magnitude equiripples between 0 and -ripple dB;
        // it never rises above 0 nor dips below the specified ripple.
        const double rippleDb = 1.0;
        CrossoverSpec spec = new(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Chebyshev, 1_000, 24, rippleDb));

        foreach (double frequency in new[] { 50.0, 100.0, 300.0, 500.0, 700.0 })
        {
            double db = MagnitudeDb(CrossoverFilter.Response(spec, frequency, SampleRate));
            Assert.InRange(db, -rippleDb - 0.05, 0.05);
        }
    }

    [Fact]
    public void Chebyshev_HasASteeperKneeThanButterworth()
    {
        // The defining trade: for the same order, the passband ripple buys a steeper
        // transition. Just past the corner a 1 dB Chebyshev is well below Butterworth.
        CrossoverSpec cheby = new(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Chebyshev, 1_000, 24, 1.0));
        CrossoverSpec butter = LowPass(CrossoverFilterFamily.Butterworth, 1_000, 24);

        double chebyDb = MagnitudeDb(CrossoverFilter.Response(cheby, 1_600, SampleRate));
        double butterDb = MagnitudeDb(CrossoverFilter.Response(butter, 1_600, SampleRate));

        Assert.True(
            chebyDb < butterDb - 3.0,
            $"Chebyshev {chebyDb:0.0} dB should sit well below Butterworth {butterDb:0.0} dB past the corner.");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(3.5)]
    [InlineData(6.0)]
    [InlineData(double.NaN)]
    public void BuildSections_RejectsOutOfRangeChebyshevRipple(double ripple)
    {
        // A ripple above 10·log10(2) ≈ 3.01 dB makes acosh(1/ε) undefined and poisons
        // the coefficients with NaN, so the DSP refuses it up front rather than trusting
        // the UI to have clamped. Below/at the cap the filter builds fine.
        Assert.Throws<ArgumentOutOfRangeException>(() => CrossoverFilter.BuildSections(
            new CrossoverEdge(CrossoverFilterFamily.Chebyshev, 1_000, 24, ripple),
            highPass: false,
            SampleRate));

        var atCap = CrossoverFilter.BuildSections(
            new CrossoverEdge(
                CrossoverFilterFamily.Chebyshev, 1_000, 24, CrossoverFilter.MaximumChebyshevRippleDb),
            highPass: false,
            SampleRate);
        Assert.All(atCap, section => Assert.True(double.IsFinite(section.B0)));
    }

    [Fact]
    public void SupportedSlopes_ChebyshevOffersOddOrders_BesselDoesNot()
    {
        // Chebyshev (and Butterworth) are computed for any order; Bessel is table-bound
        // to even-ish orders with no 5th/7th-order (30/42 dB/oct) entry.
        Assert.Contains(30, CrossoverFilter.SupportedSlopes(CrossoverFilterFamily.Chebyshev));
        Assert.Contains(42, CrossoverFilter.SupportedSlopes(CrossoverFilterFamily.Chebyshev));
        Assert.DoesNotContain(30, CrossoverFilter.SupportedSlopes(CrossoverFilterFamily.Bessel));
        Assert.DoesNotContain(42, CrossoverFilter.SupportedSlopes(CrossoverFilterFamily.Bessel));
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
