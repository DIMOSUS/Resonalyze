using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public static class SpectrumAnalysis
{

    /// <summary>Hann single-sided power spectrum normalized by coherent gain: tone-correct, not a PSD (noise reads ~1.76 dB high).</summary>
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
        double[] window = Windowing.SharedAnalysisWindow(windowType, length);
        var spectrum = new Complex[length];
        double windowSum = 0.0;
        for (int i = 0; i < length; i++)
        {
            windowSum += window[i];
            spectrum[i] = new Complex(samples[i] * window[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        // 2|X_k|/(N·CG): a full-scale sine reads 1.0 at any FFT length or window; DC takes half the scale.
        double coherentGain = windowSum / length;
        double scale = coherentGain > 0.0
            ? 2.0 / (length * coherentGain)
            : 2.0 / length;
        var power = new double[length / 2];
        for (int i = 0; i < power.Length; i++)
        {
            double magnitude = spectrum[i].Magnitude * (i == 0 ? scale * 0.5 : scale);
            power[i] = magnitude * magnitude;
        }

        return power;
    }

    /// <summary>Tone-calibrated reference-free RTA magnitude from accumulated |FFT|² (no coherence, no phase); equals sqrt of
    /// <see cref="ComputePowerSpectrum"/> bin for bin. <paramref name="frameLength"/> recovers the window's coherent gain.</summary>
    public static double[] ComputeInputMagnitudeSpectrum(
        IReadOnlyList<double> autoPowerSpectrum,
        WindowType windowType,
        int frameLength)
    {
        ArgumentNullException.ThrowIfNull(autoPowerSpectrum);
        if (frameLength < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(frameLength));
        }

        double[] window = Windowing.SharedAnalysisWindow(windowType, frameLength);
        double windowSum = 0.0;
        for (int i = 0; i < frameLength; i++)
        {
            windowSum += window[i];
        }

        double coherentGain = windowSum / frameLength;

        // Same 2/(N·CG) scale as ComputePowerSpectrum: length-independent, DC takes half.
        double scale = coherentGain > 0.0
            ? 2.0 / (frameLength * coherentGain)
            : 2.0 / frameLength;

        var magnitude = new double[autoPowerSpectrum.Count];
        for (int i = 0; i < magnitude.Length; i++)
        {
            double power = autoPowerSpectrum[i];
            double binScale = i == 0 ? scale * 0.5 : scale;
            magnitude[i] = power > 0.0 ? Math.Sqrt(power) * binScale : 0.0;
        }

        return magnitude;
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

        double[] window = Windowing.SharedAnalysisWindow(windowType, reference.Count);
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

    /// <summary>Target auto-power of <see cref="ComputeTransferSpectrumFrame"/> with a single FFT, for mic-only captures.</summary>
    public static double[] ComputeAutoPowerSpectrumFrame(
        IReadOnlyList<float> samples,
        WindowType windowType = WindowType.Hann)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("Samples must not be empty.", nameof(samples));
        }

        double[] window = Windowing.SharedAnalysisWindow(windowType, samples.Count);
        var spectrum = new Complex[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            spectrum[i] = new Complex(samples[i] * window[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        int binCount = samples.Count / 2;
        var power = new double[binCount];
        for (int i = 0; i < binCount; i++)
        {
            power[i] = spectrum[i].Magnitude * spectrum[i].Magnitude;
        }

        return power;
    }

    /// <summary>|&lt;Sxy&gt;|² / (&lt;Sxx&gt;·&lt;Syy&gt;) over several frames (one frame is always unity), clamped to [0, 1].</summary>
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

    /// <summary>In place: (K·γ̂² − 1)/(K − 1), since E[γ̂²] = 1/K for noise (0.5 at K = 2, right at the PHAT/unwrap thresholds). K = 1 gives 0.</summary>
    public static double[] DebiasCoherence(double[] coherence, int averageCount)
    {
        ArgumentNullException.ThrowIfNull(coherence);
        for (int i = 0; i < coherence.Length; i++)
        {
            coherence[i] = averageCount <= 1
                ? 0.0
                : Math.Clamp(
                    (averageCount * coherence[i] - 1.0) / (averageCount - 1.0),
                    0.0,
                    1.0);
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
