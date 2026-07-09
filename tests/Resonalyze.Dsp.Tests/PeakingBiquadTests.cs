namespace Resonalyze.Dsp.Tests;

public sealed class PeakingBiquadTests
{
    [Fact]
    public void ZeroGain_IsPassthrough()
    {
        // A peaking filter with 0 dB gain is a unity passthrough: b0 = 1 and the
        // feedforward/feedback pairs cancel.
        BiquadCoefficients c = PeakingBiquad.Compute(new PeqBand(1000, 1.0, 0.0), 48_000);

        Assert.Equal(1.0, c.B0, 6);
        // miniDSP a1 = -a1_rbj and here a1_rbj == b1, so a1 == -b1.
        Assert.Equal(-c.B1, c.A1, 6);
        Assert.Equal(-c.B2, c.A2, 6);
    }

    [Fact]
    public void Compute_ReturnsFiniteCoefficients()
    {
        BiquadCoefficients c = PeakingBiquad.Compute(new PeqBand(600, 4.0, 6.0), 48_000);

        Assert.True(double.IsFinite(c.B0));
        Assert.True(double.IsFinite(c.B1));
        Assert.True(double.IsFinite(c.B2));
        Assert.True(double.IsFinite(c.A1));
        Assert.True(double.IsFinite(c.A2));
    }

    [Fact]
    public void Compute_RejectsNonPositiveSampleRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PeakingBiquad.Compute(new PeqBand(600, 4.0, 6.0), 0));
    }

    [Theory]
    [InlineData(8.0)]   // boost
    [InlineData(-8.0)]  // cut
    public void DigitalResponse_MatchesTheAnalogPrototypeOffCentre(double gainDb)
    {
        // The bandwidth is set by alpha = sin(w0)/(2Q); a wrong alpha (e.g. dropping
        // the factor of 2) would still hit the centre gain and stay finite, so the
        // passthrough/finite tests miss it. Well below Nyquist the digital filter
        // must track the independent analog peaking prototype off-centre to a few
        // hundredths of a dB — pinning the bandwidth, cross-checked against a
        // formula the biquad code never touches.
        const double fs = 48_000;
        const double f0 = 1_000;
        const double q = 2.0;
        var band = new PeqBand(f0, q, gainDb);
        BiquadCoefficients coefficients = PeakingBiquad.Compute(band, fs);

        foreach (double frequency in new[] { f0 / 2.0, f0, 2.0 * f0 })
        {
            double digitalDb = 20.0 * Math.Log10(
                BiquadResponse.Evaluate(coefficients, frequency, fs).Magnitude);
            Assert.Equal(band.MagnitudeDbAt(frequency), digitalDb, tolerance: 0.3);
        }

        // The centre must sit exactly on the band gain in both formulations.
        double centreDigital = 20.0 * Math.Log10(
            BiquadResponse.Evaluate(coefficients, f0, fs).Magnitude);
        Assert.Equal(gainDb, centreDigital, tolerance: 0.01);
    }
}
