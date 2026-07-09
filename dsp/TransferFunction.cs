using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Estimates relative impulse responses between two captured channels.
/// </summary>
public static class TransferFunction
{
    public static TransferEstimateResult ComputeAveragedRelativeIr(
        IReadOnlyList<TransferFunctionFrame> frames,
        double epsilon = 1e-12)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one transfer frame is required.", nameof(frames));
        }
        if (!double.IsFinite(epsilon) || epsilon < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epsilon));
        }

        int sampleCount = frames.Min(frame => Math.Min(frame.Reference.Count, frame.Target.Count));
        if (sampleCount == 0)
        {
            throw new ArgumentException("Transfer frames must not be empty.", nameof(frames));
        }

        int fftLength = DspMath.NextPowerOfTwo(checked(sampleCount * 2));
        var crossSpectrum = new Complex[fftLength];
        var referencePowerSpectrum = new double[fftLength];
        var targetPowerSpectrum = new double[fftLength];

        foreach (TransferFunctionFrame frame in frames)
        {
            AccumulateFrameSpectra(
                frame.Reference,
                frame.Target,
                sampleCount,
                crossSpectrum,
                referencePowerSpectrum,
                targetPowerSpectrum);
        }

        // γ² from the shared cross/auto-spectra formula; epsilon 0 keeps the
        // previous denominator > 0 gate. Only the first half is retained — the
        // upper half mirrors it for the real inputs here.
        double[] coherence = SpectrumAnalysis
            .ComputeCoherence(
                crossSpectrum,
                referencePowerSpectrum,
                targetPowerSpectrum,
                epsilon: 0.0)[..(fftLength / 2 + 1)];

        Complex[] relative = InverseH1Response(
            crossSpectrum,
            referencePowerSpectrum,
            epsilon,
            filter: null);

        var impulseResponse = new double[fftLength];
        double peakMagnitude = 0;
        int peakIndex = 0;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            double value = relative[i].Real;
            impulseResponse[i] = value;
            double magnitude = Math.Abs(value);
            if (magnitude > peakMagnitude)
            {
                peakMagnitude = magnitude;
                peakIndex = i;
            }
        }

        return new TransferEstimateResult(
            impulseResponse,
            peakIndex,
            frames.Count >= 2 ? coherence : null);
    }

    // Forward-transforms one zero-padded frame pair and adds its cross- and
    // auto-spectra to the running sums.
    private static void AccumulateFrameSpectra(
        IReadOnlyList<double> reference,
        IReadOnlyList<double> target,
        int sampleCount,
        Complex[] crossSpectrum,
        double[] referencePowerSpectrum,
        double[]? targetPowerSpectrum)
    {
        int fftLength = crossSpectrum.Length;
        var referenceSpectrum = new Complex[fftLength];
        var targetSpectrum = new Complex[fftLength];
        for (int i = 0; i < sampleCount; i++)
        {
            referenceSpectrum[i] = new Complex(reference[i], 0.0);
            targetSpectrum[i] = new Complex(target[i], 0.0);
        }

        Fourier.Forward(referenceSpectrum, FourierOptions.Matlab);
        Fourier.Forward(targetSpectrum, FourierOptions.Matlab);

        for (int bin = 0; bin < fftLength; bin++)
        {
            crossSpectrum[bin] += targetSpectrum[bin] * Complex.Conjugate(referenceSpectrum[bin]);
            referencePowerSpectrum[bin] += MagnitudeSquared(referenceSpectrum[bin]);
            if (targetPowerSpectrum != null)
            {
                targetPowerSpectrum[bin] += MagnitudeSquared(targetSpectrum[bin]);
            }
        }
    }

    // The H1 estimate cross / (auto + eps), optionally shaped by a spectral
    // filter, transformed back to the time domain.
    private static Complex[] InverseH1Response(
        Complex[] crossSpectrum,
        double[] referencePowerSpectrum,
        double epsilon,
        IReadOnlyList<double>? filter)
    {
        int fftLength = crossSpectrum.Length;
        var relative = new Complex[fftLength];
        for (int bin = 0; bin < fftLength; bin++)
        {
            relative[bin] = crossSpectrum[bin] / (referencePowerSpectrum[bin] + epsilon);
            if (filter != null && filter.Count == fftLength)
            {
                relative[bin] *= filter[bin];
            }
        }

        Fourier.Inverse(relative, FourierOptions.Matlab);
        return relative;
    }

    private static double MagnitudeSquared(Complex value) =>
        value.Real * value.Real + value.Imaginary * value.Imaginary;

    /// <summary>
    /// Computes the phase-transform (GCC-PHAT) correlation of a loopback-referenced
    /// transfer impulse response. Its spectrum already carries the
    /// microphone/loopback cross-phase, so whitening it to unit magnitude over the
    /// band where the response has energy collapses the correlation to a sharp,
    /// low-side-lobe peak at the true broadband delay — independent of the driver's
    /// magnitude shape (and its polarity: an inverted channel simply flips the
    /// peak, which <see cref="PhaseTransformCorrelation.RefineAround"/> handles).
    /// The correlation is indexed to match the impulse response, so envelope-peak
    /// lags refine directly.
    /// </summary>
    /// <param name="coherence">
    /// Optional per-bin γ² (the half spectrum from
    /// <see cref="TransferEstimateResult.Coherence"/>, length
    /// <c>fftLength / 2 + 1</c>). When supplied and length-matched, each in-band bin
    /// is scaled by a floored-linear coherence weight, so bins whose phase does not
    /// repeat across averages (noise, non-linear distortion, non-averaging
    /// reflections) carry less say in the whitened correlation. It must come from
    /// the same transfer FFT that produced <paramref name="impulseResponse"/>; a
    /// null or wrong-length array is ignored and leaves the result bit-identical.
    /// </param>
    public static PhaseTransformCorrelation ComputePhaseTransformFromResponse(
        IReadOnlyList<double> impulseResponse,
        double referenceGate = 0.02,
        IReadOnlyList<double>? coherence = null)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Count == 0)
        {
            throw new ArgumentException("Impulse response must not be empty.");
        }

        // Padding up to a power of two keeps MathNet on the fast radix-2 path (an
        // odd length would silently fall back to the much slower Bluestein
        // algorithm). It is a no-op for the pipeline's own IRs, which are already
        // power-of-two, and zero-padding does not move the correlation peak: the
        // lag axis stays index-aligned with the impulse response.
        int fftLength = DspMath.NextPowerOfTwo(impulseResponse.Count);
        Complex[] spectrum = RealForwardSpectrum(impulseResponse, fftLength);
        var gateReference = new double[fftLength];
        for (int bin = 0; bin < fftLength; bin++)
        {
            gateReference[bin] = spectrum[bin].Magnitude;
        }

        return BuildPhaseTransform(spectrum, gateReference, filter: null, referenceGate, coherence);
    }

    // The lowest fraction of its whitened phasor a fully incoherent (γ²=0) in-band
    // bin keeps. A floored-linear map — not a bin-selector — because sub-sample
    // refinement precision follows the Cramér-Rao bound (∝ 1/(SNR·B_rms²)): it comes
    // from broadband phase agreement, so keeping every in-band bin at ≥ this share of
    // its weight preserves occupied bandwidth (and avoids punching a spectral hole
    // that would ring back as the very side lobes the soft gate exists to suppress),
    // while still demoting untrustworthy bins 4:1 against coherent ones.
    private const double CoherenceWeightFloor = 0.25;

    // Shared core: whiten the cross-spectrum to unit magnitude, weight it by a soft
    // band mask taken from where the gate reference has energy, and inverse-
    // transform to the correlation.
    private static PhaseTransformCorrelation BuildPhaseTransform(
        Complex[] crossSpectrum,
        double[] gateReference,
        IReadOnlyList<double>? filter,
        double referenceGate,
        IReadOnlyList<double>? coherence = null)
    {
        int fftLength = crossSpectrum.Length;
        double maxReference = 0;
        for (int bin = 0; bin < fftLength; bin++)
        {
            maxReference = Math.Max(maxReference, gateReference[bin]);
        }

        // γ² is the DC..Nyquist half spectrum (length fftLength/2 + 1). Only apply it
        // when the length matches exactly: a different length means a different
        // frequency grid, and folding by this FFT's length would misattribute SNR to
        // the wrong bins — a full-weight no-op is strictly safer than mis-indexing.
        int half = fftLength / 2;
        bool useCoherence = coherence != null && coherence.Count == half + 1;

        // A soft band mask instead of a hard energy gate. Bins fade in over a
        // raised cosine between gateLow and gateHigh of the reference peak, so the
        // band tapers smoothly at the excitation edges rather than as a brick wall
        // — a brick wall rings into the correlation as side lobes that can bias the
        // sub-sample refinement. The whole passband still sits at weight one; only
        // the true roll-off edges taper.
        double gateHigh = maxReference * referenceGate;
        double gateLow = gateHigh * 0.2;
        var whitened = new Complex[fftLength];
        double weightSum = 0;
        for (int bin = 0; bin < fftLength; bin++)
        {
            double bandWeight = SoftGate(gateReference[bin], gateLow, gateHigh);
            if (bandWeight <= 0)
            {
                continue;
            }

            if (useCoherence)
            {
                // Fold the full-spectrum bin onto its half-spectrum γ² partner. Bin i
                // and its Hermitian mirror fftLength-i fold to the same index, so both
                // get an identical real weight and the whitened spectrum stays
                // conjugate-symmetric (the inverse transform stays real).
                int folded = bin <= half ? bin : fftLength - bin;
                double g2 = coherence![folded];
                if (!(g2 > 0))
                {
                    g2 = 0; // also maps NaN to the floor rather than corrupting the weight
                }
                else if (g2 > 1)
                {
                    g2 = 1;
                }

                // Complement form (not the affine floor + (1-floor)*g2): at g2==1 it is
                // 1 - (1-floor)*0 = 1.0 bit-exactly for any floor, so flat/unit coherence
                // is a guaranteed no-op regardless of the constant.
                bandWeight *= 1.0 - (1.0 - CoherenceWeightFloor) * (1.0 - g2);
            }

            double magnitude = crossSpectrum[bin].Magnitude;
            if (magnitude <= 1e-20)
            {
                continue;
            }

            Complex unit = bandWeight * crossSpectrum[bin] / magnitude;
            if (filter != null && filter.Count == fftLength)
            {
                unit *= filter[bin];
            }

            whitened[bin] = unit;
            weightSum += unit.Magnitude;
        }

        var correlation = new double[fftLength];
        if (weightSum > 0)
        {
            Fourier.Inverse(whitened, FourierOptions.Matlab);
            for (int i = 0; i < fftLength; i++)
            {
                correlation[i] = whitened[i].Real;
            }
        }

        // The peak of a perfectly aligned unit-phasor sum is weightSum/N, so this
        // normalizes the coefficient to [0, 1].
        double normalizer = weightSum / fftLength;
        return new PhaseTransformCorrelation(correlation, normalizer);
    }

    // Raised-cosine soft gate: 0 below low, 1 above high, a smooth cosine ramp
    // between. Keeps the passband at unity while fading the excitation edges.
    private static double SoftGate(double value, double low, double high)
    {
        if (value <= low)
        {
            return 0.0;
        }
        if (value >= high)
        {
            return 1.0;
        }

        return 0.5 - 0.5 * Math.Cos(Math.PI * (value - low) / (high - low));
    }

    // Sub-sample peak location by a fine windowed-sinc (Lanczos) upsampling around
    // the integer extremum, then one parabolic step between the winning grid node
    // and its neighbours to remove the residual grid quantisation. The PHAT
    // correlation is band-limited, so sinc interpolation is the correct, unbiased
    // reconstruction — unlike a raw 3-point parabola on the samples, which
    // systematically mislocates a sinc-shaped peak. The sign of the extremum is
    // preserved so a polarity-inverted arrival (a trough) refines to its true
    // minimum instead of a nearby positive side lobe.
    internal static double RefinePeakLag(
        double[] correlation,
        int peakLag,
        int fftLength,
        double sign)
    {
        const int upsample = 32;
        const int kernelHalfWidth = 16;
        double step = 1.0 / upsample;
        int bestNode = 0;
        double bestValue = sign * correlation[WrapIndex(peakLag, fftLength)];
        for (int node = -upsample + 1; node < upsample; node++)
        {
            double value = sign * InterpolateCircular(
                correlation, peakLag + node * step, kernelHalfWidth);
            if (value > bestValue)
            {
                bestValue = value;
                bestNode = node;
            }
        }

        // Parabolic vertex between the winning fine node and its two neighbours,
        // reconstructed with the same interpolator so the finish is consistent.
        double center = bestValue;
        double left = sign * InterpolateCircular(
            correlation, peakLag + (bestNode - 1) * step, kernelHalfWidth);
        double right = sign * InterpolateCircular(
            correlation, peakLag + (bestNode + 1) * step, kernelHalfWidth);
        double denominator = left - 2.0 * center + right;
        double vertex = Math.Abs(denominator) > 1e-18
            ? Math.Clamp(0.5 * (left - right) / denominator, -1.0, 1.0)
            : 0.0;

        return peakLag + (bestNode + vertex) * step;
    }

    private static Complex[] RealForwardSpectrum(
        IReadOnlyList<double> signal,
        int fftLength)
    {
        var spectrum = new Complex[fftLength];
        int count = Math.Min(signal.Count, fftLength);
        for (int i = 0; i < count; i++)
        {
            spectrum[i] = new Complex(signal[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        return spectrum;
    }

    private static double InterpolateCircular(
        double[] samples,
        double position,
        int halfWidth)
    {
        int center = (int)Math.Floor(position);
        double sum = 0;
        for (int k = center - halfWidth + 1; k <= center + halfWidth; k++)
        {
            double weight = DspMath.LanczosKernel(position - k, halfWidth);
            if (weight != 0)
            {
                sum += samples[WrapIndex(k, samples.Length)] * weight;
            }
        }

        return sum;
    }

    internal static int WrapIndex(int index, int length) =>
        DspMath.WrapIndex(index, length);

    public static double[] ComputeRelativeIr(
        IReadOnlyList<double> referenceMic,
        IReadOnlyList<double> targetMic,
        double epsilon = 1e-12,
        IReadOnlyList<double>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(referenceMic);
        ArgumentNullException.ThrowIfNull(targetMic);
        if (referenceMic.Count != targetMic.Count)
        {
            throw new ArgumentException("Input arrays must have same length.");
        }
        if (referenceMic.Count == 0)
        {
            throw new ArgumentException("Input arrays must not be empty.");
        }
        if (!double.IsFinite(epsilon) || epsilon < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epsilon));
        }

        int fftLength = DspMath.NextPowerOfTwo(checked(referenceMic.Count * 2));
        var crossSpectrum = new Complex[fftLength];
        var referencePowerSpectrum = new double[fftLength];
        AccumulateFrameSpectra(
            referenceMic,
            targetMic,
            referenceMic.Count,
            crossSpectrum,
            referencePowerSpectrum,
            targetPowerSpectrum: null);

        Complex[] relative = InverseH1Response(
            crossSpectrum,
            referencePowerSpectrum,
            epsilon,
            filter);

        var impulseResponse = new double[fftLength];
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            impulseResponse[i] = relative[i].Real;
        }

        return impulseResponse;
    }
}

public readonly record struct TransferFunctionFrame(
    IReadOnlyList<double> Reference,
    IReadOnlyList<double> Target);

public readonly record struct TransferEstimateResult(
    double[] ImpulseResponse,
    int PeakIndex,
    double[]? Coherence);

/// <summary>
/// A phase-transform (GCC-PHAT) delay estimate. <see cref="LagSamples"/> is the
/// refined lag in the raw correlation-index space of the coarse anchor it was
/// searched around; <see cref="PeakCorrelation"/> is the normalized peak height
/// in [0, 1] (magnitude, so it is polarity-blind); <see cref="Refined"/> is false
/// when the peak sat on the search window edge, meaning the estimate should not be
/// trusted over the anchor.
/// </summary>
public readonly record struct PhaseTransformDelay(
    double LagSamples,
    double PeakCorrelation,
    bool Refined);

/// <summary>
/// A precomputed GCC-PHAT correlation, from
/// <see cref="TransferFunction.ComputePhaseTransformFromResponse"/>. Refine any
/// number of coarse lags of the same capture from it without recomputing the
/// transform.
/// </summary>
public sealed class PhaseTransformCorrelation
{
    private readonly double[] correlation;
    private readonly double normalizer;

    internal PhaseTransformCorrelation(double[] correlation, double normalizer)
    {
        this.correlation = correlation;
        this.normalizer = normalizer;
    }

    /// <summary>
    /// Refines <paramref name="coarseLagSamples"/> to sub-sample precision by the
    /// extremum of the whitened correlation within
    /// <paramref name="searchRadiusSamples"/>. The extremum is taken by magnitude,
    /// so a polarity-inverted arrival (a strong negative trough) is found just as
    /// a normal arrival (a positive peak) is; its sign is preserved through the
    /// interpolation. A peak pinned to the window edge is reported as not refined.
    /// </summary>
    public PhaseTransformDelay RefineAround(int coarseLagSamples, int searchRadiusSamples)
    {
        if (searchRadiusSamples < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(searchRadiusSamples));
        }
        if (normalizer <= 0)
        {
            return new PhaseTransformDelay(coarseLagSamples, 0, false);
        }

        int length = correlation.Length;
        int bestLag = coarseLagSamples;
        double bestMagnitude = -1;
        for (int offset = -searchRadiusSamples; offset <= searchRadiusSamples; offset++)
        {
            int lag = coarseLagSamples + offset;
            double magnitude = Math.Abs(correlation[TransferFunction.WrapIndex(lag, length)]);
            if (magnitude > bestMagnitude)
            {
                bestMagnitude = magnitude;
                bestLag = lag;
            }
        }

        bool interior = Math.Abs(bestLag - coarseLagSamples) < searchRadiusSamples;
        double sign = Math.Sign(correlation[TransferFunction.WrapIndex(bestLag, length)]);
        if (sign == 0)
        {
            sign = 1;
        }

        double refinedLag = interior
            ? TransferFunction.RefinePeakLag(correlation, bestLag, length, sign)
            : bestLag;
        return new PhaseTransformDelay(
            refinedLag,
            bestMagnitude / normalizer,
            interior);
    }
}
