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
}
