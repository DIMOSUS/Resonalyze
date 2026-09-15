using System.Numerics;

namespace Resonalyze.Dsp;

public interface IImpulseMeasurement
{
    Complex[]? ImpulseResponse { get; }
    int PeakIndex { get; }
    int SampleRate { get; }

    double HarmonicIROffset(double harmonic);

    /// <summary>0 = whole band. Set after dividing out a protective high-pass: below it bins are zeroed and a windowed spectrum shows only leakage.</summary>
    double LowestMeasuredFrequencyHz => 0.0;

    /// <summary>Infinity = whole band. Set when a band sweep stopped short and the response is zeroed above it.</summary>
    double HighestMeasuredFrequencyHz => double.PositiveInfinity;
}
