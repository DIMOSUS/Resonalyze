using System.Numerics;

namespace Resonalyze.Dsp.Tests;

internal sealed class SyntheticMeasurement : IImpulseMeasurement
{
    private readonly Func<double, double> harmonicOffset;

    public SyntheticMeasurement(
        Complex[] impulseResponse,
        int sampleRate,
        int maxMagnitudeIndex,
        Func<double, double>? harmonicOffset = null)
    {
        ImpulseResponse = impulseResponse;
        SampleRate = sampleRate;
        PeakIndex = maxMagnitudeIndex;
        this.harmonicOffset = harmonicOffset ?? (_ => 0);
    }

    public Complex[]? ImpulseResponse { get; }
    public int PeakIndex { get; }
    public int SampleRate { get; }

    /// <summary>
    /// Where the response stops carrying a measurement; zero unless a test is
    /// standing in for one whose protective high-pass was divided back out.
    /// </summary>
    public double LowestMeasuredFrequencyHz { get; init; }

    /// <summary>The other end of the same thing; infinity unless a test sets it.</summary>
    public double HighestMeasuredFrequencyHz { get; init; } = double.PositiveInfinity;

    public double HarmonicIROffset(double harmonic) => harmonicOffset(harmonic);
}
