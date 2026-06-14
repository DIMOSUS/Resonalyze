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
        ImpulseResponce = impulseResponse;
        SampleRate = sampleRate;
        MaxMagnitudeInd = maxMagnitudeIndex;
        this.harmonicOffset = harmonicOffset ?? (_ => 0);
    }

    public Complex[]? ImpulseResponce { get; }
    public int MaxMagnitudeInd { get; }
    public int SampleRate { get; }

    public double HarmonicIROffset(double harmonic) => harmonicOffset(harmonic);
}
