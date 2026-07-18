namespace Resonalyze.Dsp;

/// <summary>
/// The shared encoding of a display-smoothing selection. Plain fractional-octave
/// smoothing is stored as its positive inverse octave count (6 = 1/6 octave,
/// 0 = off) — the historical convention every option DTO and file format uses.
/// The psychoacoustic mode is the single negative code below: it smooths
/// MAGNITUDE curves with the base fractional-octave width, then floors each
/// point at the window's median, so narrow interference dips (which the ear
/// largely ignores, and which no alignment or EQ move can genuinely fill) drop
/// out of the drawn curve while narrow peaks — audible resonances — and any
/// structure wider than the window survive untouched. Non-magnitude curves
/// (phase, group delay, coherence, harmonic widths) must never apply the
/// asymmetric floor: for them the code decodes to the plain base width via
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
    /// The base fractional-octave width (inverse octaves) of the psychoacoustic
    /// mode: 1/6 octave — wide enough that a comb-interference notch spans less
    /// than half the window (so the median ignores it), narrow enough to keep
    /// the broad tonal balance and real modal valleys visible.
    /// </summary>
    public const int PsychoacousticBaseInverseOctaves = 6;

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
