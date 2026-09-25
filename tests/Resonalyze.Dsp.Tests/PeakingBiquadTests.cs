namespace Resonalyze.Dsp.Tests;

public sealed class PeakingBiquadTests
{
    [Fact]
    public void ZeroGain_IsPassthrough()
    {
        BiquadCoefficients c = PeakingBiquad.Compute(new PeqBand(1000, 1.0, 0.0), 48_000);

        Assert.Equal(1.0, c.B0, 6);
        // miniDSP a1 = -a1_rbj and here a1_rbj == b1.
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
    [InlineData(8.0)]
    [InlineData(-8.0)]
    public void DigitalResponse_MatchesTheAnalogPrototypeOffCentre(double gainDb)
    {
        // A wrong alpha = sin(w0)/(2Q) still hits the centre gain; tracking the analog prototype off-centre pins the bandwidth.
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

        double centreDigital = 20.0 * Math.Log10(
            BiquadResponse.Evaluate(coefficients, f0, fs).Magnitude);
        Assert.Equal(gainDb, centreDigital, tolerance: 0.01);
    }

    // A band past Nyquist (a hand-edited bank, a slow custom processor) used to realise with a pole outside the
    // unit circle: a preview of a filter that diverges on the device.
    [Theory]
    [InlineData(PeqBandType.Peaking, 23_000, 44_100)]
    [InlineData(PeqBandType.Peaking, 30_000, 48_000)]
    [InlineData(PeqBandType.HighShelf, 23_000, 44_100)]
    [InlineData(PeqBandType.LowShelf, 30_000, 48_000)]
    public void ABandPastNyquist_RealisesAsAStableSection(PeqBandType type, double frequencyHz, double sampleRateHz)
    {
        BiquadCoefficients c = PeqBiquad.Compute(new PeqBand(frequencyHz, 1.0, -6.0, type), sampleRateHz);

        // Stored negated: the denominator is 1 - A1 z^-1 - A2 z^-2, stable inside the triangle |a2| < 1, |a1| < 1 + a2.
        double a1 = -c.A1;
        double a2 = -c.A2;
        Assert.True(Math.Abs(a2) < 1 && Math.Abs(a1) < 1 + a2, $"a1 {a1}, a2 {a2}");
    }
}
