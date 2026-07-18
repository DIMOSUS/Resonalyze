namespace Resonalyze.Dsp;

/// <summary>
/// The shared encoding of a display-smoothing selection. Plain fractional-octave
/// smoothing is stored as its positive inverse octave count (6 = 1/6 octave,
/// 0 = off) — the historical convention every option DTO and file format uses.
/// The psychoacoustic mode is the single negative code below: it smooths
/// MAGNITUDE curves with a frequency-dependent bandwidth and a cubic mean,
/// giving peaks more perceptual weight without hard-clipping valleys.
/// Non-magnitude curves
/// (phase, group delay, coherence, harmonic widths) must never apply the
/// magnitude weighting: for them the code decodes to the plain base width via
/// <see cref="SmoothingOctaves"/>, keeping those curves unbiased.
/// </summary>
public static class SpectrumSmoothing
{
    /// <summary>
    /// The stored code of the psychoacoustic mode. Negative so it can travel
    /// through every existing integer smoothing field without colliding with a
    /// plain width; the magnitude equals <see cref="PsychoacousticBaseInverseOctaves"/>,
    /// so even code that naively takes <c>Math.Abs</c> lands on the base width.
    /// </summary>
    public const int PsychoacousticCode = -PsychoacousticBaseInverseOctaves;

    /// <summary>
    /// The high-frequency fractional-octave width (inverse octaves) of the
    /// psychoacoustic mode. The effective width grows smoothly to 1/3 octave
    /// below 100 Hz.
    /// </summary>
    public const int PsychoacousticBaseInverseOctaves = 6;

    public static double PsychoacousticOctaves(double frequency)
    {
        if (frequency <= 100.0)
        {
            return 1.0 / 3.0;
        }
        if (frequency >= 1_000.0)
        {
            return 1.0 / 6.0;
        }

        double position = Math.Log10(frequency / 100.0);
        return 1.0 / 3.0 - position / 6.0;
    }

    public static bool IsPsychoacoustic(double smoothingInverseOctaves) =>
        smoothingInverseOctaves == PsychoacousticCode;

    /// <summary>
    /// The plain smoothing width (in octaves) a stored code decodes to: the
    /// inverse of a positive width, the base width for the psychoacoustic code,
    /// zero (off) otherwise. This is the ONE translation every consumer that
    /// divides by the stored value must use — a naive <c>1.0 / code</c> turns
    /// the psychoacoustic code into a negative width, and a naive
    /// <c>code &gt; 0</c> check silently reads it as "off".
    /// </summary>
    public static double SmoothingOctaves(double smoothingInverseOctaves) =>
        smoothingInverseOctaves > 0
            ? 1.0 / smoothingInverseOctaves
            : IsPsychoacoustic(smoothingInverseOctaves)
                ? 1.0 / PsychoacousticBaseInverseOctaves
                : 0.0;

    /// <summary>
    /// The equivalent plain inverse-octave width of a stored code — what a
    /// consumer (or an older reader) should fall back to when it cannot honor
    /// the asymmetric mode: the base width for the psychoacoustic code, the
    /// value itself for a plain width, zero for off.
    /// </summary>
    public static int EquivalentInverseOctaves(int smoothingInverseOctaves) =>
        IsPsychoacoustic(smoothingInverseOctaves)
            ? PsychoacousticBaseInverseOctaves
            : Math.Max(0, smoothingInverseOctaves);
}
