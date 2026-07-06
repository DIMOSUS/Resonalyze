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
            var reference = new Complex[fftLength];
            var target = new Complex[fftLength];
            for (int i = 0; i < sampleCount; i++)
            {
                reference[i] = new Complex(frame.Reference[i], 0.0);
                target[i] = new Complex(frame.Target[i], 0.0);
            }

            Fourier.Forward(reference, FourierOptions.Matlab);
            Fourier.Forward(target, FourierOptions.Matlab);

            for (int bin = 0; bin < fftLength; bin++)
            {
                Complex referenceBin = reference[bin];
                Complex targetBin = target[bin];
                crossSpectrum[bin] += targetBin * Complex.Conjugate(referenceBin);
                referencePowerSpectrum[bin] += MagnitudeSquared(referenceBin);
                targetPowerSpectrum[bin] += MagnitudeSquared(targetBin);
            }
        }

        var relative = new Complex[fftLength];
        var coherence = new double[fftLength / 2 + 1];
        for (int bin = 0; bin < fftLength; bin++)
        {
            relative[bin] = crossSpectrum[bin] / (referencePowerSpectrum[bin] + epsilon);
            if (bin < coherence.Length)
            {
                double denominator = referencePowerSpectrum[bin] * targetPowerSpectrum[bin];
                coherence[bin] = denominator > 0
                    ? Math.Clamp(MagnitudeSquared(crossSpectrum[bin]) / denominator, 0.0, 1.0)
                    : 0.0;
            }
        }

        Fourier.Inverse(relative, FourierOptions.Matlab);

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

    private static double MagnitudeSquared(Complex value) =>
        value.Real * value.Real + value.Imaginary * value.Imaginary;

    /// <summary>
    /// Computes the phase-transform (GCC-PHAT) cross-correlation of two captured
    /// channels, ready to refine one or more coarse lags without recomputing the
    /// transform. The cross-spectrum is whitened to unit magnitude and weighted by
    /// a smooth band mask built from where the reference actually carries energy, so
    /// the correlation collapses to a sharp, low-side-lobe peak at the true
    /// broadband delay — independent of the driver's magnitude shape (and its
    /// polarity: an inverted channel simply flips the peak, which
    /// <see cref="PhaseTransformCorrelation.RefineAround"/> handles).
    /// </summary>
    public static PhaseTransformCorrelation ComputePhaseTransform(
        IReadOnlyList<double> reference,
        IReadOnlyList<double> target,
        IReadOnlyList<double>? filter = null,
        double referenceGate = 0.02)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(target);
        if (reference.Count != target.Count)
        {
            throw new ArgumentException("Input arrays must have same length.");
        }
        if (reference.Count == 0)
        {
            throw new ArgumentException("Input arrays must not be empty.");
        }

        // Pad to twice the length so the lags we read stay free of circular wrap.
        int fftLength = DspMath.NextPowerOfTwo(checked(reference.Count * 2));
        Complex[] referenceSpectrum = RealForwardSpectrum(reference, fftLength);
        Complex[] targetSpectrum = RealForwardSpectrum(target, fftLength);

        var crossSpectrum = new Complex[fftLength];
        var gateReference = new double[fftLength];
        for (int bin = 0; bin < fftLength; bin++)
        {
            crossSpectrum[bin] = targetSpectrum[bin] * Complex.Conjugate(referenceSpectrum[bin]);
            gateReference[bin] = referenceSpectrum[bin].Magnitude;
        }

        return BuildPhaseTransform(crossSpectrum, gateReference, filter, referenceGate);
    }

    /// <summary>
    /// Computes the phase-transform (GCC-PHAT) auto-correlation of a single
    /// loopback-referenced transfer impulse response. Its spectrum already carries
    /// the microphone/loopback cross-phase, so whitening it to unit magnitude over
    /// the band where the response has energy yields the same sharp delay peak as a
    /// two-channel GCC-PHAT — usable where only the transfer IR is available. The
    /// correlation is indexed to match the impulse response, so envelope-peak lags
    /// refine directly.
    /// </summary>
    public static PhaseTransformCorrelation ComputePhaseTransformFromResponse(
        IReadOnlyList<double> impulseResponse,
        double referenceGate = 0.02)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Count == 0)
        {
            throw new ArgumentException("Impulse response must not be empty.");
        }

        // No padding: the transfer IR is already the deconvolved response, so its
        // whitened self-correlation is indexed exactly like the IR/envelope.
        int fftLength = impulseResponse.Count;
        Complex[] spectrum = RealForwardSpectrum(impulseResponse, fftLength);
        var gateReference = new double[fftLength];
        for (int bin = 0; bin < fftLength; bin++)
        {
            gateReference[bin] = spectrum[bin].Magnitude;
        }

        return BuildPhaseTransform(spectrum, gateReference, filter: null, referenceGate);
    }

    // Shared core: whiten the cross-spectrum to unit magnitude, weight it by a soft
    // band mask taken from where the gate reference has energy, and inverse-
    // transform to the correlation.
    private static PhaseTransformCorrelation BuildPhaseTransform(
        Complex[] crossSpectrum,
        double[] gateReference,
        IReadOnlyList<double>? filter,
        double referenceGate)
    {
        int fftLength = crossSpectrum.Length;
        double maxReference = 0;
        for (int bin = 0; bin < fftLength; bin++)
        {
            maxReference = Math.Max(maxReference, gateReference[bin]);
        }

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

    /// <summary>
    /// Convenience one-shot: compute the GCC-PHAT correlation and refine a single
    /// coarse lag. Prefer <see cref="ComputePhaseTransform"/> plus repeated
    /// <see cref="PhaseTransformCorrelation.RefineAround"/> when refining several
    /// lags of the same capture, so the transform runs once.
    /// </summary>
    public static PhaseTransformDelay EstimatePhaseTransformDelay(
        IReadOnlyList<double> reference,
        IReadOnlyList<double> target,
        int coarseLagSamples,
        int searchRadiusSamples,
        IReadOnlyList<double>? filter = null,
        double referenceGate = 0.02) =>
        ComputePhaseTransform(reference, target, filter, referenceGate)
            .RefineAround(coarseLagSamples, searchRadiusSamples);

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
            double weight = Lanczos(position - k, halfWidth);
            if (weight != 0)
            {
                sum += samples[WrapIndex(k, samples.Length)] * weight;
            }
        }

        return sum;
    }

    private static double Lanczos(double x, int a)
    {
        if (x == 0)
        {
            return 1.0;
        }
        if (x <= -a || x >= a)
        {
            return 0.0;
        }

        double piX = Math.PI * x;
        return a * Math.Sin(piX) * Math.Sin(piX / a) / (piX * piX);
    }

    internal static int WrapIndex(int index, int length)
    {
        int wrapped = index % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

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
        var reference = new Complex[fftLength];
        var target = new Complex[fftLength];

        for (int i = 0; i < referenceMic.Count; i++)
        {
            reference[i] = new Complex(referenceMic[i], 0.0);
            target[i] = new Complex(targetMic[i], 0.0);
        }

        Fourier.Forward(reference, FourierOptions.Matlab);
        Fourier.Forward(target, FourierOptions.Matlab);

        var relative = new Complex[fftLength];
        for (int bin = 0; bin < fftLength; bin++)
        {
            Complex numerator = target[bin] * Complex.Conjugate(reference[bin]);
            double denominator =
                reference[bin].Magnitude * reference[bin].Magnitude + epsilon;
            relative[bin] = numerator / denominator;
        }

        if (filter != null && filter.Count == fftLength)
        {
            for (int bin = 0; bin < fftLength; bin++)
            {
                relative[bin] *= filter[bin];
            }
        }

        Fourier.Inverse(relative, FourierOptions.Matlab);

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
/// A precomputed GCC-PHAT cross-correlation, from
/// <see cref="TransferFunction.ComputePhaseTransform"/>. Refine any number of
/// coarse lags of the same capture from it without recomputing the transform.
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
