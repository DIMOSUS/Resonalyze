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
            gateOffsetMs: 0,
            leftMs: 0,
            plateauMs: TransformLength * 1000.0 / SampleRate,
            rightMs: 0,
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

    [Fact]
    public void GroupDelay_MarksBinsMoreThanThirtyDecibelsBelowPeakAsNaN()
    {
        var response = new Complex[TransformLength];
        response[0] = Complex.One;
        response[1] = -Complex.One;
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: 0);

        IReadOnlyList<SignalPoint> groupDelay = DataHelper.GetGroupDelay(
            measurement,
            gateOffsetMs: 0,
            leftMs: 0,
            plateauMs: TransformLength * 1000.0 / SampleRate,
            rightMs: 0,
            smoothingInverseOctaves: 0).Points;

        SignalPoint firstValidPoint =
            groupDelay.First(point => !double.IsNaN(point.Y));

        Assert.InRange(firstValidPoint.X, 450.0, 550.0);
        Assert.All(
            groupDelay.Where(point => point.X < firstValidPoint.X),
            point => Assert.True(double.IsNaN(point.Y)));
    }

    [Fact]
    public void GroupDelay_ReadsAbsoluteDelayFromIrStart()
    {
        // The gate's left shoulder lands on the peak and the buffer offset is added
        // back, so the group delay reads the absolute arrival time of a peak that sits
        // well into the IR (not relative to the peak).
        const int peakSample = 800;
        var response = new Complex[TransformLength];
        response[peakSample] = Complex.One;
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: peakSample);

        IReadOnlyList<SignalPoint> groupDelay = DataHelper.GetGroupDelay(
            measurement,
            gateOffsetMs: peakSample * 1000.0 / SampleRate,
            leftMs: 48 * 1000.0 / SampleRate,
            plateauMs: 256 * 1000.0 / SampleRate,
            rightMs: 64 * 1000.0 / SampleRate,
            smoothingInverseOctaves: 96).Points;

        double expectedDelayMilliseconds = peakSample * 1000.0 / SampleRate;
        List<SignalPoint> analysisBand = groupDelay
            .Where(point => point.X >= 1_000 && point.X <= 18_000)
            .ToList();

        Assert.NotEmpty(analysisBand);
        Assert.All(
            analysisBand,
            point => Assert.InRange(
                point.Y,
                expectedDelayMilliseconds - 1e-6,
                expectedDelayMilliseconds + 1e-6));
    }

    [Fact]
    public void GroupDelay_WrapsWhenLeftShoulderPrecedesIrStart()
    {
        // Peak near the IR start: the left shoulder runs into negative indices, so the
        // gate must read the cyclic tail (wrap) and the time correction must still
        // recover the true absolute arrival.
        const int peakSample = 5;
        var response = new Complex[TransformLength];
        response[peakSample] = Complex.One;
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: peakSample);

        IReadOnlyList<SignalPoint> groupDelay = DataHelper.GetGroupDelay(
            measurement,
            gateOffsetMs: peakSample * 1000.0 / SampleRate,
            leftMs: 48 * 1000.0 / SampleRate, // 48 samples > peak → left shoulder negative
            plateauMs: 256 * 1000.0 / SampleRate,
            rightMs: 64 * 1000.0 / SampleRate,
            smoothingInverseOctaves: 96).Points;

        double expectedDelayMilliseconds = peakSample * 1000.0 / SampleRate;
        List<SignalPoint> analysisBand = groupDelay
            .Where(point => point.X >= 1_000 && point.X <= 18_000)
            .ToList();

        Assert.NotEmpty(analysisBand);
        Assert.All(
            analysisBand,
            point => Assert.InRange(
                point.Y,
                expectedDelayMilliseconds - 1e-6,
                expectedDelayMilliseconds + 1e-6));
    }

    [Fact]
    public void GroupDelay_ReflectionNulls_DoNotSpike()
    {
        // A 0.7 reflection 5 ms behind the arrival combs the spectrum every 200 Hz.
        // At each null the per-bin group delay legitimately diverges to about
        // -aΔ/(1-a) ≈ -11.7 ms, but those bins carry almost no energy: the
        // energy-weighted evaluation must keep the curve near the real arrivals
        // even with display smoothing off.
        const int peakSample = 100;
        const int reflectionDelaySamples = 240; // 5 ms at 48 kHz.
        var response = new Complex[8192];
        response[peakSample] = Complex.One;
        response[peakSample + reflectionDelaySamples] = new Complex(0.7, 0.0);
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: peakSample);

        IReadOnlyList<SignalPoint> groupDelay = DataHelper.GetGroupDelay(
            measurement,
            gateOffsetMs: peakSample * 1000.0 / SampleRate,
            leftMs: 1.0,
            plateauMs: 2.0,
            rightMs: 6.0, // The reflection sits inside the gate.
            smoothingInverseOctaves: 0).Points;

        double arrivalMilliseconds = peakSample * 1000.0 / SampleRate;
        List<SignalPoint> analysisBand = groupDelay
            .Where(point => point.X >= 300 && point.X <= 20_000)
            .Where(point => !double.IsNaN(point.Y))
            .ToList();

        Assert.NotEmpty(analysisBand);
        // The legitimate comb oscillation stays within aΔ/(1+a) ≈ +2.1 ms of the
        // arrival; anything approaching the raw -11.7 ms null excursion is a spike.
        Assert.All(
            analysisBand,
            point => Assert.InRange(
                point.Y,
                arrivalMilliseconds - 2.5,
                arrivalMilliseconds + 3.5));
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
