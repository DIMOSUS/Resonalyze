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
    public void GroupDelay_MarksBinsFarBelowTheLocalEnvelopeAsNaN()
    {
        // Validity reads the local octave envelope (a quiet shelf stays valid); below the −60 dB global backstop is silence.
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

        // |H| = 2·sin(πf/fs) crosses −60 dB re Nyquist at f ≈ 15 Hz.
        SignalPoint firstValidPoint =
            groupDelay.First(point => !double.IsNaN(point.Y));
        Assert.InRange(firstValidPoint.X, 5.0, 50.0);
        Assert.All(
            groupDelay.Where(point => point.X < firstValidPoint.X),
            point => Assert.True(double.IsNaN(point.Y)));
        Assert.All(
            groupDelay.Where(point => point.X is > 100 and < 18_000),
            point => Assert.True(double.IsFinite(point.Y)));
    }

    [Fact]
    public void GroupDelay_ReadsAbsoluteDelayFromIrStart()
    {
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
    public void GroupDelay_AQuietBandSurvivesNextToATallResonance()
    {
        // A 50 Hz resonance ~47 dB over the broadband arrival: a global gate would blank the mid band.
        var response = new Complex[65_536];
        response[1_000] = Complex.One;
        for (int i = 0; i < 4_800; i++)
        {
            response[1_000 + i] += new Complex(
                Math.Sin(2.0 * Math.PI * 50.0 * i / SampleRate)
                    * Math.Exp(-i / 480.0),
                0);
        }
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: 1_000);

        IReadOnlyList<SignalPoint> groupDelay = DataHelper.GetGroupDelay(
            measurement,
            gateOffsetMs: 1_000 * 1000.0 / SampleRate,
            leftMs: 5,
            plateauMs: 500,
            rightMs: 500,
            smoothingInverseOctaves: 0).Points;

        List<SignalPoint> midBand = groupDelay
            .Where(point => point.X is >= 500 and <= 5_000)
            .ToList();
        Assert.NotEmpty(midBand);
        double finiteShare =
            midBand.Count(point => double.IsFinite(point.Y)) / (double)midBand.Count;
        Assert.True(
            finiteShare > 0.9,
            $"only {finiteShare:P0} of the mid band survived the gate");
    }

    [Fact]
    public void GatedPhase_ReadsTheCyclicTailLikeGroupDelayDoes()
    {
        // Phase and GD share one gate: a left shoulder before the IR start reads the circular tail in both.
        const int peakSample = 5;
        var clean = new Complex[TransformLength];
        clean[peakSample] = Complex.One;
        var withTail = (Complex[])clean.Clone();
        withTail[^20] = new Complex(0.3, 0); // −20 samples, inside the shoulder

        List<SignalPoint> cleanPhase = DataHelper.GetGatedPhaseData(
            new SyntheticMeasurement(clean, SampleRate, peakSample),
            gateOffsetMs: peakSample * 1000.0 / SampleRate,
            leftMs: 48 * 1000.0 / SampleRate,
            plateauMs: 256 * 1000.0 / SampleRate,
            rightMs: 64 * 1000.0 / SampleRate,
            referenceSamples: 0,
            unwrap: false);
        List<SignalPoint> tailPhase = DataHelper.GetGatedPhaseData(
            new SyntheticMeasurement(withTail, SampleRate, peakSample),
            gateOffsetMs: peakSample * 1000.0 / SampleRate,
            leftMs: 48 * 1000.0 / SampleRate,
            plateauMs: 256 * 1000.0 / SampleRate,
            rightMs: 64 * 1000.0 / SampleRate,
            referenceSamples: 0,
            unwrap: false);

        double maxDifference = cleanPhase
            .Zip(tailPhase, (a, b) => Math.Abs(a.Y - b.Y))
            .Max();
        Assert.True(
            maxDifference > 0.1,
            $"the cyclic tail never reached the phase (max diff {maxDifference:0.0000} rad)");
    }

    [Fact]
    public void GroupDelay_WrapsWhenLeftShoulderPrecedesIrStart()
    {
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
        // Comb nulls push per-bin GD to -aΔ/(1-a) ≈ -11.7 ms with almost no energy; energy weighting keeps the curve near the arrivals.
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
        // The legitimate comb swing stays within aΔ/(1+a) ≈ +2.1 ms.
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
