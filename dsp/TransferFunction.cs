using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// The spectral validity of a band-limited excitation, as fractions of Nyquist.
/// Weight is zero outside [LowZero, HighZero] (the achieved sweep edges — nothing
/// was excited beyond them), unity inside [LowFull, HighFull] (the requested band,
/// excited at full amplitude), and a raised cosine across the fade guard bands
/// between them. Placing the ramps INSIDE the excited guard bands matters: a ramp
/// below the achieved edge half-passes bins the sweep never reached, where
/// Gxy/Gxx is microphone noise over the reference's leakage skirt — garbage that
/// shows up as large spikes just outside the sweep band.
/// </summary>
public readonly record struct ExcitationBandGate(
    double LowZeroNyquistFraction,
    double LowFullNyquistFraction,
    double HighFullNyquistFraction,
    double HighZeroNyquistFraction)
{
    /// <summary>A gate that passes everything — for full-band excitation.</summary>
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

/// <summary>
/// Estimates relative impulse responses between two captured channels.
/// </summary>
public static class TransferFunction
{
    // H1 regularization relative to the strongest excitation bin. An absolute
    // epsilon changes meaning with the record level and with the accumulated
    // average count (the spectra below are unnormalized running sums), so its
    // strength silently drifted between measurements; -100 dB of the peak bin
    // is scale-invariant. Note it does no gating work of its own: any bin the
    // gates let through already has at least gateLow (-74 dB re max) in the
    // denominator, so λ biases it by under 0.25% — it only keeps the division
    // Tikhonov-safe should the gate constants ever loosen.
    private const double RelativeRegularization = 1e-10;

    // The power gate: bins whose reference power sits more than 60 dB under
    // the strongest excitation bin fade out over a raised cosine that reaches
    // zero another 14 dB down. This is only a safety net for bins at the true
    // capture noise floor (below -90 dB even for 16-bit loopbacks) — it
    // deliberately canNOT mark the region below the sweep start, because at
    // measurement FFT lengths the sweep's own spectral leakage skirts hold
    // the reference power at just -40..-20 dB re max all the way down to DC
    // (verified on reconstructed field captures), while genuinely excited
    // bins reach -45 dB (~36 dB of 1/f tilt across 12 octaves plus the
    // first-octave fade-in). Cutting below the sweep start is the caller's
    // job via the explicit excitation edge — the sweep parameters are known,
    // the spectrum alone cannot reveal them.
    private const double ExcitationGatePowerRatio = 1e-6;
    private const double ExcitationGateFloorShare = 0.04;

    /// <param name="excitationLowNyquistFraction">
    /// The excitation's low edge as a fraction of Nyquist. LEGACY edge shape:
    /// bins fade out over a raised cosine between this frequency and half of
    /// it — i.e. the ramp sits below the edge, in unexcited territory. Callers
    /// that know the achieved and the requested band should use the
    /// <see cref="ExcitationBandGate"/> overload, whose ramps live inside the
    /// excited fade regions. Zero (the default) disables the edge.
    /// </param>
    /// <param name="excitationHighNyquistFraction">
    /// The excitation's high edge as a fraction of Nyquist, mirroring the low
    /// edge (legacy ramp toward Nyquist). One (the default) disables the edge.
    /// </param>
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

    /// <summary>
    /// H1 estimate of the relative impulse response, with spectral validity
    /// taken from <paramref name="excitationGate"/>: zero outside the achieved
    /// sweep band, unity inside the requested band, raised-cosine ramps across
    /// the fade guard bands between them. The returned coherence carries the
    /// same validity — the reference's leakage skirts are deterministic across
    /// runs, so raw γ² reads ~1 exactly where the estimate is zeroed as
    /// unexcited.
    /// </summary>
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
    /// The same H1 estimate as <see cref="ComputeAveragedRelativeIr"/>, stopped
    /// one step earlier: the gated transfer MAGNITUDE on its own bin grid, with
    /// no inverse transform and no window.
    /// </summary>
    /// <remarks>
    /// This is what a spatial average needs from a microphone. It wants the
    /// steady-state magnitude — the whole decay, no gate — and taking that from an
    /// impulse response means an inverse transform followed immediately by a
    /// forward one over the same full length, which returns exactly this array.
    /// <para>
    /// Bins the excitation gate closed come back as zero rather than as a very
    /// small number, so a caller can tell "the sweep never went here" from "the
    /// response is low here" — the first is a gap in the measurement and must not
    /// become a −200 dB point on a curve.
    /// </para>
    /// </remarks>
    public static TransferMagnitudeEstimate ComputeAveragedMagnitude(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate) =>
        ComputeAveragedMagnitudeAndIr(frames, excitationGate, wantImpulseResponse: false)
            .Magnitude;

    /// <summary>
    /// The gated magnitude AND the impulse response it came from, from one
    /// accumulation of the frames.
    /// </summary>
    /// <remarks>
    /// A caller that wants the steady-state magnitude and also has to judge whether
    /// the channel measured anything at all needs both, and the accumulation — the
    /// forward transform of every frame — is what costs. The inverse transform on top
    /// of it is one more pass over one array.
    /// </remarks>
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
    /// One frame's impulse response for each of several targets that share a single
    /// reference — the whole point being that the reference is transformed ONCE.
    /// </summary>
    /// <remarks>
    /// A run of a microphone array is exactly this shape: one loopback recorded
    /// beside every microphone, sample for sample. Judging each microphone on its own
    /// transformed that same loopback again for every one of them, which is most of
    /// the work: n targets cost 3n transforms that way and 2n + 1 this way. Measured
    /// on eight channels of a 96 kHz / 20 s take, **3993 ms one at a time against
    /// 2093 ms** — the transforms account for most of it and the reused scratch
    /// buffers for the rest, which at 4 194 304 bins is 64 MB an allocation the
    /// garbage collector no longer sees. The answers are identical bin for bin,
    /// because the excitation gate and the regularization are functions of the
    /// reference alone.
    /// <para>
    /// Entries are null where the target is unusable (empty, or shorter than the
    /// reference); the caller decides what that means.
    /// </para>
    /// </remarks>
    public static Complex[]?[] ComputeSingleFrameIrs(
        IReadOnlyList<double> reference,
        IReadOnlyList<IReadOnlyList<double>> targets,
        ExcitationBandGate excitationGate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(targets);
        excitationGate.Validate();

        var results = new Complex[]?[targets.Count];
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

        // Built from the reference, so it is the same gate for every target.
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

            results[index] = InverseGatedH1(
                crossSpectrum,
                referencePowerSpectrum,
                gateWeights,
                regularization);
        }

        return results;
    }

    // The shared core of both estimates: the cross/auto spectra summed over the
    // frames, the excitation gate built from the reference, and the debiased
    // coherence carrying that same gate.
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

        // γ² from the shared cross/auto-spectra formula; epsilon 0 keeps the
        // previous denominator > 0 gate. Only the first half is retained — the
        // upper half mirrors it for the real inputs here. The raw estimate is
        // debiased by the average count before anything stores or consumes it:
        // at 2-4 averages the raw MSC of pure noise reads 1/K (0.5 at K=2 —
        // straddling the very thresholds the unwrap and the PHAT weighting
        // trust), which is estimator bias, not information.
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

        // The gate is the estimate's validity, and the coherence must carry
        // it too: below the sweep start the sweep's own deterministic leakage
        // repeats across runs, so raw γ² reads ~1 exactly where H1 is zeroed
        // as unexcited — and downstream consumers (the phase unwrap's trust
        // gate, the PHAT weighting, the plotted coherence curve) would keep
        // presenting those bins as reliable.
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

    // The validity of every bin of the H1 estimate: the excitation edge from
    // the caller's known sweep start times the power-floor safety net read
    // from the reference's own accumulated spectrum. The weights are real and
    // Hermitian-symmetric (frequencies fold through min(bin, N - bin); a real
    // capture's power spectrum already is), so applying them is zero-phase
    // filtering: nothing moves in time. The peak scan that anchors the power
    // thresholds and the regularization only looks at bins at FULL edge
    // weight: a bin the estimate discards or attenuates must not scale the
    // gate it is excluded from — a loud mains-adjacent hum below (or inside
    // the ramp of) a narrow sweep's start, or DC, whose converter-offset
    // splatter can rival the sweep bins at measurement FFT lengths, would
    // otherwise fade genuinely excited bins.
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

        // The peak scan only trusts bins at FULL edge weight (see the method
        // comment) — that is the requested band, not the fade guard bands.
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
                // Zero below the achieved low edge (nothing was excited there),
                // rising to unity where the fade-in completes.
                weight *= DspMath.RaisedCosineGate(
                    NyquistFraction(bin),
                    gate.LowZeroNyquistFraction,
                    gate.LowFullNyquistFraction);
            }
            if (weight > 0 && hasHighEdge)
            {
                // Mirror: unity where the fade-out starts, zero above the
                // achieved high edge.
                weight *= 1.0 - DspMath.RaisedCosineGate(
                    NyquistFraction(bin),
                    gate.HighFullNyquistFraction,
                    gate.HighZeroNyquistFraction);
            }
            weights[bin] = weight;
        }

        return (weights, maxReferencePower * RelativeRegularization);
    }

    // The H1 estimate cross / (auto + λ), shaped by the validity weights and
    // transformed back to the time domain.
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
    /// repeat across averages (noise, level- or drift-varying distortion,
    /// non-averaging reflections) carry less say in the whitened correlation.
    /// Repeatable content — including stationary harmonic distortion — reads as
    /// coherent and is not de-weighted. It must come from
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
            double bandWeight = DspMath.RaisedCosineGate(
                gateReference[bin], gateLow, gateHigh);
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
}

public readonly record struct TransferFunctionFrame(
    IReadOnlyList<double> Reference,
    IReadOnlyList<double> Target);

/// <summary>
/// The gated transfer magnitude on its own bin grid, with the coherence that
/// judges it and the transform length the bins are spaced by
/// (<c>sampleRate / FftLength</c> hertz per bin).
/// </summary>
/// <param name="Magnitude">
/// Linear |H| per bin, index 0..FftLength/2. Zero where the excitation gate is
/// closed — a bin the sweep never reached, not a quiet one.
/// </param>
/// <param name="Coherence">Null for a single frame, which has none to give.</param>
public readonly record struct TransferMagnitudeEstimate(
    double[] Magnitude,
    double[]? Coherence,
    int FftLength);

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
