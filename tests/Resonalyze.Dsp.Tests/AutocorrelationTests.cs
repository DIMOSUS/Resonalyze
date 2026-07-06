using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class AutocorrelationTests
{
    private const int SampleRate = 48_000;
    private const int WindowLength = 2048;
    private const int PeakOffset = 64;

    [Fact]
    public void GetAutocorrelation_MatchesDirectComputationAtIntegerLags()
    {
        double[] signal = BuildDecayingTone(frequencyHz: 1000);
        AnalysisCurve curve = DataHelper.GetAutocorrelation(
            BuildMeasurement(signal),
            new ImpulseResponseOptions());

        double[] expected = DirectNormalizedAutocorrelation(signal);

        // The curve carries 10 sub-sample points per integer lag; index k * 10 is
        // the integer lag k.
        int lagCount = Math.Min(50, curve.Points.Count / 10);
        for (int k = 0; k < lagCount; k++)
        {
            Assert.Equal(expected[k], curve.Points[k * 10].Y, precision: 9);
        }
    }

    [Fact]
    public void GetAutocorrelation_IsUnityAtLagZero()
    {
        double[] signal = BuildDecayingTone(frequencyHz: 440);
        AnalysisCurve curve = DataHelper.GetAutocorrelation(
            BuildMeasurement(signal),
            new ImpulseResponseOptions());

        Assert.Equal(1.0, curve.Points[0].Y, precision: 9);
        Assert.Equal(0.0, curve.Points[0].X, precision: 9);
    }

    [Fact]
    public void GetAutocorrelation_PeriodicSignalPeaksAtItsPeriod()
    {
        const double frequencyHz = 1000;
        double[] signal = BuildDecayingTone(frequencyHz, decay: 0.0);
        AnalysisCurve curve = DataHelper.GetAutocorrelation(
            BuildMeasurement(signal),
            new ImpulseResponseOptions());

        double periodMs = 1000.0 / frequencyHz;
        SignalPoint nearPeriod = curve.Points
            .Where(point => Math.Abs(point.X - periodMs) < 0.05)
            .MaxBy(point => point.Y);

        Assert.True(
            nearPeriod.Y > 0.9,
            $"Expected a strong correlation peak at ~{periodMs} ms, got {nearPeriod.Y}.");
    }

    private static double[] BuildDecayingTone(double frequencyHz, double decay = 500.0)
    {
        var signal = new double[WindowLength];
        for (int i = 0; i < WindowLength; i++)
        {
            double t = i / (double)SampleRate;
            signal[i] = Math.Sin(Math.Tau * frequencyHz * t) * Math.Exp(-decay * t);
        }

        return signal;
    }

    // The measurement window starts at PeakIndex - 64, so placing the peak at 64
    // makes GetAutocorrelation analyze exactly the provided signal.
    private static SyntheticMeasurement BuildMeasurement(double[] signal)
    {
        var impulse = new Complex[WindowLength + PeakOffset];
        for (int i = 0; i < signal.Length; i++)
        {
            impulse[i] = new Complex(signal[i], 0.0);
        }

        return new SyntheticMeasurement(impulse, SampleRate, PeakOffset);
    }

    private static double[] DirectNormalizedAutocorrelation(double[] signal)
    {
        int length = signal.Length;
        double mean = signal.Average();

        double denominator = 0;
        for (int i = 0; i < length; i++)
        {
            denominator += (signal[i] - mean) * (signal[i] - mean);
        }

        var correlation = new double[length];
        for (int k = 0; k < length; k++)
        {
            double numerator = 0;
            for (int i = 0; i < length - k; i++)
            {
                numerator += (signal[i] - mean) * (signal[i + k] - mean);
            }

            correlation[k] = numerator / denominator;
        }

        return correlation;
    }
}
