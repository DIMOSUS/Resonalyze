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
        // The whole point of an all-pass: |H| = 1 at EVERY frequency, not just in the
        // passband. The RBJ numerator is the denominator reversed, so this holds
        // analytically — hence an exact assert rather than a tolerance band.
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
                        // Sweep the whole audible band, not just the corner.
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
        // A second-order all-pass sweeps 360° and sits at exactly -180° on its corner,
        // i.e. H(f0) = -1. Asserting the complex value dodges the ±pi wrap that a raw
        // phase comparison would trip over.
        // The tolerance is looser than the magnitude test's on purpose: at a very low
        // f0/fs (20 Hz on 96 kHz) with a high Q the direct-form biquad is ill-conditioned
        // — alpha ~ 3e-5, so the coefficient sums cancel and ~9 significant digits go.
        // |H| = 1 survives it (numerator and denominator round alike, being mirrored),
        // but the phase does not. 1e-7 here is still a phase within 6e-6 degrees of 180°.
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
        // A first-order all-pass sweeps 180° and sits at exactly -90° on its corner,
        // i.e. H(f0) = -j.
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
        // Well below Nyquist the bilinear warping is negligible, so the digital filter
        // must land on the analog ideal tau(f0) = 4Q/w0. This pins the readout the UI
        // shows AND proves the section is placed at the corner it claims.
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
        // A first-order all-pass has no Q; at its corner tau = 1/w0 (half its DC value).
        const double sampleRate = 48_000;
        const double corner = 100;
        double expected = 1.0 / (Math.Tau * corner);

        double actual = AllPassFilter.GroupDelaySeconds(
            new AllPassSpec(AllPassType.FirstOrder, corner), corner, sampleRate);

        Assert.True(
            Math.Abs(actual - expected) < expected * 0.01,
            $"Group delay {actual * 1000:0.000} ms, expected {expected * 1000:0.000} ms.");
    }

    [Fact]
    public void SecondOrder_GroupDelayGrowsWithQ()
    {
        // Higher Q turns the phase harder and piles up more delay at the corner — the
        // trade-off the UI readout exists to expose.
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
        // A first-order section has one real pole and no Q, so an absurd Q must neither
        // throw nor change the response — the UI greys the field for exactly this reason.
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
        // Q <= 0 divides by zero in alpha and NaN-poisons every coefficient. The DSP
        // refuses it rather than trusting the UI to have clamped.
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
        // The prewarp tangent blows up at Nyquist; the corner is clamped just below,
        // the same way hardware limits its frequency entry.
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
    public void Chain_AppliesTheAllPassWithTheCrossoverOff()
    {
        // Real processors run the all-pass as its own stage, so it must not be gated by
        // the crossover. Magnitude stays flat, phase moves.
        var chain = new DspChannelChain(
            AllPass: new AllPassSpec(AllPassType.SecondOrder, 1_000, 1.0),
            Crossover: CrossoverSpec.Off);

        Complex response = chain.Response(1_000, 48_000);

        Assert.Equal(1.0, response.Magnitude, 9);
        Assert.Equal(-1.0, response.Real, 9);
    }

    [Fact]
    public void PreparedResponse_MatchesTheAnalyticChain()
    {
        // The two response paths (the analytic chain and the prepared biquad cascade)
        // must not drift apart: every stage has to be wired into both.
        var chain = new DspChannelChain(
            GainDb: -3,
            DelayMs: 0.5,
            InvertPolarity: true,
            Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)),
            AllPass: new AllPassSpec(AllPassType.SecondOrder, 120, 1.5));
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
