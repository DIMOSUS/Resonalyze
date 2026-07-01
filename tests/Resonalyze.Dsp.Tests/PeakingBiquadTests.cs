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
}
