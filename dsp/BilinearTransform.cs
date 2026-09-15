namespace Resonalyze.Dsp;

internal static class BilinearTransform
{
    /// <summary>Prewarp tangent blows up at Nyquist; compare against this when matching the realized, not nominal, filter.</summary>
    public const double NyquistFraction = 0.499;

    /// <summary>Per section: a prototype scale factor can push a section past Nyquist even when the cutoff is fine.</summary>
    public static double ClampBelowNyquist(double frequencyHz, double sampleRateHz) =>
        Math.Min(frequencyHz, sampleRateHz * NyquistFraction);
}
