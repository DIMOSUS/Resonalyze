using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class AllPassFilterTests
{
    private static readonly double[] SampleRates = { 44_100, 48_000, 96_000 };
    private static readonly double[] Corners = { 20, 80, 200, 1_000, 5_000, 10_000 };
    private static readonly double[] Qs = { 0.1, 0.5, 1.0, 2.0, 5.0, 20.0 };

    [Fact]
    public void Magnitude_IsUnityEverywhere_AcrossTheMatrix()
    {
        // The RBJ numerator is the reversed denominator, so |H| = 1 holds analytically: exact assert.
        var failures = new List<string>();
        foreach (double sampleRate in SampleRates)
        {
            foreach (double corner in Corners)
            {
                if (corner >= sampleRate * 0.5)
                {
                    continue;
                }

                foreach (double q in Qs)
                {
                    foreach (AllPassType type in
                        new[] { AllPassType.FirstOrder, AllPassType.SecondOrder })
                    {
                        var spec = new AllPassSpec(type, corner, q);
                        foreach (double f in EqualizationCurve.LogFrequencyGrid(
                            20, Math.Min(20_000, sampleRate * 0.49), 200))
                        {
                            double magnitude =
                                AllPassFilter.Response(spec, f, sampleRate).Magnitude;
                            if (Math.Abs(magnitude - 1.0) > 1e-9)
                            {
                                failures.Add(
                                    $"{type} fs={sampleRate} f0={corner} Q={q} @ {f:0} Hz: " +
                                    $"|H| = {magnitude:0.000000000}");
                            }
                        }
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\r\n", failures.Take(10)));
    }

    [Fact]
    public void SecondOrder_IsMinus180DegreesAtTheCorner_AcrossTheMatrix()
    {
        // H(f0) = -1 compared as a complex value to dodge the ±pi wrap. Looser tolerance: at 20 Hz on 96 kHz with high Q
        // the direct-form biquad loses ~9 digits (alpha ~ 3e-5); |H| survives, phase does not.
        const double tolerance = 1e-7;
        var failures = new List<string>();
        foreach (double sampleRate in SampleRates)
        {
            foreach (double corner in Corners)
            {
                if (corner >= sampleRate * 0.5)
                {
                    continue;
                }

                foreach (double q in Qs)
                {
                    Complex h = AllPassFilter.Response(
                        new AllPassSpec(AllPassType.SecondOrder, corner, q),
                        corner,
                        sampleRate);
                    if (Math.Abs(h.Real + 1.0) > tolerance ||
                        Math.Abs(h.Imaginary) > tolerance)
                    {
                        failures.Add(
                            $"fs={sampleRate} f0={corner} Q={q}: H = {h} (expected -1)");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\r\n", failures.Take(10)));
    }

    [Fact]
    public void FirstOrder_IsMinus90DegreesAtTheCorner_AcrossTheMatrix()
    {
        var failures = new List<string>();
        foreach (double sampleRate in SampleRates)
        {
            foreach (double corner in Corners)
            {
                if (corner >= sampleRate * 0.5)
                {
                    continue;
                }

                Complex h = AllPassFilter.Response(
                    new AllPassSpec(AllPassType.FirstOrder, corner),
                    corner,
                    sampleRate);
                if (Math.Abs(h.Real) > 1e-9 || Math.Abs(h.Imaginary + 1.0) > 1e-9)
                {
                    failures.Add($"fs={sampleRate} f0={corner}: H = {h} (expected -j)");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\r\n", failures.Take(10)));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(5.0)]
    public void SecondOrder_GroupDelayAtCorner_MatchesTheAnalogIdeal(double q)
    {
        // Well below Nyquist the digital filter must match the analog tau(f0) = 4Q/w0.
        const double sampleRate = 48_000;
        const double corner = 100;
        double expected = 4.0 * q / (Math.Tau * corner);

        double actual = AllPassFilter.GroupDelaySeconds(
            new AllPassSpec(AllPassType.SecondOrder, corner, q), corner, sampleRate);

        Assert.True(
            Math.Abs(actual - expected) < expected * 0.01,
            $"Q={q}: group delay {actual * 1000:0.000} ms, expected {expected * 1000:0.000} ms.");
    }

    [Fact]
    public void FirstOrder_GroupDelayAtCorner_MatchesTheAnalogIdeal()
    {
        const double sampleRate = 48_000;
        const double corner = 100;
        double expected = 1.0 / (Math.Tau * corner);

        double actual = AllPassFilter.GroupDelaySeconds(
            new AllPassSpec(AllPassType.FirstOrder, corner), corner, sampleRate);

        Assert.True(
            Math.Abs(actual - expected) < expected * 0.01,
            $"Group delay {actual * 1000:0.000} ms, expected {expected * 1000:0.000} ms.");
    }

    [Theory]
    [InlineData(AllPassType.FirstOrder, 100, 1.0)]
    [InlineData(AllPassType.FirstOrder, 23_900, 1.0)]
    [InlineData(AllPassType.SecondOrder, 100, 1.0)]
    [InlineData(AllPassType.SecondOrder, 21_600, 5.0)]
    [InlineData(AllPassType.SecondOrder, 23_900, 5.0)]
    [InlineData(AllPassType.SecondOrder, 23_900, 20.0)]
    [InlineData(AllPassType.SecondOrder, 23_952, 20.0)]
    public void GroupDelay_IntegratesToTheStagesTotalPhaseSwing(
        AllPassType type, double corner, double q)
    {
        // An order-N all-pass sweeps exactly N·pi from DC to Nyquist, so its delay integral is pinned for any corner and Q.
        const double sampleRate = 48_000;
        double expected = (type == AllPassType.SecondOrder ? 2.0 : 1.0) * Math.PI;
        var spec = new AllPassSpec(type, corner, q);

        // Half-weighted endpoints matter: the delay at DC and Nyquist is sizeable.
        double TauSamples(double omega) => AllPassFilter.GroupDelaySeconds(
            spec, omega * sampleRate / Math.Tau, sampleRate) * sampleRate;

        const int steps = 200_000;
        double sum = (TauSamples(0) + TauSamples(Math.PI)) / 2.0;
        for (int i = 1; i < steps; i++)
        {
            sum += TauSamples(Math.PI * i / steps);
        }

        double integral = sum * Math.PI / steps;
        Assert.True(
            Math.Abs(integral - expected) < 1e-3,
            $"{type} f0={corner} Q={q}: integral {integral:0.0000}, expected {expected:0.0000}.");
    }

    [Theory]
    // 0.45/0.98/0.998 of Nyquist: a fixed-step phase difference wrapped (-5.2 ms for 31.8, -0.6 ms for 265).
    [InlineData(10_800, 5.0, 0.4)]
    [InlineData(10_800, 20.0, 1.6)]
    [InlineData(23_520, 5.0, 6.0)]
    [InlineData(23_520, 20.0, 25.0)]
    [InlineData(23_952, 5.0, 60.0)]
    [InlineData(23_952, 20.0, 250.0)]
    public void SecondOrder_GroupDelayNearNyquist_IsNeverWrappedAway(
        double corner, double q, double atLeastMs)
    {
        const double sampleRate = 48_000;

        double actual = AllPassFilter.GroupDelaySeconds(
            new AllPassSpec(AllPassType.SecondOrder, corner, q), corner, sampleRate) * 1_000.0;

        Assert.True(
            actual > atLeastMs,
            $"f0={corner} Q={q}: {actual:0.000} ms, expected more than {atLeastMs} ms.");
    }

    [Fact]
    public void CornerGroupDelay_ReadsTheClampedCorner_NotTheRequestedOne()
    {
        // A corner at/above Nyquist is clamped, not off: the readout must follow the section (the old guard said 0 ms).
        const double sampleRate = 48_000;
        var atNyquist = new AllPassSpec(AllPassType.SecondOrder, 24_000, 20.0);
        var atTheClamp = new AllPassSpec(
            AllPassType.SecondOrder, sampleRate * 0.499, 20.0);

        double actual = AllPassFilter.CornerGroupDelaySeconds(atNyquist, sampleRate);

        Assert.Equal(
            AllPassFilter.CornerGroupDelaySeconds(atTheClamp, sampleRate), actual, 9);
        Assert.True(actual > 0.2, $"Expected a huge delay, got {actual * 1000:0.0} ms.");
    }

    [Fact]
    public void GroupDelay_AgreesWithThePreparedChain()
    {
        const int sampleRate = 48_000;
        foreach (double corner in new[] { 100.0, 1_000.0, 21_600.0, 23_900.0 })
        {
            foreach (double q in new[] { 0.5, 5.0, 20.0 })
            {
                var spec = new AllPassSpec(AllPassType.SecondOrder, corner, q);
                PreparedDspResponse prepared = PreparedDspResponse.Create(
                    new DspChannelChain(Peq: new EqualizationCurve(new[]
                    {
                        new PeqBand(corner, q, 0, PeqBandType.AllPassSecondOrder)
                    })),
                    sampleRate);

                double readout = AllPassFilter.GroupDelaySeconds(spec, corner, sampleRate);
                double plot = prepared.GroupDelayMs(corner) / 1_000.0;

                Assert.Equal(readout, plot, 12);
            }
        }
    }

    [Fact]
    public void SecondOrder_GroupDelayGrowsWithQ()
    {
        const double sampleRate = 48_000;
        const double corner = 100;
        double low = AllPassFilter.GroupDelaySeconds(
            new AllPassSpec(AllPassType.SecondOrder, corner, 0.5), corner, sampleRate);
        double high = AllPassFilter.GroupDelaySeconds(
            new AllPassSpec(AllPassType.SecondOrder, corner, 4.0), corner, sampleRate);

        Assert.True(high > low * 4, $"Q 0.5 -> {low * 1000:0.00} ms, Q 4 -> {high * 1000:0.00} ms.");
    }

    [Fact]
    public void Off_BuildsNothingAndIsTransparent()
    {
        var spec = new AllPassSpec(AllPassType.Off, 1_000);

        Assert.Empty(AllPassFilter.BuildSections(spec, 48_000));
        Assert.Equal(Complex.One, AllPassFilter.Response(spec, 1_000, 48_000));
        Assert.Equal(0, AllPassFilter.GroupDelaySeconds(spec, 1_000, 48_000));
    }

    [Fact]
    public void FirstOrder_IgnoresQ()
    {
        // First order has no Q: an absurd Q must neither throw nor change the response.
        Complex withDefault = AllPassFilter.Response(
            new AllPassSpec(AllPassType.FirstOrder, 1_000), 500, 48_000);
        Complex withNonsense = AllPassFilter.Response(
            new AllPassSpec(AllPassType.FirstOrder, 1_000, double.NaN), 500, 48_000);

        Assert.Equal(withDefault.Real, withNonsense.Real, 12);
        Assert.Equal(withDefault.Imaginary, withNonsense.Imaginary, 12);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void BuildSections_RejectsInvalidSecondOrderQ(double q)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AllPassFilter.BuildSections(
                new AllPassSpec(AllPassType.SecondOrder, 1_000, q), 48_000));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-100.0)]
    [InlineData(double.NaN)]
    public void BuildSections_RejectsInvalidFrequency(double frequencyHz)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AllPassFilter.BuildSections(
                new AllPassSpec(AllPassType.SecondOrder, frequencyHz), 48_000));
    }

    [Fact]
    public void BuildSections_ClampsACornerAboveNyquist()
    {
        // The prewarp tangent blows up at Nyquist, so the corner clamps just below.
        IReadOnlyList<BiquadCoefficients> sections = AllPassFilter.BuildSections(
            new AllPassSpec(AllPassType.SecondOrder, 24_000, 1.0), 48_000);

        Assert.All(sections, section =>
        {
            Assert.True(double.IsFinite(section.B0));
            Assert.True(double.IsFinite(section.B1));
            Assert.True(double.IsFinite(section.A1));
            Assert.True(double.IsFinite(section.A2));
        });
    }

    [Fact]
    public void Chain_AppliesTheAllPassBandWithTheCrossoverOff()
    {
        var chain = new DspChannelChain(
            Peq: new EqualizationCurve(new[]
            {
                new PeqBand(1_000, 1.0, 0, PeqBandType.AllPassSecondOrder)
            }),
            Crossover: CrossoverSpec.Off);

        Complex response = chain.Response(1_000, 48_000);

        Assert.Equal(1.0, response.Magnitude, 9);
        Assert.Equal(-1.0, response.Real, 9);
    }

    [Fact]
    public void PreparedResponse_MatchesTheAnalyticChain()
    {
        // The analytic chain and the prepared biquad cascade must both wire every stage.
        var chain = new DspChannelChain(
            GainDb: -3,
            DelayMs: 0.5,
            InvertPolarity: true,
            Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)),
            Peq: new EqualizationCurve(new[]
            {
                new PeqBand(120, 1.5, 0, PeqBandType.AllPassSecondOrder)
            }));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, 48_000);

        foreach (double f in EqualizationCurve.LogFrequencyGrid(20, 20_000, 100))
        {
            Complex analytic = chain.Response(f, 48_000);
            Complex fast = prepared.Response(f);
            Assert.True(
                (analytic - fast).Magnitude < 1e-9,
                $"@ {f:0} Hz: analytic {analytic} vs prepared {fast}");
        }
    }
}
