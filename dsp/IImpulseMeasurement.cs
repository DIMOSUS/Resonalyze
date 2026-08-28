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

    /// <summary>
    /// The lowest frequency this response carries a measurement at. Zero — the
    /// default — means the whole band was measured.
    /// </summary>
    /// <remarks>
    /// Set where a protective high-pass was divided back out: below the frequency
    /// it took the signal past recovering, the compensation zeroed those bins, and
    /// a windowed spectrum of the result draws the window's leakage rather than a
    /// loudspeaker. Frequency-domain analysis stops here — not at a very low level,
    /// which would be a claim, but at nothing, which is the truth.
    /// </remarks>
    double LowestMeasuredFrequencyHz => 0.0;

    /// <summary>
    /// The highest frequency this response carries a measurement at. Infinity — the
    /// default — means the whole band was measured.
    /// </summary>
    /// <remarks>
    /// The other end of <see cref="LowestMeasuredFrequencyHz"/>, and set by the same
    /// thing at the top: a band sweep that stopped short leaves the response zeroed
    /// above it, and a windowed spectrum of a zero is the window.
    /// </remarks>
    double HighestMeasuredFrequencyHz => double.PositiveInfinity;
}
