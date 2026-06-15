using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class SyntheticFilterTests
{
    private const int SampleRate = 48_000;
    private const int TransformLength = 4096;

    [Theory]
    [InlineData(6_000)]
    [InlineData(12_000)]
    [InlineData(18_000)]
    public void TwoTapAveragingFilter_MatchesAnalyticalFrequencyResponse(double frequency)
    {
        var response = new Complex[TransformLength];
        response[0] = new Complex(0.5, 0);
        response[1] = new Complex(0.5, 0);
        var measurement = new SyntheticMeasurement(response, SampleRate, maxMagnitudeIndex: 0);

        List<SignalPoint> spectrum = DataHelper.GetSpectrumData(
            measurement,
            start: 0,
            length: TransformLength);

        SignalPoint measuredPoint = spectrum.Single(point => Math.Abs(point.X - frequency) < 1e-9);
        double expectedMagnitude = Math.Abs(Math.Cos(Math.PI * frequency / SampleRate));
        double expectedDecibels = DataHelper.AmplitudeToDecibels(expectedMagnitude);

        Assert.InRange(
            measuredPoint.Y,
            expectedDecibels - 1e-10,
            expectedDecibels + 1e-10);
    }
}
