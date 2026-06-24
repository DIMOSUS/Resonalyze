using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Shared FFT helpers for spectrum-based measurements.
/// </summary>
public static class SpectrumAnalysis
{
    public static double[] ComputeMagnitudeSpectrum(IReadOnlyList<float> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("Samples must not be empty.", nameof(samples));
        }

        var spectrum = new Complex[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            spectrum[i] = new Complex(samples[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        var magnitudes = new double[samples.Count / 2];
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = spectrum[i].Magnitude;
        }

        return magnitudes;
    }

    public static double[] ComputeTransferMagnitudeSpectrum(
        IReadOnlyList<float> reference,
        IReadOnlyList<float> target,
        double epsilon = 1e-12)
    {
        TransferSpectrumFrame frame = ComputeTransferSpectrumFrame(reference, target);
        return ComputeH1MagnitudeSpectrum(
            frame.CrossSpectrum,
            frame.ReferencePowerSpectrum,
            epsilon);
    }

    public static TransferSpectrumFrame ComputeTransferSpectrumFrame(
        IReadOnlyList<float> reference,
        IReadOnlyList<float> target)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(target);
        if (reference.Count != target.Count)
        {
            throw new ArgumentException("Input arrays must have same length.");
        }
        if (reference.Count == 0)
        {
            throw new ArgumentException("Samples must not be empty.", nameof(reference));
        }

        var referenceSpectrum = new Complex[reference.Count];
        var targetSpectrum = new Complex[target.Count];
        for (int i = 0; i < reference.Count; i++)
        {
            double window = HannWindow(i, reference.Count);
            referenceSpectrum[i] = new Complex(reference[i] * window, 0.0);
            targetSpectrum[i] = new Complex(target[i] * window, 0.0);
        }

        Fourier.Forward(referenceSpectrum, FourierOptions.Matlab);
        Fourier.Forward(targetSpectrum, FourierOptions.Matlab);

        int binCount = reference.Count / 2;
        var crossSpectrum = new Complex[binCount];
        var referencePowerSpectrum = new double[binCount];
        for (int i = 0; i < binCount; i++)
        {
            crossSpectrum[i] = targetSpectrum[i] * Complex.Conjugate(referenceSpectrum[i]);
            referencePowerSpectrum[i] =
                referenceSpectrum[i].Magnitude * referenceSpectrum[i].Magnitude;
        }

        return new TransferSpectrumFrame(crossSpectrum, referencePowerSpectrum);
    }

    public static double[] ComputeH1MagnitudeSpectrum(
        IReadOnlyList<Complex> crossSpectrum,
        IReadOnlyList<double> referencePowerSpectrum,
        double epsilon = 1e-12)
    {
        ArgumentNullException.ThrowIfNull(crossSpectrum);
        ArgumentNullException.ThrowIfNull(referencePowerSpectrum);
        if (crossSpectrum.Count != referencePowerSpectrum.Count)
        {
            throw new ArgumentException("Input arrays must have same length.");
        }
        if (!double.IsFinite(epsilon) || epsilon < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epsilon));
        }

        var magnitudes = new double[crossSpectrum.Count];
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = (crossSpectrum[i] /
                (referencePowerSpectrum[i] + epsilon)).Magnitude;
        }

        return magnitudes;
    }

    private static double HannWindow(int index, int length)
    {
        if (length == 1)
        {
            return 1.0;
        }

        return 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * index / (length - 1));
    }
}

public sealed record TransferSpectrumFrame(
    Complex[] CrossSpectrum,
    double[] ReferencePowerSpectrum);
