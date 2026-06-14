using System.Numerics;

namespace Resonalyze.Dsp;

public interface IImpulseMeasurement
{
    Complex[]? ImpulseResponce { get; }
    int MaxMagnitudeInd { get; }
    int SampleRate { get; }
    double HarmonicIROffset(double harmonic);
}
