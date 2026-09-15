using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>An all-pass PEQ band has zero gain yet is not transparent, and realizes exactly the channel all-pass stage's section.</summary>
public sealed class AllPassBandTests
{
    [Fact]
    public void AllPassBand_WithZeroGain_IsNotTransparent()
    {
        // IsTransparent once read GainDb == 0 as 'contributes nothing' and dropped phase-only bands.
        Assert.False(new PeqBand(1_000, 1.0, 0, PeqBandType.AllPassSecondOrder).IsTransparent);
        Assert.False(new PeqBand(1_000, 1.0, 0, PeqBandType.AllPassFirstOrder).IsTransparent);

        Assert.True(new PeqBand(1_000, 1.0, 0).IsTransparent);
        Assert.True(new PeqBand(1_000, 1.0, 0, PeqBandType.LowShelf).IsTransparent);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-100.0, 1.0)]
    [InlineData(1_000.0, 0.0)]
    [InlineData(1_000.0, -1.0)]
    public void AllPassBand_WithDegenerateFrequencyOrQ_IsTransparent(
        double frequencyHz, double q)
    {
        // Only frequency and Q can silence an all-pass.
        Assert.True(
            new PeqBand(frequencyHz, q, 0, PeqBandType.AllPassSecondOrder).IsTransparent);
    }

    [Theory]
    [InlineData(PeqBandType.AllPassFirstOrder, AllPassType.FirstOrder)]
    [InlineData(PeqBandType.AllPassSecondOrder, AllPassType.SecondOrder)]
    public void PeqBiquad_RealizesTheSameSectionAsTheChannelStage(
        PeqBandType bandType, AllPassType stageType)
    {
        // Must be bit-identical to the former per-channel stage, or migrated projects change sound.
        const double sampleRate = 48_000;
        foreach (double corner in new[] { 60.0, 500.0, 2_000.0, 10_000.0 })
        {
            foreach (double q in new[] { 0.5, 1.0, 5.0 })
            {
                BiquadCoefficients fromBand = PeqBiquad.Compute(
                    new PeqBand(corner, q, 0, bandType), sampleRate);
                BiquadCoefficients fromStage = AllPassFilter.BuildSections(
                    new AllPassSpec(stageType, corner, q), sampleRate)[0];

                Assert.Equal(fromStage, fromBand);
            }
        }
    }

    [Fact]
    public void PeqBiquad_IgnoresAStaleGainOnAnAllPassBand()
    {
        // A stale gain left by a type switch must not be read.
        BiquadCoefficients clean = PeqBiquad.Compute(
            new PeqBand(1_000, 2.0, 0, PeqBandType.AllPassSecondOrder), 48_000);
        BiquadCoefficients stale = PeqBiquad.Compute(
            new PeqBand(1_000, 2.0, 6, PeqBandType.AllPassSecondOrder), 48_000);

        Assert.Equal(clean, stale);
    }

    [Fact]
    public void PeqBiquad_DegradesADegenerateAllPassToAPassThrough()
    {
        // Hand-edited values must degrade, not throw from the audio path (the coefficient export walks every band).
        var identity = new BiquadCoefficients(1, 0, 0, 0, 0);

        Assert.Equal(identity, PeqBiquad.Compute(
            new PeqBand(0, 1.0, 0, PeqBandType.AllPassSecondOrder), 48_000));
        Assert.Equal(identity, PeqBiquad.Compute(
            new PeqBand(1_000, 0, 0, PeqBandType.AllPassFirstOrder), 48_000));
    }

    [Fact]
    public void PeqBiquad_KeepsTheNyquistClampForAnAllPassBand()
    {
        // The all-pass path keeps its own Nyquist clamp; the bell path has none.
        BiquadCoefficients section = PeqBiquad.Compute(
            new PeqBand(24_000, 1.0, 0, PeqBandType.AllPassSecondOrder), 48_000);

        Assert.True(double.IsFinite(section.B0));
        Assert.True(double.IsFinite(section.B1));
        Assert.True(double.IsFinite(section.A1));
        Assert.True(double.IsFinite(section.A2));
    }

    [Fact]
    public void ToAllPassSpec_RefusesANonAllPassBand()
    {
        Assert.Throws<ArgumentException>(() =>
            PeqBiquad.ToAllPassSpec(new PeqBand(1_000, 1.0, 3)));
    }

    [Fact]
    public void Magnitude_IsExactlyZeroDb_AnalogAndDigital()
    {
        var band = new PeqBand(1_000, 2.0, 6, PeqBandType.AllPassSecondOrder);
        foreach (double f in EqualizationCurve.LogFrequencyGrid(20, 20_000, 100))
        {
            Assert.Equal(0.0, band.MagnitudeDbAt(f));
            Assert.Equal(
                0.0, DigitalEqualizationResponse.MagnitudeDbAt(band, f, 48_000), 9);
        }
    }

    [Fact]
    public void Chain_AllPassBandMatchesTheFilterItReplaced()
    {
        const double sampleRate = 48_000;
        var spec = new AllPassSpec(AllPassType.SecondOrder, 120, 1.5);
        var viaBand = new DspChannelChain(
            Peq: new EqualizationCurve(
                new[] { new PeqBand(120, 1.5, 0, PeqBandType.AllPassSecondOrder) }));

        foreach (double f in EqualizationCurve.LogFrequencyGrid(20, 20_000, 100))
        {
            Complex band = viaBand.Response(f, sampleRate);
            Complex filter = AllPassFilter.Response(spec, f, sampleRate);
            Assert.True(
                (band - filter).Magnitude < 1e-9,
                $"@ {f:0} Hz: band {band} vs filter {filter}");
        }
    }

    [Fact]
    public void PreparedResponse_AllPassBandIsNotAScaleOnlyChain()
    {
        // Skipped as transparent, the chain would take the scalar path and bypass the FFT.
        var chain = new DspChannelChain(
            Peq: new EqualizationCurve(
                new[] { new PeqBand(1_000, 1.0, 0, PeqBandType.AllPassSecondOrder) }));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, 48_000);

        Assert.False(prepared.IsTimeDomainScaleOnly);
        Assert.Equal(
            AllPassFilter.GroupDelaySeconds(
                new AllPassSpec(AllPassType.SecondOrder, 1_000, 1.0), 1_000, 48_000),
            prepared.GroupDelayMs(1_000) / 1_000.0,
            12);
    }

    [Theory]
    [InlineData(PeqQConvention.Symmetric)]
    [InlineData(PeqQConvention.Classic)]
    public void QConventions_LeaveAnAllPassBandAlone(PeqQConvention convention)
    {
        // An all-pass has no half-gain bandwidth: a stale gain must not scale its Q on a sheet.
        var band = new PeqBand(1_000, 2.0, 6, PeqBandType.AllPassSecondOrder);

        Assert.Equal(band, PeqQConventions.ToConvention(band, convention));
        Assert.Equal(band, PeqQConventions.ToRbj(band, convention));
    }
}
