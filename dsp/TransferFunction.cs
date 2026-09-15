using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Excitation validity as Nyquist fractions: zero outside the achieved edges, unity inside the requested band, raised-cosine
/// ramps inside the fade guard bands. See docs/tech/sweep-measurement.md#excitation-band-gate.
/// </summary>
public readonly record struct ExcitationBandGate(
    double LowZeroNyquistFraction,
    double LowFullNyquistFraction,
    double HighFullNyquistFraction,
    double HighZeroNyquistFraction)
{
    public static ExcitationBandGate FullBand => new(0.0, 0.0, 1.0, 1.0);

    public void Validate()
    {
        if (!double.IsFinite(LowZeroNyquistFraction) ||
            !double.IsFinite(LowFullNyquistFraction) ||
            !double.IsFinite(HighFullNyquistFraction) ||
            !double.IsFinite(HighZeroNyquistFraction) ||
            LowZeroNyquistFraction < 0.0 ||
            LowFullNyquistFraction < LowZeroNyquistFraction ||
            HighFullNyquistFraction <= LowFullNyquistFraction ||
            HighZeroNyquistFraction < HighFullNyquistFraction ||
            HighZeroNyquistFraction > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExcitationBandGate),
                "Excitation gate fractions must satisfy 0 <= lowZero <= lowFull < highFull <= highZero <= 1.");
        }
    }
}

public static class TransferFunction
{
    // Scale-invariant (-100 dB of the peak bin); gates already bias passed bins < 0.25 %. See docs/tech/sweep-measurement.md#h1-transfer-estimate.
    private const double RelativeRegularization = 1e-10;

    // Safety net at the true noise floor only; cannot find the sweep start (leakage skirts). See docs/tech/sweep-measurement.md#h1-transfer-estimate.
    private const double ExcitationGatePowerRatio = 1e-6;
    private const double ExcitationGateFloorShare = 0.04;

    /// <param name="excitationLowNyquistFraction">Legacy edge: ramp sits below the edge, in unexcited bins; prefer the <see cref="ExcitationBandGate"/> overload. 0 disables.</param>
    /// <param name="excitationHighNyquistFraction">Legacy high edge, mirrored toward Nyquist. 1 disables.</param>
    public static TransferEstimateResult ComputeAveragedRelativeIr(
        IReadOnlyList<TransferFunctionFrame> frames,
        double excitationLowNyquistFraction = 0.0,
        double excitationHighNyquistFraction = 1.0)
    {
        if (!double.IsFinite(excitationLowNyquistFraction) ||
            excitationLowNyquistFraction is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(excitationLowNyquistFraction));
        }
        if (!double.IsFinite(excitationHighNyquistFraction) ||
            excitationHighNyquistFraction is < 0.0 or > 1.0 ||
            excitationHighNyquistFraction <= excitationLowNyquistFraction)
        {
            throw new ArgumentOutOfRangeException(nameof(excitationHighNyquistFraction));
        }

        return ComputeAveragedRelativeIr(frames, new ExcitationBandGate(
            excitationLowNyquistFraction * 0.5,
            excitationLowNyquistFraction,
            excitationHighNyquistFraction,
            0.5 * (excitationHighNyquistFraction + 1.0)));
    }

    /// <summary>H1 relative IR gated by <paramref name="excitationGate"/>; the returned coherence carries the same gate.</summary>
    public static TransferEstimateResult ComputeAveragedRelativeIr(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate)
    {
        GatedH1Accumulation accumulation = AccumulateGatedH1(frames, excitationGate);

        Complex[] relative = InverseGatedH1(
            accumulation.CrossSpectrum,
            accumulation.ReferencePowerSpectrum,
            accumulation.GateWeights,
            accumulation.Regularization);

        var impulseResponse = new double[relative.Length];
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
            frames.Count >= 2 ? accumulation.Coherence : null);
    }

    /// <summary>
    /// The H1 estimate stopped before the inverse transform: gated magnitude on its own bin grid (spatial averages need it).
    /// Closed bins are exactly zero, so "never swept" is distinguishable from "low".
    /// </summary>
    public static TransferMagnitudeEstimate ComputeAveragedMagnitude(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate) =>
        ComputeAveragedMagnitudeAndIr(frames, excitationGate, wantImpulseResponse: false)
            .Magnitude;

    /// <summary>Gated magnitude and its impulse response from one accumulation (the forward transforms are the cost).</summary>
    public static (TransferMagnitudeEstimate Magnitude, Complex[]? ImpulseResponse)
        ComputeAveragedMagnitudeAndIr(
            IReadOnlyList<TransferFunctionFrame> frames,
            ExcitationBandGate excitationGate,
            bool wantImpulseResponse = true)
    {
        GatedH1Accumulation accumulation = AccumulateGatedH1(frames, excitationGate);

        int fftLength = accumulation.CrossSpectrum.Length;
        int binCount = fftLength / 2 + 1;
        var magnitude = new double[binCount];
        for (int bin = 0; bin < binCount; bin++)
        {
            double weight = accumulation.GateWeights[bin];
            magnitude[bin] = weight > 0
                ? weight * accumulation.CrossSpectrum[bin].Magnitude /
                    (accumulation.ReferencePowerSpectrum[bin] + accumulation.Regularization)
                : 0.0;
        }

        return (
            new TransferMagnitudeEstimate(
                magnitude,
                frames.Count >= 2 ? accumulation.Coherence : null,
                fftLength),
            wantImpulseResponse
                ? InverseGatedH1(
                    accumulation.CrossSpectrum,
                    accumulation.ReferencePowerSpectrum,
                    accumulation.GateWeights,
                    accumulation.Regularization)
                : null);
    }

    /// <summary>
    /// Compactness of each target's IR against one shared reference transformed once (2n+1 transforms instead of 3n).
    /// Only verdicts return; null for unusable targets. See docs/tech/sweep-measurement.md#run-acceptance.
    /// </summary>
    public static TransferIrCompactness?[] MeasureSingleFrameCompactness(
        IReadOnlyList<double> reference,
        IReadOnlyList<IReadOnlyList<double>> targets,
        ExcitationBandGate excitationGate,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(targets);
        excitationGate.Validate();

        var results = new TransferIrCompactness?[targets.Count];
        int sampleCount = reference.Count;
        if (sampleCount == 0 || targets.Count == 0)
        {
            return results;
        }

        int fftLength = DspMath.NextPowerOfTwo(checked(sampleCount * 2));
        var referenceSpectrum = new Complex[fftLength];
        for (int i = 0; i < sampleCount; i++)
        {
            referenceSpectrum[i] = new Complex(reference[i], 0.0);
        }

        Fourier.Forward(referenceSpectrum, FourierOptions.Matlab);
        var referencePowerSpectrum = new double[fftLength];
        for (int bin = 0; bin < fftLength; bin++)
        {
            referencePowerSpectrum[bin] = MagnitudeSquared(referenceSpectrum[bin]);
        }

        (double[] gateWeights, double regularization) = BuildExcitationGate(
            referencePowerSpectrum,
            excitationGate);

        var targetSpectrum = new Complex[fftLength];
        var crossSpectrum = new Complex[fftLength];
        for (int index = 0; index < targets.Count; index++)
        {
            IReadOnlyList<double> target = targets[index];
            if (target == null || target.Count < sampleCount)
            {
                continue;
            }

            Array.Clear(targetSpectrum);
            for (int i = 0; i < sampleCount; i++)
            {
                targetSpectrum[i] = new Complex(target[i], 0.0);
            }

            Fourier.Forward(targetSpectrum, FourierOptions.Matlab);
            for (int bin = 0; bin < fftLength; bin++)
            {
                crossSpectrum[bin] =
                    targetSpectrum[bin] * Complex.Conjugate(referenceSpectrum[bin]);
            }

            Complex[] response = InverseGatedH1(
                crossSpectrum,
                referencePowerSpectrum,
                gateWeights,
                regularization);
            results[index] = TransferIrDiagnostics.MeasureCompactness(response, sampleRate);
        }

        return results;
    }

    private static GatedH1Accumulation AccumulateGatedH1(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one transfer frame is required.", nameof(frames));
        }
        excitationGate.Validate();

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

        // Coherence debiased by the average count: raw MSC of pure noise reads 1/K (0.5 at K=2), straddling downstream thresholds.
        (double[] gateWeights, double regularization) = BuildExcitationGate(
            referencePowerSpectrum,
            excitationGate);

        double[] coherence = SpectrumAnalysis.DebiasCoherence(
            SpectrumAnalysis
                .ComputeCoherence(
                    crossSpectrum,
                    referencePowerSpectrum,
                    targetPowerSpectrum,
                    epsilon: 0.0)[..(fftLength / 2 + 1)],
            frames.Count);

        // Coherence carries the gate: deterministic leakage below the sweep start reads gamma^2 ~1 where H1 is zeroed.
        for (int bin = 0; bin < coherence.Length; bin++)
        {
            coherence[bin] *= gateWeights[bin];
        }

        return new GatedH1Accumulation(
            crossSpectrum,
            referencePowerSpectrum,
            gateWeights,
            regularization,
            coherence);
    }

    private readonly record struct GatedH1Accumulation(
        Complex[] CrossSpectrum,
        double[] ReferencePowerSpectrum,
        double[] GateWeights,
        double Regularization,
        double[] Coherence);

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

    // Real, Hermitian-symmetric weights (zero-phase). Peak scan uses only full-weight bins so hum or DC cannot scale the gate.
    // See docs/tech/sweep-measurement.md#h1-transfer-estimate.
    private static (double[] Weights, double Regularization) BuildExcitationGate(
        double[] referencePowerSpectrum,
        ExcitationBandGate gate)
    {
        int fftLength = referencePowerSpectrum.Length;
        int half = fftLength / 2;
        double NyquistFraction(int bin) =>
            Math.Min(bin, fftLength - bin) / (double)half;

        bool hasLowEdge = gate.LowFullNyquistFraction > 0.0;
        bool hasHighEdge = gate.HighFullNyquistFraction < 1.0;

        double maxReferencePower = 0;
        for (int bin = 1; bin < fftLength; bin++)
        {
            double fraction = NyquistFraction(bin);
            if ((!hasLowEdge || fraction >= gate.LowFullNyquistFraction) &&
                (!hasHighEdge || fraction <= gate.HighFullNyquistFraction))
            {
                maxReferencePower = Math.Max(maxReferencePower, referencePowerSpectrum[bin]);
            }
        }

        double gateHigh = maxReferencePower * ExcitationGatePowerRatio;
        double gateLow = gateHigh * ExcitationGateFloorShare;
        var weights = new double[fftLength];
        for (int bin = 0; bin < fftLength; bin++)
        {
            double weight = DspMath.RaisedCosineGate(
                referencePowerSpectrum[bin], gateLow, gateHigh);
            if (weight > 0 && hasLowEdge)
            {
                weight *= DspMath.RaisedCosineGate(
                    NyquistFraction(bin),
                    gate.LowZeroNyquistFraction,
                    gate.LowFullNyquistFraction);
            }
            if (weight > 0 && hasHighEdge)
            {
                weight *= 1.0 - DspMath.RaisedCosineGate(
                    NyquistFraction(bin),
                    gate.HighFullNyquistFraction,
                    gate.HighZeroNyquistFraction);
            }
            weights[bin] = weight;
        }

        return (weights, maxReferencePower * RelativeRegularization);
    }

    private static Complex[] InverseGatedH1(
        Complex[] crossSpectrum,
        double[] referencePowerSpectrum,
        double[] weights,
        double regularization)
    {
        int fftLength = crossSpectrum.Length;
        var relative = new Complex[fftLength];
        for (int bin = 0; bin < fftLength; bin++)
        {
            if (weights[bin] > 0)
            {
                relative[bin] = weights[bin] * crossSpectrum[bin]
                    / (referencePowerSpectrum[bin] + regularization);
            }
        }

        Fourier.Inverse(relative, FourierOptions.Matlab);
        return relative;
    }

    private static double MagnitudeSquared(Complex value) =>
        value.Real * value.Real + value.Imaginary * value.Imaginary;

    /// <summary>
    /// GCC-PHAT correlation of a loopback-referenced transfer IR, index-aligned with it. See docs/tech/sweep-measurement.md#gcc-phat.
    /// </summary>
    /// <param name="coherence">Optional half-spectrum gamma^2 from the same transfer FFT; soft-weights bins. Wrong length is ignored (bit-identical result).</param>
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

        // Power of two keeps MathNet off the slow Bluestein path; zero-padding does not move the peak.
        int fftLength = DspMath.NextPowerOfTwo(impulseResponse.Count);
        return ComputePhaseTransformFromSpectrum(
            RealForwardSpectrum(impulseResponse, fftLength), referenceGate, coherence);
    }

    /// <summary>Correlation from a caller-held forward spectrum on the correlation grid; read-only.</summary>
    internal static PhaseTransformCorrelation ComputePhaseTransformFromSpectrum(
        Complex[] spectrum,
        double referenceGate = 0.02,
        IReadOnlyList<double>? coherence = null)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        if (spectrum.Length == 0)
        {
            throw new ArgumentException("Spectrum must not be empty.");
        }

        var gateReference = new double[spectrum.Length];
        for (int bin = 0; bin < spectrum.Length; bin++)
        {
            gateReference[bin] = spectrum[bin].Magnitude;
        }

        return BuildPhaseTransform(spectrum, gateReference, filter: null, referenceGate, coherence);
    }

    // Floored-linear, not a bin selector: refinement precision needs occupied bandwidth. See docs/tech/sweep-measurement.md#gcc-phat.
    private const double CoherenceWeightFloor = 0.25;

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

        // Apply only on an exact length match; another grid would misattribute SNR to wrong bins.
        int half = fftLength / 2;
        bool useCoherence = coherence != null && coherence.Count == half + 1;

        // Soft band mask: a brick wall rings into side lobes that bias sub-sample refinement.
        double gateHigh = maxReference * referenceGate;
        double gateLow = gateHigh * 0.2;
        var whitened = new Complex[fftLength];
        double weightSum = 0;
        for (int bin = 0; bin < fftLength; bin++)
        {
            double bandWeight = DspMath.RaisedCosineGate(
                gateReference[bin], gateLow, gateHigh);
            if (bandWeight <= 0)
            {
                continue;
            }

            if (useCoherence)
            {
                // Bin and its Hermitian mirror fold to one weight, keeping the inverse transform real.
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

                // Complement form: exactly 1.0 at g2 == 1 for any floor, so unit coherence is a guaranteed no-op.
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

        double normalizer = weightSum / fftLength;
        return new PhaseTransformCorrelation(correlation, normalizer);
    }

    // Lanczos upsampling then a parabolic step: the band-limited peak is sinc-shaped (a raw 3-point parabola is biased).
    // Extremum sign is kept so an inverted arrival refines to its trough.
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
}

public readonly record struct TransferFunctionFrame(
    IReadOnlyList<double> Reference,
    IReadOnlyList<double> Target);

/// <summary>Gated transfer magnitude (linear, bins 0..FftLength/2; zero = gate closed) and coherence (null for one frame).</summary>
public readonly record struct TransferMagnitudeEstimate(
    double[] Magnitude,
    double[]? Coherence,
    int FftLength);

public readonly record struct TransferEstimateResult(
    double[] ImpulseResponse,
    int PeakIndex,
    double[]? Coherence);

/// <summary>
/// GCC-PHAT delay: <see cref="LagSamples"/> in the anchor's correlation-index space; <see cref="PeakCorrelation"/> in [0, 1],
/// polarity-blind; <see cref="Refined"/> false when the peak sat on the window edge.
/// </summary>
public readonly record struct PhaseTransformDelay(
    double LagSamples,
    double PeakCorrelation,
    bool Refined);

/// <summary>Precomputed GCC-PHAT correlation; refine many coarse lags of one capture without recomputing.</summary>
public sealed class PhaseTransformCorrelation
{
    private readonly double[] correlation;
    private readonly double normalizer;

    internal PhaseTransformCorrelation(double[] correlation, double normalizer)
    {
        this.correlation = correlation;
        this.normalizer = normalizer;
    }

    /// <summary>Sub-sample extremum by magnitude within the radius (inverted arrivals found too); edge-pinned peaks are not refined.</summary>
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
