namespace Resonalyze.Dsp;

/// <summary>Smoothing code: positive = 1/N octave, 0 = off, <see cref="PsychoacousticCode"/> = psychoacoustic (magnitude only).
/// Non-magnitude curves must decode via <see cref="SmoothingOctaves"/> to the plain base width.</summary>
public static class SpectrumSmoothing
{
    /// <summary>Negative so it fits integer fields; |code| is the base width, so a naive Math.Abs still works.</summary>
    public const int PsychoacousticCode = -PsychoacousticBaseInverseOctaves;

    /// <summary>HF width; grows smoothly to 1/3 octave below 100 Hz.</summary>
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

    /// <summary>Use this, not <c>1.0 / code</c> or <c>code &gt; 0</c>, which mis-read the psychoacoustic code.</summary>
    public static double SmoothingOctaves(double smoothingInverseOctaves) =>
        smoothingInverseOctaves > 0
            ? 1.0 / smoothingInverseOctaves
            : IsPsychoacoustic(smoothingInverseOctaves)
                ? 1.0 / PsychoacousticBaseInverseOctaves
                : 0.0;

    /// <summary>Half height of sinc(x)·sinc(x/2), where the magnitude resampler's Lanczos-2 mean falls to one half.</summary>
    private const double LanczosHalfHeightX = 0.5522198099135098;

    /// <summary>Full width at half maximum, octaves, of the kernel a magnitude curve is smoothed with at this code: the
    /// Lanczos mean reaches the nominal width but halves far inside it; the psychoacoustic Gaussian is its base width.</summary>
    public static double MagnitudeFwhmOctaves(double smoothingInverseOctaves)
    {
        double width = SmoothingOctaves(smoothingInverseOctaves);
        if (width <= 0.0 || IsPsychoacoustic(smoothingInverseOctaves))
        {
            return width;
        }

        double reach = 0.5 * LanczosHalfHeightX * (Math.Pow(2.0, 0.5 * width) - 1.0);
        return Math.Log2((1.0 + reach) / (1.0 - reach));
    }

    public static int EquivalentInverseOctaves(int smoothingInverseOctaves) =>
        IsPsychoacoustic(smoothingInverseOctaves)
            ? PsychoacousticBaseInverseOctaves
            : Math.Max(0, smoothingInverseOctaves);
}
