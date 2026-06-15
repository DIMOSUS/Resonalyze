using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class SyntheticDelayTests
{
    private const int SampleRate = 48_000;
    private const int TransformLength = 4096;
    private const int DelaySamples = 24;

    [Fact]
    public void UnwrappedPhase_HasSlopeMatchingSampleDelay()
    {
        SyntheticMeasurement measurement = CreateDelayedImpulse();
        double[] rectangularWindow = Enumerable.Repeat(1.0, TransformLength).ToArray();

        List<SignalPoint> phase = DataHelper.GetPhaseData(
            measurement,
            offset: 0,
            length: TransformLength,
            window: rectangularWindow,
            unwrap: true);

        List<SignalPoint> analysisBand = phase
            .Where(point => point.X >= 1_000 && point.X <= 18_000)
            .ToList();
        double slope = LinearRegressionSlope(analysisBand);
        double measuredDelaySeconds = -slope / Math.Tau;
        double expectedDelaySeconds = DelaySamples / (double)SampleRate;

        Assert.InRange(
            measuredDelaySeconds,
            expectedDelaySeconds - 1e-10,
            expectedDelaySeconds + 1e-10);
    }

    [Fact]
    public void GroupDelay_RecoversKnownSampleDelay()
    {
        SyntheticMeasurement measurement = CreateDelayedImpulse();

        IReadOnlyList<SignalPoint> groupDelay = DataHelper.GetGroupDelay(
            measurement,
            length: TransformLength,
            leftTukeyWindow: 0,
            rightTukeyWindow: 0,
            offset: 0,
            smoothingInverseOctaves: 96).Points;

        double expectedDelayMilliseconds = DelaySamples * 1000.0 / SampleRate;
        List<SignalPoint> analysisBand = groupDelay
            .Where(point => point.X >= 1_000 && point.X <= 18_000)
            .ToList();

        Assert.NotEmpty(analysisBand);
        Assert.All(
            analysisBand,
            point => Assert.InRange(
                point.Y,
                expectedDelayMilliseconds - 1e-9,
                expectedDelayMilliseconds + 1e-9));
    }

    private static SyntheticMeasurement CreateDelayedImpulse()
    {
        var response = new Complex[TransformLength];
        response[DelaySamples] = Complex.One;
        return new SyntheticMeasurement(response, SampleRate, maxMagnitudeIndex: 0);
    }

    private static double LinearRegressionSlope(IReadOnlyList<SignalPoint> points)
    {
        Assert.NotEmpty(points);

        double averageX = points.Average(point => point.X);
        double averageY = points.Average(point => point.Y);
        double numerator = 0;
        double denominator = 0;

        foreach (SignalPoint point in points)
        {
            double centeredX = point.X - averageX;
            numerator += centeredX * (point.Y - averageY);
            denominator += centeredX * centeredX;
        }

        return numerator / denominator;
    }
}
