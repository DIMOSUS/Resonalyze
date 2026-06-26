using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Shared FFT helpers for spectrum-based measurements.
/// </summary>
public static class SpectrumAnalysis
{

    /// <summary>
    /// Computes the single-sided power spectrum of a Hann-windowed block.
    /// The result is normalized by the window's coherent gain so the level of a
    /// tone is independent of the analysis window (a rectangular window and a
    /// Hann window report the same level). This is a tone-correct
    /// (amplitude-calibrated) spectrum, not a power spectral density: broadband
    /// noise reads about 1.76 dB high for Hann relative to an ENBW-normalized
    /// estimate, because coherent-gain correction cannot calibrate tones and
    /// noise power simultaneously. Averaging in the power domain still removes
    /// the downward bias of magnitude averaging for noise-like signals.
    /// </summary>
    public static double[] ComputePowerSpectrum(
        IReadOnlyList<float> samples,
        WindowType windowType = WindowType.Hann)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("Samples must not be empty.", nameof(samples));
        }

        int length = samples.Count;
        double[] window = Windowing.CreateAnalysisWindow(windowType, length);
        var spectrum = new Complex[length];
        double windowSum = 0.0;
        for (int i = 0; i < length; i++)
        {
            windowSum += window[i];
            spectrum[i] = new Complex(samples[i] * window[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        double coherentGain = windowSum / length;
        double scale = coherentGain > 0.0 ? 1.0 / coherentGain : 1.0;
        var power = new double[length / 2];
        for (int i = 0; i < power.Length; i++)
        {
            double magnitude = spectrum[i].Magnitude * scale;
            power[i] = magnitude * magnitude;
        }

        return power;
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
        IReadOnlyList<float> target,
        WindowType windowType = WindowType.Hann)
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

        double[] window = Windowing.CreateAnalysisWindow(windowType, reference.Count);
        var referenceSpectrum = new Complex[reference.Count];
        var targetSpectrum = new Complex[target.Count];
        for (int i = 0; i < reference.Count; i++)
        {
            referenceSpectrum[i] = new Complex(reference[i] * window[i], 0.0);
            targetSpectrum[i] = new Complex(target[i] * window[i], 0.0);
        }

        Fourier.Forward(referenceSpectrum, FourierOptions.Matlab);
        Fourier.Forward(targetSpectrum, FourierOptions.Matlab);

        int binCount = reference.Count / 2;
        var crossSpectrum = new Complex[binCount];
        var referencePowerSpectrum = new double[binCount];
        var targetPowerSpectrum = new double[binCount];
        for (int i = 0; i < binCount; i++)
        {
            crossSpectrum[i] = targetSpectrum[i] * Complex.Conjugate(referenceSpectrum[i]);
            referencePowerSpectrum[i] =
                referenceSpectrum[i].Magnitude * referenceSpectrum[i].Magnitude;
            targetPowerSpectrum[i] =
                targetSpectrum[i].Magnitude * targetSpectrum[i].Magnitude;
        }

        return new TransferSpectrumFrame(
            crossSpectrum,
            referencePowerSpectrum,
            targetPowerSpectrum);
    }

    /// <summary>
    /// Computes the magnitude-squared coherence γ² from averaged cross- and
    /// auto-spectra: |&lt;Sxy&gt;|² / (&lt;Sxx&gt;·&lt;Syy&gt;). The inputs must
    /// be accumulated over several frames; for a single frame coherence is
    /// always unity. Values are clamped to [0, 1].
    /// </summary>
    public static double[] ComputeCoherence(
        IReadOnlyList<Complex> crossSpectrum,
        IReadOnlyList<double> referencePowerSpectrum,
        IReadOnlyList<double> targetPowerSpectrum,
        double epsilon = 1e-12)
    {
        ArgumentNullException.ThrowIfNull(crossSpectrum);
        ArgumentNullException.ThrowIfNull(referencePowerSpectrum);
        ArgumentNullException.ThrowIfNull(targetPowerSpectrum);
        if (crossSpectrum.Count != referencePowerSpectrum.Count ||
            crossSpectrum.Count != targetPowerSpectrum.Count)
        {
            throw new ArgumentException("Input arrays must have same length.");
        }
        if (!double.IsFinite(epsilon) || epsilon < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epsilon));
        }

        var coherence = new double[crossSpectrum.Count];
        for (int i = 0; i < coherence.Length; i++)
        {
            double denominator = referencePowerSpectrum[i] * targetPowerSpectrum[i];
            double magnitude = crossSpectrum[i].Magnitude;
            double value = denominator > epsilon
                ? magnitude * magnitude / denominator
                : 0.0;
            coherence[i] = Math.Clamp(value, 0.0, 1.0);
        }

        return coherence;
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
}

public sealed record TransferSpectrumFrame(
    Complex[] CrossSpectrum,
    double[] ReferencePowerSpectrum,
    double[] TargetPowerSpectrum);
