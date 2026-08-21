using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Builds a raised-cosine bandpass mask in the frequency domain.
/// </summary>
public static class BandpassWindow
{
    /// <summary>
    /// Applies a frequency-domain mask to a real signal and returns the real part of
    /// the result. The mask is real and even, so the filter is ZERO PHASE: it moves no
    /// arrival in time, which is what makes a band-limited read of "when did this
    /// arrive" honest — at the price of a symmetric pre-ringing skirt (see
    /// <see cref="PeakSearchOptions.AnalysisKernelEnvelope"/>, which measures that skirt
    /// so it is not mistaken for an earlier arrival).
    ///
    /// <paramref name="window"/> must be as long as <paramref name="signal"/>. The
    /// transform is circular, so a caller filtering a CUT of a longer record should
    /// zero-pad it first — otherwise a narrow low-frequency band, whose kernel is long,
    /// wraps the tail back onto the head.
    /// </summary>
    public static double[] Apply(IReadOnlyList<double> signal, double[] window)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(window);
        if (window.Length != signal.Count)
        {
            throw new ArgumentException(
                "Window and signal must be the same length.",
                nameof(window));
        }

        var spectrum = new Complex[signal.Count];
        for (int i = 0; i < signal.Count; i++)
        {
            spectrum[i] = new Complex(signal[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] *= window[i];
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);

        var filtered = new double[spectrum.Length];
        for (int i = 0; i < spectrum.Length; i++)
        {
            filtered[i] = spectrum[i].Real;
        }

        return filtered;
    }

    public static (double F1, double F2, double F3, double F4) BandAround(
        double centerHz,
        double passOctaves,
        double fadeOctaves)
    {
        if (!double.IsFinite(centerHz) || centerHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(centerHz));
        }
        if (!double.IsFinite(passOctaves) || passOctaves < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(passOctaves));
        }
        if (!double.IsFinite(fadeOctaves) || fadeOctaves < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fadeOctaves));
        }

        double f2 = centerHz / Math.Pow(2.0, passOctaves * 0.5);
        double f3 = centerHz * Math.Pow(2.0, passOctaves * 0.5);

        double f1 = fadeOctaves > 0
            ? f2 / Math.Pow(2.0, fadeOctaves)
            : f2;
        double f4 = fadeOctaves > 0
            ? f3 * Math.Pow(2.0, fadeOctaves)
            : f3;

        return (f1, f2, f3, f4);
    }

    public static double Weight(
        double frequency,
        double f1,
        double f2,
        double f3,
        double f4)
    {
        ValidateBandEdges(f1, f2, f3, f4);

        double absoluteFrequency = Math.Abs(frequency);

        if (absoluteFrequency <= f1)
        {
            return 0.0;
        }
        if (absoluteFrequency < f2)
        {
            if (f2 <= f1)
            {
                return 1.0;
            }

            return 0.5 - 0.5 * Math.Cos(
                Math.PI * (absoluteFrequency - f1) / (f2 - f1));
        }
        if (absoluteFrequency <= f3)
        {
            return 1.0;
        }
        if (absoluteFrequency < f4)
        {
            if (f4 <= f3)
            {
                return 1.0;
            }

            return 0.5 + 0.5 * Math.Cos(
                Math.PI * (absoluteFrequency - f3) / (f4 - f3));
        }

        return 0.0;
    }

    public static double[] Create(
        int fftSize,
        double sampleRate,
        double f1,
        double f2,
        double f3,
        double f4)
    {
        if (fftSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fftSize));
        }
        if (!double.IsFinite(sampleRate) || sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        ValidateBandEdges(f1, f2, f3, f4);

        var window = new double[fftSize];
        for (int bin = 0; bin < fftSize; bin++)
        {
            double frequency = bin <= fftSize / 2
                ? bin * sampleRate / fftSize
                : -(fftSize - bin) * sampleRate / fftSize;
            window[bin] = Weight(frequency, f1, f2, f3, f4);
        }

        return window;
    }

    public static double[] Create(
        int fftSize,
        double sampleRate,
        double centerHz,
        double passOctaves,
        double fadeOctaves)
    {
        (double f1, double f2, double f3, double f4) = BandAround(
            centerHz,
            passOctaves,
            fadeOctaves);
        return Create(fftSize, sampleRate, f1, f2, f3, f4);
    }

    private static void ValidateBandEdges(
        double f1,
        double f2,
        double f3,
        double f4)
    {
        if (!double.IsFinite(f1) ||
            !double.IsFinite(f2) ||
            !double.IsFinite(f3) ||
            !double.IsFinite(f4))
        {
            throw new ArgumentOutOfRangeException(nameof(f1));
        }
        if (f1 < 0 || f2 <= 0 || f3 <= 0 || f4 <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(f1));
        }
        if (f1 > f2 || f2 > f3 || f3 > f4)
        {
            throw new ArgumentException(
                "Band edges must satisfy f1 <= f2 <= f3 <= f4.");
        }
    }
}
