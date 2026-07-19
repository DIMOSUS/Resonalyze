namespace Resonalyze.Dsp;

/// <summary>
/// Helpers shared by every filter this library designs through the bilinear
/// transform (the crossover cascades and the all-pass stage alike).
/// </summary>
internal static class BilinearTransform
{
    /// <summary>
    /// The highest fraction of the sample rate a bilinear-transform corner can
    /// be realized at: just below Nyquist, because the prewarp tangent blows up
    /// at Nyquist itself. A corner configured above this is silently clamped
    /// here, so callers that must stay consistent with the realized filter
    /// (rather than the nominal setting) compare against this fraction.
    /// </summary>
    public const double NyquistFraction = 0.499;

    /// <summary>
    /// A section corner at or above Nyquist cannot be realized by the bilinear
    /// transform (the prewarp tangent blows up), so it is clamped just below —
    /// the same way DSP hardware limits its frequency entry. Applied per section
    /// because a prototype's scale factor can push a section past Nyquist even
    /// when the nominal cutoff itself is fine.
    /// </summary>
    public static double ClampBelowNyquist(double frequencyHz, double sampleRateHz) =>
        Math.Min(frequencyHz, sampleRateHz * NyquistFraction);
}
