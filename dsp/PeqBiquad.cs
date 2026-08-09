namespace Resonalyze.Dsp;

/// <summary>
/// The one place a <see cref="PeqBand"/> becomes coefficients, whatever its
/// <see cref="PeqBandType"/>. Everything that realizes a band — the response
/// preview, the Virtual DSP chain and the coefficient exports — goes through here,
/// so a new band shape is added in one place instead of three.
/// </summary>
public static class PeqBiquad
{
    /// <remarks>
    /// A band whose type is neither of the shelves is realized as a bell, which
    /// includes a value no enum member matches: a hand-edited project or settings
    /// file must degrade to the default shape, not throw out of the audio path.
    /// </remarks>
    public static BiquadCoefficients Compute(PeqBand band, double sampleRateHz) =>
        band.Type.IsShelving()
            ? ShelvingBiquad.Compute(band, sampleRateHz)
            : PeakingBiquad.Compute(band, sampleRateHz);
}
