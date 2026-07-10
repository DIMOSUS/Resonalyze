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

        // Tone calibration (dBFS): a full-scale bin-centred sine reads
        // amplitude 1.0 for any FFT length and any window — |X_k| = N·CG/2,
        // so the amplitude is 2|X_k|/(N·CG). Without the 2/N the level jumped
        // 6 dB per FFT-size doubling. DC has no conjugate mirror, so it takes
        // half the scale (a DC level of 1.0 also reads 1.0).
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

    /// <summary>
    /// Converts an accumulated single-input auto-power spectrum — the windowed,
    /// not-yet-gain-corrected |FFT|² produced as the target power by
    /// <see cref="ComputeTransferSpectrumFrame"/> — into a tone-calibrated
    /// magnitude spectrum. The result is normalized by the analysis window's
    /// coherent gain, so a tone reads the same level regardless of window, the
    /// same convention as <see cref="ComputePowerSpectrum"/>. This is the
    /// reference-free RTA magnitude: it reflects the input level alone with no
    /// division by any reference channel, so unlike the H1 transfer function it
    /// carries neither coherence nor phase. <paramref name="frameLength"/> is the
    /// pre-FFT block length the auto-power was measured with (twice the bin count
    /// for a real signal); it is needed to recover the window's coherent gain.
    /// By the identity sqrt(|FFT|²)·scale, the result equals the square root of
    /// <see cref="ComputePowerSpectrum"/> of the same block bin-for-bin.
    /// </summary>
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

        double[] window = Windowing.CreateAnalysisWindow(windowType, frameLength);
        double windowSum = 0.0;
        for (int i = 0; i < frameLength; i++)
        {
            windowSum += window[i];
        }

        double coherentGain = windowSum / frameLength;

        // Tone calibration (dBFS): a full-scale bin-centred sine has
        // |X_k| = N·CG/2, so the amplitude is 2|X_k|/(N·CG) — INDEPENDENT of
        // the FFT length. Without the 2/N the same input jumped 6 dB per
        // FFT-size doubling, on the same axis as the length-invariant H1
        // transfer gain. DC (no conjugate mirror) takes half the scale,
        // matching ComputePowerSpectrum bin for bin.
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

    /// <summary>
    /// Removes the small-sample positive bias of the raw γ² estimate, in place.
    /// The MSC estimator over K averages has E[γ̂²] = 1/K for fully incoherent
    /// signals — at K = 2 pure noise reads ~0.5, exactly at the thresholds the
    /// unwrap and PHAT weighting trust — so raw values are rescaled by the
    /// standard first-order correction (K·γ̂² − 1)/(K − 1), which maps the null
    /// expectation to 0 and keeps 1 at 1. With one average (no estimate at all)
    /// everything collapses to 0. Returns the same array for chaining.
    /// </summary>
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
