using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>Minimum phase from a magnitude spectrum by the real-cepstrum method (cf. MATLAB <c>rceps</c>); excess = measured − minimum. See docs/tech/phase-and-group-delay.md#minimum-phase-reconstruction.</summary>
public static class MinimumPhase
{
    /// <summary>Floor relative to the spectrum peak (−160 dB): minimum phase is gain-invariant, an absolute floor made it level-dependent.</summary>
    public const double DefaultMagnitudeFloor = 1e-8;

    /// <summary>Minimum phase (radians, length N) from a full 0 … fs linear magnitude spectrum.</summary>
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
        double maxMagnitude = 0.0;
        for (int i = 0; i < length; i++)
        {
            double value = magnitude[i];
            if (double.IsFinite(value) && value > maxMagnitude)
            {
                maxMagnitude = value;
            }
        }

        double floor = Math.Max(maxMagnitude * magnitudeFloor, double.Epsilon);
        double[] logMagnitude = new double[length];
        for (int i = 0; i < length; i++)
        {
            double value = magnitude[i];
            logMagnitude[i] = Math.Log(
                double.IsFinite(value) && value > floor ? value : floor);
        }

        return FromLogMagnitude(logMagnitude);
    }

    /// <summary>
    /// Computes the minimum-phase response (radians) from a complex spectrum,
    /// using only its magnitude.
    /// </summary>
    /// <remarks>Reserve API: no caller in the solution today (see AGENTS.md).</remarks>
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

        Complex[] buffer = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            buffer[i] = new Complex(logMagnitude[i], 0.0);
        }

        Fourier.Inverse(buffer, FourierOptions.Matlab);

        // Fold the anti-causal half onto the causal half: DC and even-N Nyquist keep weight 1, positive quefrencies double.
        ApplyMinimumPhaseLifter(buffer, length);

        // The imaginary part of the result is the minimum phase.
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

        for (int i = 1; i <= positiveEnd; i++)
        {
            cepstrum[i] *= (even && i == length / 2) ? 1.0 : 2.0;
        }

        for (int i = positiveEnd + 1; i < length; i++)
        {
            cepstrum[i] = Complex.Zero;
        }
    }
}
