using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class SyntheticImpulseTests
{
    [Fact]
    public void ExtractWindow_PreservesImpulseAndZeroPadsOutsideResponse()
    {
        var response = new Complex[8];
        response[3] = new Complex(0.75, 0);
        var measurement = new SyntheticMeasurement(response, sampleRate: 48_000, maxMagnitudeIndex: 3);

        Complex[] window = DataHelper.ExtractWindow(measurement, start: -2, length: 12);

        Assert.Equal(12, window.Length);
        Assert.Equal(0.75, window[5].Real, precision: 12);
        Assert.All(
            window.Where((_, index) => index != 5),
            sample => Assert.Equal(Complex.Zero, sample));
    }

    [Fact]
    public void ExtractWindow_CanWrapOutsideResponse()
    {
        var response = new Complex[8];
        response[7] = new Complex(0.5, 0);
        response[0] = new Complex(1.0, 0);
        var measurement = new SyntheticMeasurement(
            response,
            sampleRate: 48_000,
            maxMagnitudeIndex: 0);

        Complex[] window = DataHelper.ExtractWindow(
            measurement,
            start: -1,
            length: 3,
            wrap: true);

        Assert.Equal(0.5, window[0].Real, precision: 12);
        Assert.Equal(1.0, window[1].Real, precision: 12);
        Assert.Equal(0.0, window[2].Real, precision: 12);
    }
}
