using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// Provides the impulse-response data and timing metadata required by DSP projections.
/// </summary>
public interface IImpulseMeasurement
{
    Complex[]? ImpulseResponse { get; }
    int PeakIndex { get; }
    int SampleRate { get; }

    /// <summary>
    /// Returns the sample offset of a harmonic impulse relative to the linear response.
    /// </summary>
    double HarmonicIROffset(double harmonic);
}
