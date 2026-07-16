namespace Resonalyze.Dsp;

/// <summary>
/// Helpers shared by every filter this library designs through the bilinear
/// transform (the crossover cascades and the all-pass stage alike).
/// </summary>
internal static class BilinearTransform
{
    /// <summary>
    /// A section corner at or above Nyquist cannot be realized by the bilinear
    /// transform (the prewarp tangent blows up), so it is clamped just below —
    /// the same way DSP hardware limits its frequency entry. Applied per section
    /// because a prototype's scale factor can push a section past Nyquist even
    /// when the nominal cutoff itself is fine.
    /// </summary>
    public static double ClampBelowNyquist(double frequencyHz, double sampleRateHz) =>
        Math.Min(frequencyHz, sampleRateHz * 0.499);
}
