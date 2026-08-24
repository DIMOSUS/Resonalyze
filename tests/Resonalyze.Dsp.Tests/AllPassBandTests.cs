using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The all-pass as a PEQ band type: a band with zero gain that must NOT be
/// transparent, realizing exactly the same section the channel's all-pass stage
/// builds. These are the invariants the move from a per-channel stage into the
/// PEQ bank rests on.
/// </summary>
public sealed class AllPassBandTests
{
    [Fact]
    public void AllPassBand_WithZeroGain_IsNotTransparent()
    {
        // The historical trap: IsTransparent read "GainDb == 0" as "contributes
        // nothing", which silently dropped a phase-only band from the chain, the
        // prepared cascade AND the preview. An all-pass carries no gain by definition.
        Assert.False(new PeqBand(1_000, 1.0, 0, PeqBandType.AllPassSecondOrder).IsTransparent);
        Assert.False(new PeqBand(1_000, 1.0, 0, PeqBandType.AllPassFirstOrder).IsTransparent);

        // A zero-gain bell and shelf stay transparent — that shortcut is correct for
        // every band whose whole effect IS its gain.
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
        // A half-filled slot must still be skippable; only the frequency and Q can
        // silence an all-pass, since its gain never meant anything.
        Assert.True(
            new PeqBand(frequencyHz, q, 0, PeqBandType.AllPassSecondOrder).IsTransparent);
    }

    [Theory]
    [InlineData(PeqBandType.AllPassFirstOrder, AllPassType.FirstOrder)]
    [InlineData(PeqBandType.AllPassSecondOrder, AllPassType.SecondOrder)]
    public void PeqBiquad_RealizesTheSameSectionAsTheChannelStage(
        PeqBandType bandType, AllPassType stageType)
    {
        // The whole migration story: an all-pass band must be bit-for-bit the filter
        // the per-channel stage used to run, or moving a project's stage into the
        // bank changes its sound.
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
        // Switching a +6 dB bell's slot to all-pass may leave the 6 dB behind in the
        // model. The realization must not read it — an all-pass has no gain knob.
        BiquadCoefficients clean = PeqBiquad.Compute(
            new PeqBand(1_000, 2.0, 0, PeqBandType.AllPassSecondOrder), 48_000);
        BiquadCoefficients stale = PeqBiquad.Compute(
            new PeqBand(1_000, 2.0, 6, PeqBandType.AllPassSecondOrder), 48_000);

        Assert.Equal(clean, stale);
    }

    [Fact]
    public void PeqBiquad_DegradesADegenerateAllPassToAPassThrough()
    {
        // The class contract: a hand-edited file must degrade, not throw out of the
        // audio path. The channel stage's builder throws on these — the band variant
        // may meet them (the coefficient export walks every band unfiltered).
        var identity = new BiquadCoefficients(1, 0, 0, 0, 0);

        Assert.Equal(identity, PeqBiquad.Compute(
            new PeqBand(0, 1.0, 0, PeqBandType.AllPassSecondOrder), 48_000));
        Assert.Equal(identity, PeqBiquad.Compute(
            new PeqBand(1_000, 0, 0, PeqBandType.AllPassFirstOrder), 48_000));
    }

    [Fact]
    public void PeqBiquad_KeepsTheNyquistClampForAnAllPassBand()
    {
        // The bell path computes w0 with no clamp; the all-pass path must keep its
        // own — a corner typed at Nyquist would otherwise blow up the prewarp tangent.
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
        // Unity magnitude everywhere is what makes an all-pass an all-pass, and the
        // analog preview model must agree with the digital realization — including
        // when a stale gain is still sitting in the slot.
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
        // The analytic chain with the band in its PEQ must equal the raw all-pass
        // response — phase included. This is the equivalence the project-file
        // migration relied on when the per-channel stage became a band.
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
        // If the band were skipped as transparent, the chain would degrade to a pure
        // scalar and the FFT path would be bypassed — the all-pass would be a silent
        // no-op on the rendered impulse. The section has to be there.
        var chain = new DspChannelChain(
            Peq: new EqualizationCurve(
                new[] { new PeqBand(1_000, 1.0, 0, PeqBandType.AllPassSecondOrder) }));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, 48_000);

        Assert.False(prepared.IsTimeDomainScaleOnly);
        // And at the corner it holds the delay the stage variant holds.
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
        // The conventions restate a bandwidth between half-gain points; an all-pass
        // has none. A stale gain in the slot must not scale its Q on a tuning sheet.
        var band = new PeqBand(1_000, 2.0, 6, PeqBandType.AllPassSecondOrder);

        Assert.Equal(band, PeqQConventions.ToConvention(band, convention));
        Assert.Equal(band, PeqQConventions.ToRbj(band, convention));
    }
}
