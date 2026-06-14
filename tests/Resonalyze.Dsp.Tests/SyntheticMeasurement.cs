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

    public double HarmonicIROffset(double harmonic) => harmonicOffset(harmonic);
}
