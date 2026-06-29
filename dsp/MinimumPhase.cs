using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Reconstructs the minimum-phase response that corresponds to a measured
/// magnitude spectrum, using the real-cepstrum method.
/// </summary>
/// <remarks>
/// A measured transfer function factors into a minimum-phase part — uniquely
/// determined by the magnitude through the Hilbert (Bode) relation, and therefore
/// correctable with a minimum-phase equalizer — and an excess (all-pass) part
/// caused by pure delay, reflections and other non-minimum-phase behaviour that an
/// equalizer cannot fix. This class produces the minimum-phase component so callers
/// can derive the excess phase as <c>measured − minimum</c>.
///
/// The computation is the standard cepstral construction (cf. MATLAB <c>rceps</c>):
/// take the log of the magnitude, transform to the (real) cepstrum, fold the
/// anti-causal part onto the causal part so the result is causal/minimum-phase, then
/// transform back; the imaginary part of the resulting log-spectrum is the
/// minimum phase.
///
/// All methods are pure: they allocate their own buffers and do not mutate inputs.
/// </remarks>
public static class MinimumPhase
{
    /// <summary>
    /// Default magnitude floor. Magnitudes below this are clamped before taking the
    /// logarithm so that nulls and the noise floor do not produce log singularities.
    /// Matches <see cref="DataHelper"/>'s minimum amplitude.
    /// </summary>
    public const double DefaultMagnitudeFloor = 1e-8;

    /// <summary>
    /// Computes the minimum-phase response (radians) from a full-length linear
    /// magnitude spectrum (bins covering 0 … sample rate).
    /// </summary>
    /// <param name="magnitude">Linear magnitude per FFT bin, length N.</param>
    /// <param name="magnitudeFloor">Lower clamp applied before the logarithm.</param>
    /// <returns>Minimum phase in radians, length N.</returns>
    public static double[] FromMagnitude(
        IReadOnlyList<double> magnitude,
        double magnitudeFloor = DefaultMagnitudeFloor)
    {
        ArgumentNullException.ThrowIfNull(magnitude);
        if (magnitude.Count == 0)
        {
            throw new ArgumentException(
                "Magnitude must not be empty.",
                nameof(magnitude));
        }
        if (magnitudeFloor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(magnitudeFloor));
        }

        int length = magnitude.Count;
        double[] logMagnitude = new double[length];
        for (int i = 0; i < length; i++)
        {
            logMagnitude[i] = Math.Log(Math.Max(magnitude[i], magnitudeFloor));
        }

        return FromLogMagnitude(logMagnitude);
    }

    /// <summary>
    /// Computes the minimum-phase response (radians) from a complex spectrum,
    /// using only its magnitude.
    /// </summary>
    public static double[] FromSpectrum(
        IReadOnlyList<Complex> spectrum,
        double magnitudeFloor = DefaultMagnitudeFloor)
    {
        ArgumentNullException.ThrowIfNull(spectrum);

        double[] magnitude = new double[spectrum.Count];
        for (int i = 0; i < spectrum.Count; i++)
        {
            magnitude[i] = spectrum[i].Magnitude;
        }

        return FromMagnitude(magnitude, magnitudeFloor);
    }

    /// <summary>
    /// Reconstructs the full complex minimum-phase spectrum
    /// <c>|H| · e^{jφ_min}</c> from a linear magnitude spectrum.
    /// </summary>
    public static Complex[] Reconstruct(
        IReadOnlyList<double> magnitude,
        double magnitudeFloor = DefaultMagnitudeFloor)
    {
        ArgumentNullException.ThrowIfNull(magnitude);

        double[] phase = FromMagnitude(magnitude, magnitudeFloor);
        Complex[] spectrum = new Complex[magnitude.Count];
        for (int i = 0; i < magnitude.Count; i++)
        {
            spectrum[i] = Complex.FromPolarCoordinates(magnitude[i], phase[i]);
        }

        return spectrum;
    }

    /// <summary>
    /// Core cepstral step: given the natural-log magnitude per bin, returns the
    /// minimum phase per bin (radians).
    /// </summary>
    public static double[] FromLogMagnitude(IReadOnlyList<double> logMagnitude)
    {
        ArgumentNullException.ThrowIfNull(logMagnitude);
        int length = logMagnitude.Count;
        if (length == 0)
        {
            throw new ArgumentException(
                "Log magnitude must not be empty.",
                nameof(logMagnitude));
        }

        // Real cepstrum: inverse transform of the log-magnitude spectrum.
        Complex[] buffer = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            buffer[i] = new Complex(logMagnitude[i], 0.0);
        }

        Fourier.Inverse(buffer, FourierOptions.Matlab);

        // Fold the anti-causal half onto the causal half so the cepstrum describes a
        // causal (minimum-phase) sequence. DC and Nyquist (for even N) stay; the
        // positive-quefrency half doubles; the negative half is zeroed.
        ApplyMinimumPhaseLifter(buffer, length);

        // Forward transform back: the imaginary part is the minimum phase.
        Fourier.Forward(buffer, FourierOptions.Matlab);

        double[] phase = new double[length];
        for (int i = 0; i < length; i++)
        {
            phase[i] = buffer[i].Imaginary;
        }

        return phase;
    }

    private static void ApplyMinimumPhaseLifter(Complex[] cepstrum, int length)
    {
        if (length == 1)
        {
            return;
        }

        bool even = (length & 1) == 0;
        int positiveEnd = even ? length / 2 : (length - 1) / 2;

        // cepstrum[0] (DC) keeps its weight of 1.
        for (int i = 1; i <= positiveEnd; i++)
        {
            // For even N the Nyquist bin (length/2) is shared and keeps weight 1.
            cepstrum[i] *= (even && i == length / 2) ? 1.0 : 2.0;
        }

        for (int i = positiveEnd + 1; i < length; i++)
        {
            cepstrum[i] = Complex.Zero;
        }
    }
}
