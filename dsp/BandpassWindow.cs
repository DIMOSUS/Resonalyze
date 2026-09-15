using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public static class BandpassWindow
{
    /// <summary>Zero-phase mask (moves no arrival; pre-ringing handled via <see cref="PeakSearchOptions.AnalysisKernelEnvelope"/>).
    /// Circular: zero-pad a cut of a longer record, or a long low-band kernel wraps the tail onto the head.</summary>
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
