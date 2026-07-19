using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// The outcome of a phase-alignment search: the delay to add and whether the
/// variable channel sums best with its polarity flipped.
/// </summary>
public readonly record struct AlignmentResult(double DelayMs, bool InvertPolarity);

/// <summary>
/// One near-optimal solution of an alignment search: a local optimum of the
/// prior-penalized average-loss score, further penalized by how far its
/// deepest smoothed notch falls below its own average (see
/// <see cref="VirtualCrossoverAnalysis.DipExcessPenaltyWeight"/>). Near a
/// steep crossover several such candidates — the true alignment and its
/// (flip + half-period shift) impostors — can score within fractions of a dB
/// of each other inside the pair band, so the caller may need external
/// evidence to pick between them.
/// <see cref="LossDb"/> is the raw in-band average without the penalties;
/// <see cref="DipDb"/> is the deepest 1/6-octave-smoothed loss notch — the
/// number that separates a smooth shallow loss from a sharp cancellation the
/// average barely notices.
/// </summary>
public sealed record AlignmentCandidate(
    double DelayMs,
    bool InvertPolarity,
    double ScoreDb,
    double LossDb = 0,
    double DipDb = 0);

/// <summary>
/// The broadband leading-edge onset of one processed IR (see
/// <see cref="VirtualCrossoverAnalysis.EstimateBroadbandOnset"/>): the
/// Hilbert-envelope crossings of 10 % (<see cref="EarlyMs"/>), 25 %
/// (<see cref="OnsetMs"/>, the working figure) and 50 % (<see cref="LateMs"/>)
/// of the first credible arrival's peak level, on that peak's own rising
/// front. The crossings are monotonic in the threshold; how far they disagree
/// is the front's sharpness — a sharp direct front keeps them within a
/// fraction of a crossover period, a modal low-frequency build-up spreads
/// them over milliseconds. Callers comparing two channels must gate on the
/// spread of the DIFFERENCE across the three thresholds, not on one channel's
/// spread alone — and on <see cref="SnrDb"/> (the record's strongest envelope
/// peak against its noise floor): a noise-only record still produces three
/// stable-looking crossings, and only the SNR exposes it.
/// </summary>
public readonly record struct BroadbandOnsetEstimate(
    double EarlyMs,
    double OnsetMs,
    double LateMs,
    double SnrDb,
    bool IsValid);

/// <summary>
/// One extremum of a band-limited time-domain cross-correlation search.
/// <see cref="EdgePinned"/> marks an extremum found on (or within a couple of
/// samples of) the lag-window boundary: that is not a measured lobe but the
/// window's cut through one whose true extremum can lie outside, so both the
/// position and the magnitude are artifacts of where the window happened to
/// end — callers gating on either must not trust an edge-pinned value.
/// </summary>
public sealed record CorrelationDelayCandidate(
    double DelayMs,
    double Coefficient,
    bool InvertPolarity,
    bool EdgePinned = false);

/// <summary>
/// Diagnostic result of a time-domain delay search around a crossover.
/// <see cref="DelayMs"/> is the delay to add to the second impulse response
/// passed to the search so it aligns with the first.
/// <see cref="PositiveRival"/> is the strongest OTHER positive local maximum
/// in the window, outside the main peak's own lobe — the same-polarity
/// neighbor a period away that <see cref="Confidence"/> (peak vs trough)
/// cannot see. A trust decision between two same-polarity lobes needs the
/// peak to beat this rival too, or the choice of lobe is ambiguity, not
/// measurement. Null when the window holds no separated positive structure.
/// <see cref="NegativeRival"/> is the same figure for the trough's side: the
/// deepest OTHER negative local minimum outside the trough's own lobe. A
/// caller seeding from a dominant trough owes it the same rival scrutiny a
/// dominant peak gets.
/// </summary>
public sealed record CorrelationAlignmentResult(
    double CenterFrequencyHz,
    double BandLowHz,
    double BandHighHz,
    double SearchRangeMs,
    CorrelationDelayCandidate PositivePeak,
    CorrelationDelayCandidate NegativeTrough,
    CorrelationDelayCandidate? PositiveRival = null,
    CorrelationDelayCandidate? NegativeRival = null)
{
    public CorrelationDelayCandidate BestByMagnitude =>
        Math.Abs(NegativeTrough.Coefficient) > Math.Abs(PositivePeak.Coefficient)
            ? NegativeTrough
            : PositivePeak;

    public double Confidence =>
        Math.Abs(BestByMagnitude.Coefficient) -
        Math.Min(
            Math.Abs(PositivePeak.Coefficient),
            Math.Abs(NegativeTrough.Coefficient));
}

/// <summary>
/// The acoustic polarity read from a measured impulse response.
/// </summary>
public enum PolarityEstimate
{
    Unknown,
    Positive,
    Negative
}

/// <summary>
/// The sample range of a processed impulse response that holds MEASURED
/// content: <see cref="VirtualCrossoverAnalysis.ApplyChain(System.Numerics.Complex[], DspChannelChain, int, out ValidSampleRange)"/>
/// shifts the input by the chain delay (manufacturing silence before
/// <see cref="StartSample"/>) and rounds the FFT length up (manufacturing a
/// tail from <see cref="EndSample"/> on). Envelope noise floors must be read
/// inside the range only — both the delay prefix and the FFT tail collapse a
/// quantile floor — while reported positions stay in full-record coordinates.
/// The default (empty) range means unknown: analyses fall back to the
/// padding-signature heuristic, which can trim only the tail.
/// </summary>
public readonly record struct ValidSampleRange(int StartSample, int EndSample)
{
    /// <summary>Whether the range is known (non-empty).</summary>
    public bool IsKnown => EndSample > StartSample;
}

/// <summary>
/// Time-domain processing for the virtual crossover: applies a channel's DSP
/// chain to its transfer impulse response and sums the processed channels. All
/// transfer IRs share the loopback time reference (sample 0), so a sample-wise
/// sum of the processed responses is exactly what the microphone would capture
/// with every channel playing through its DSP settings — relative delay,
/// polarity and phase included.
/// </summary>
public static class VirtualCrossoverAnalysis
{
    // Zero padding appended before the FFT so the chain's delay shift and the
    // filter ringing tails stay linear instead of wrapping around. The floor
    // (8192 samples ≈ 170 ms at 48 kHz) covers every crossover; the actual pad
    // follows the chain's slowest pole, because a low-frequency high-Q PEQ
    // rings far longer — a 20 Hz / Q 10 boost decays only ~9 dB over the old
    // fixed pad, and the rest wrapped circularly into the early IR, phase and
    // the alignment sums. The cap bounds the FFT growth for pathological
    // settings.
    private const int MinFilterTailPadding = 8192;
    private const int MaxFilterTailPadding = 262_144;
    private const double FilterTailDecayDb = 120.0;

    /// <summary>
    /// Applies the chain to an impulse response by multiplying its spectrum with
    /// the chain response bin by bin (conjugate-mirrored, so a real input stays
    /// real). The result is the impulse response of measurement + DSP.
    /// </summary>
    public static Complex[] ApplyChain(
        Complex[] impulseResponse,
        DspChannelChain chain,
        int sampleRate) =>
        ApplyChain(impulseResponse, chain, sampleRate, out _);

    /// <summary>
    /// The <see cref="ValidSampleRange"/> that
    /// <see cref="ApplyChain(Complex[], DspChannelChain, int, out ValidSampleRange)"/>
    /// reports for an input of <paramref name="inputLength"/> samples through
    /// <paramref name="chain"/> into a record of
    /// <paramref name="outputLength"/>: the input shifted by the chain delay.
    /// Pure arithmetic, so a caller holding a CACHED processed response can
    /// recover the range without re-running the chain. The measurement's own
    /// quiet regions (leading silence, an anechoic tail INSIDE the input)
    /// stay part of the range: they are recorded silence, not padding.
    /// </summary>
    public static ValidSampleRange ChainValidRange(
        int inputLength,
        DspChannelChain chain,
        int sampleRate,
        int outputLength)
    {
        ArgumentNullException.ThrowIfNull(chain);
        // The delay is SIGNED: a positive delay shifts the content right
        // (manufacturing the silent prefix the start excludes), a negative
        // one shifts it left, so the content ENDS earlier — the vacated tail
        // is manufactured silence exactly like the FFT padding, and the head
        // samples the shift pushes past zero wrap to the buffer's far end,
        // outside the range either way.
        double delaySamplesExact = chain.DelayMs / 1_000.0 * sampleRate;
        int startSample = Math.Clamp(
            (int)Math.Floor(Math.Max(0.0, delaySamplesExact)),
            0,
            Math.Max(0, outputLength - 1));
        int endSample = Math.Min(
            outputLength, inputLength + (int)Math.Ceiling(delaySamplesExact));
        // A delay that shifts the whole input out of the record leaves no
        // contiguous measured range: report unknown rather than an empty lie.
        return endSample > startSample
            ? new ValidSampleRange(startSample, endSample)
            : default;
    }

    /// <summary>
    /// <see cref="ApplyChain(Complex[], DspChannelChain, int)"/>, additionally
    /// reporting WHERE the measured content sits inside the returned record
    /// (see <see cref="ValidSampleRange"/>): the chain delay manufactures
    /// silence before it, and the FFT sizing manufactures a tail after it —
    /// zeros for a scale-only chain, the filter kernel's decay otherwise —
    /// neither carrying measurement noise, so envelope/SNR analyses must read
    /// inside the range (see <see cref="EstimateBroadbandOnset"/>).
    /// </summary>
    public static Complex[] ApplyChain(
        Complex[] impulseResponse,
        DspChannelChain chain,
        int sampleRate,
        out ValidSampleRange validRange)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        ArgumentNullException.ThrowIfNull(chain);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "The impulse response is empty.",
                nameof(impulseResponse));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int delaySamples = (int)Math.Ceiling(
            Math.Max(0.0, chain.DelayMs) / 1_000.0 * sampleRate);
        PreparedDspResponse preparedChain = PreparedDspResponse.Create(chain, sampleRate);
        int tailPadding = preparedChain.RequiredTailSamples(
            FilterTailDecayDb, MinFilterTailPadding, MaxFilterTailPadding);
        int length = DspMath.NextPowerOfTwo(
            impulseResponse.Length + delaySamples + tailPadding);
        validRange = ChainValidRange(
            impulseResponse.Length, chain, sampleRate, length);
        if (preparedChain.IsTimeDomainScaleOnly)
        {
            return preparedChain.ApplyTimeDomainScale(impulseResponse, length);
        }

        var spectrum = new Complex[length];
        Array.Copy(impulseResponse, spectrum, impulseResponse.Length);
        Fourier.Forward(spectrum, FourierOptions.Matlab);

        preparedChain.ApplyToSpectrum(spectrum);

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return spectrum;
    }

    /// <summary>Sample-wise sum of the processed channel impulse responses.</summary>
    public static Complex[] SumImpulseResponses(
        IReadOnlyList<Complex[]> impulseResponses)
    {
        ArgumentNullException.ThrowIfNull(impulseResponses);
        if (impulseResponses.Count == 0)
        {
            throw new ArgumentException(
                "At least one impulse response is required.",
                nameof(impulseResponses));
        }

        int length = impulseResponses.Max(ir => ir.Length);
        var sum = new Complex[length];
        foreach (Complex[] ir in impulseResponses)
        {
            for (int i = 0; i < ir.Length; i++)
            {
                sum[i] += ir[i];
            }
        }

        return sum;
    }

    /// <summary>
    /// Finds the extra delay (ms) for one channel that best aligns it with the
    /// already-processed remaining channels: the delay maximizing the energy of
    /// their complex sum inside the given frequency window (the crossover
    /// region). Because the sum energy differs from a constant only by the
    /// cross-spectrum term, the search evaluates Re Σ conj(F)·V·e^{-jωτ} on the
    /// window bins — an exact fractional-delay cross-correlation. A negative
    /// result means the channel should be advanced, i.e. the delay belongs on
    /// the other channels instead.
    /// </summary>
    public static double FindBestDelayMs(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        double minDelayMs = -5,
        double maxDelayMs = 20)
    {
        List<(double OmegaMs, Complex Cross)> crossTerms = BuildCrossTerms(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs,
            maxDelayMs);
        if (crossTerms.Count == 0)
        {
            return 0;
        }

        return SearchBestDelay(
            crossTerms, minDelayMs, maxDelayMs, maxFrequencyHz, allowInvert: false).DelayMs;
    }

    /// <summary>
    /// Finds the delay and polarity of the variable channel that minimize the
    /// average summation loss against the fixed channels inside the frequency
    /// window — the same log-frequency-weighted dB metric the Virtual DSP tool
    /// reports, optimized directly. A raw cross-correlation is deliberately NOT
    /// used here: it weights bins by their energy product, which steep crossover
    /// filters concentrate at the corner frequency, making the true peak and
    /// the (flip + half-period shift) impostor differ by only a few percent —
    /// room reflections then promote the wrong one. The dB average instead
    /// punishes the deep off-corner cancellations the impostor creates across
    /// the rest of the band. The returned invert flag is relative to the
    /// variable IR as passed in (XOR it onto the channel's polarity switch).
    /// The optional prior adds a gentle quadratic dB penalty around an
    /// arrival-based delay estimate — an independent, polarity-blind
    /// observation — as a tie-breaker between genuinely close candidates.
    /// </summary>
    public static AlignmentResult FindBestAlignment(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        double minDelayMs,
        double maxDelayMs,
        double? priorDelayMs = null,
        double priorSigmaMs = 0)
    {
        IReadOnlyList<AlignmentCandidate> candidates = FindAlignmentCandidates(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs,
            maxDelayMs,
            priorDelayMs,
            priorSigmaMs);
        return candidates.Count == 0
            ? new AlignmentResult(0, false)
            : new AlignmentResult(
                candidates[0].DelayMs, candidates[0].InvertPolarity);
    }

    /// <summary>
    /// Like <see cref="FindBestAlignment"/>, but returns every near-optimal
    /// local optimum (best first, within <see cref="CandidateGapDb"/> of the
    /// winner) instead of just the winner. Inside one pair band the true
    /// alignment and a (flip + half-period shift) impostor can be inseparable;
    /// exposing both lets the caller disambiguate with evidence this search
    /// cannot see — typically the channel's other crossover junction.
    /// </summary>
    public static IReadOnlyList<AlignmentCandidate> FindAlignmentCandidates(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        double minDelayMs,
        double maxDelayMs,
        double? priorDelayMs = null,
        double priorSigmaMs = 0,
        bool? forcedPolarity = null) =>
        FindAlignmentCandidates(
            variableImpulseResponse, fixedImpulseResponses, sampleRate,
            minFrequencyHz, maxFrequencyHz, minDelayMs, maxDelayMs,
            priorDelayMs, priorSigmaMs, forcedPolarity, out _);

    /// <summary>
    /// The overload that also reports EVERY refined local optimum (best
    /// first, uncapped, enriched with the prior-free loss diagnostics). The
    /// compact main list is truncated for SELECTION — at most
    /// <see cref="MaxAlignmentCandidates"/> within <see cref="CandidateGapDb"/>
    /// of the winner, judged on the prior-laden score — so a rival margin or
    /// confidence computed over it alone could read "unrivaled" merely
    /// because the rival was cut before the comparison ever ran.
    /// </summary>
    public static IReadOnlyList<AlignmentCandidate> FindAlignmentCandidates(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        double minDelayMs,
        double maxDelayMs,
        double? priorDelayMs,
        double priorSigmaMs,
        bool? forcedPolarity,
        out IReadOnlyList<AlignmentCandidate> allOptima)
    {
        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs,
            maxDelayMs);
        if (bins.Count == 0)
        {
            allOptima = Array.Empty<AlignmentCandidate>();
            return Array.Empty<AlignmentCandidate>();
        }

        return SearchAlignmentCandidatesByLoss(
            bins,
            minDelayMs,
            maxDelayMs,
            maxFrequencyHz,
            priorDelayMs,
            priorSigmaMs,
            forcedPolarity,
            out allOptima);
    }

    /// <summary>
    /// The first-arrival time (ms from the IR start, sub-sample) inside the
    /// driver's own band — the Time Alignment detector run on a band-passed copy
    /// of the response. Restricting to the band keeps the arrival of a
    /// crossover-filtered channel meaningful: out-of-band ringing and noise do
    /// not move the estimate.
    /// </summary>
    public static double FindBandLimitedArrivalMs(
        Complex[] impulseResponse,
        int sampleRate,
        double lowFrequencyHz,
        double highFrequencyHz,
        ValidSampleRange validRange = default) =>
        AnalyzeBandLimitedArrival(
            impulseResponse, sampleRate, lowFrequencyHz, highFrequencyHz,
            validRange)
            .FirstArrivalDelayMilliseconds;

    /// <summary>
    /// The minimum band width (as a high/low frequency ratio: a third of an
    /// octave) a band-limited arrival analysis accepts. Narrower bands leave
    /// the envelope detector too few in-band periods to place an arrival, so
    /// <see cref="AnalyzeBandLimitedArrival"/> refuses them as invalid instead
    /// of silently widening the band — an arrival measured outside the band
    /// the caller asked for answers a different question. Callers admitting
    /// shared bands (the stereo bridge, the L/R pair links) test against the
    /// same figure.
    /// </summary>
    public static readonly double MinimumArrivalBandRatio = Math.Pow(2.0, 1.0 / 3.0);

    /// <summary>
    /// The full band-limited arrival analysis behind
    /// <see cref="FindBandLimitedArrivalMs"/>, including the quality figures
    /// the bare delay hides: <c>IsValid</c> (a silent band reports zeros, not
    /// a real arrival) and the record's signal-to-noise. Callers whose result
    /// hinges on ONE arrival pair — the stereo bridge — must gate on these
    /// instead of trusting the number. The band is analyzed exactly as given
    /// (clamped to the audible range); a band narrower than
    /// <see cref="MinimumArrivalBandRatio"/> is refused as invalid.
    /// </summary>
    public static TimeAlignmentAnalysisResult AnalyzeBandLimitedArrival(
        Complex[] impulseResponse,
        int sampleRate,
        double lowFrequencyHz,
        double highFrequencyHz,
        ValidSampleRange validRange = default)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "The impulse response is empty.",
                nameof(impulseResponse));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        double low = Math.Clamp(lowFrequencyHz, 20, 20_000);
        double high = Math.Min(highFrequencyHz, 20_000);
        if (high < low * MinimumArrivalBandRatio)
        {
            return new TimeAlignmentAnalysisResult(
                Array.Empty<double>(), 0, 0.0, 0, 0.0, 0.0, 0.0, 0.0, 0.0,
                0.0, 0.0, 0.0, false, 0.0, false, 0.0, false, IsValid: false);
        }

        (int startSample, int analysisLength) =
            AnalysisWindow(impulseResponse, validRange);
        var samples = new double[analysisLength];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = impulseResponse[startSample + i].Real;
        }

        // Gentle spectral fades keep the zero-phase bandpass ringing tame, and
        // the kernel-envelope sidelobe rejection inside the peak search tells
        // that ringing from a genuine early arrival by physics, so the search
        // depth matches the analyzer's default 25 dB. The old, shallower 15 dB
        // missed a soft direct rise sitting under a strong in-room modal
        // build-up (the under-seat midbass in its 80-200 Hz pair band) and
        // latched the arrival onto the mode, milliseconds late.
        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            samples,
            sampleRate,
            new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(low * high),
                BandpassPassOctaves = Math.Log2(high / low),
                BandpassFadeOctaves = 1.0
            });
        if (startSample > 0 && result.IsValid)
        {
            // Arrival positions are reported in FULL-record coordinates, so a
            // caller comparing two channels never reads their (differing)
            // delay prefixes as timing. EnvelopeSamples and the envelope peak
            // indices stay window-local: they index the returned envelope
            // array, which covers the analysis window only.
            double startMs = startSample * 1_000.0 / sampleRate;
            result = result with
            {
                FirstArrivalPeakSample =
                    result.FirstArrivalPeakSample + startSample,
                FirstArrivalDelayMilliseconds =
                    result.FirstArrivalDelayMilliseconds + startMs,
                StrongestPeakSample = result.StrongestPeakSample + startSample,
                StrongestDelayMilliseconds =
                    result.StrongestDelayMilliseconds + startMs
            };
        }
        return result;
    }

    /// <summary>
    /// The broadband leading-edge onset of a processed channel IR: the
    /// crossings of the Hilbert envelope at 10 / 25 / 50 % of the FIRST
    /// CREDIBLE ARRIVAL's own peak level, found by walking backward down that
    /// peak's rising front (sub-sample). The arrival peak comes from the same
    /// first-arrival search the Time Alignment detector runs (25 dB depth
    /// below the strongest peak, noise-gated, with the physical rejection of
    /// the Hilbert transform's own symmetric pre-ringing) — thresholds
    /// measured against the whole crop's global maximum would let a stronger
    /// late reflection usurp all three crossings while leaving their spread
    /// deceptively tight, and the backward walk pins every crossing to the
    /// rising front immediately preceding the chosen arrival. A direct sound
    /// weaker than the 25 dB search depth times the dominant arrival instead —
    /// the same convention as every other arrival in the tool.
    /// This is a different observable from
    /// <see cref="FindBandLimitedArrivalMs"/>, which marks the first PEAK of an
    /// octave-band envelope around a junction: two drivers meeting at a
    /// crossover occupy opposite halves of that shared band, so their
    /// envelope-peak times lag their fronts by different rise times (narrower
    /// sub-band → later peak) and the arrival DIFFERENCE carries a systematic
    /// ~1/bandwidth bias — measured at ~0.3-0.4 ms (0.45-0.8 periods) on real
    /// mid/tweeter junctions. The threshold onset marks the front itself, which
    /// is what a human validates on the IR plot, and is bias-free where the
    /// front is sharp (high junctions). At low frequencies the front smears
    /// into modal build-up and the crossing wanders with the threshold — the
    /// 10-vs-50 % spread is one honesty figure callers must gate on;
    /// <see cref="BroadbandOnsetEstimate.SnrDb"/> (the record's envelope
    /// peak-to-noise grade) is the other, refusing noise-only records whose
    /// random crossings can otherwise look stable.
    /// </summary>
    public static BroadbandOnsetEstimate EstimateBroadbandOnset(
        Complex[] impulseResponse,
        int sampleRate,
        ValidSampleRange validRange = default)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "The impulse response is empty.",
                nameof(impulseResponse));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        (int startSample, int analysisLength) =
            AnalysisWindow(impulseResponse, validRange);
        var samples = new double[analysisLength];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = impulseResponse[startSample + i].Real;
        }

        double[] envelope = SignalEnvelope.Envelope(samples);
        // The shared first-arrival physics (depth, noise gate, Hilbert
        // pre-ringing ceiling) with its stock thresholds; no bandpass kernel —
        // the signal is broadband, so only the Hilbert skirt is assumed.
        var defaults = new TimeAlignmentAnalysisOptions();
        PeakSearchResult peakSearch = SignalEnvelope.FindPeak(
            envelope,
            sampleRate,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = defaults.FirstPeakThresholdBelowMaxDb,
                FirstPeakMinimumSnrDb = defaults.FirstPeakMinimumSnrDb,
                SearchWindowMilliseconds = defaults.PeakSearchWindowMilliseconds
            });
        double arrivalPeak = envelope[peakSearch.SelectedIndex];
        if (!(peakSearch.StrongestPeak > 0.0) ||
            !double.IsFinite(peakSearch.StrongestPeak) ||
            !(arrivalPeak > 0.0))
        {
            return new BroadbandOnsetEstimate(0, 0, 0, 0, IsValid: false);
        }

        double snrDb = SignalEnvelope.EstimatePeakConfidenceDecibels(
            envelope, peakSearch.StrongestPeak);
        double early = RisingFrontCrossingMs(
            envelope, peakSearch.SelectedIndex, 0.10 * arrivalPeak, sampleRate);
        double onset = RisingFrontCrossingMs(
            envelope, peakSearch.SelectedIndex, 0.25 * arrivalPeak, sampleRate);
        double late = RisingFrontCrossingMs(
            envelope, peakSearch.SelectedIndex, 0.50 * arrivalPeak, sampleRate);
        // Positions are reported in FULL-record coordinates regardless of the
        // analysis window, so a caller comparing two channels' onsets never
        // sees their (differing) delay prefixes as timing.
        double startMs = startSample * 1_000.0 / sampleRate;
        return new BroadbandOnsetEstimate(
            early + startMs, onset + startMs, late + startMs, snrDb, IsValid: true);
    }

    // How far below the record's peak a sample must sit to count as part of
    // the SYNTHETIC tail rather than the measurement. ApplyChain rounds every
    // processed IR up to a power-of-two FFT length, so the returned record's
    // tail is manufactured — exact zeros for a scale-only chain, the filter
    // kernel's sub-noise decay otherwise. -140 dB sits far below any real
    // measurement's noise floor (loopback captures grade -115 dB at best) and
    // far above numerical residue, so the cut removes only what the padding
    // manufactured.
    private const double SyntheticTailFloorRatio = 1e-7;

    // The minimum share of above-floor samples inside the content region
    // (everything up to the last above-floor sample) for the trailing silence
    // to read as ApplyChain padding. A measured record carries its noise
    // floor in every sample, so its content region is dense right up to where
    // the padding begins; a synthetic or anechoic record is mostly digital
    // silence throughout, and its trailing silence is the honest noise floor
    // it claims to be, not padding.
    private const double PaddedContentMinimumDensity = 0.5;

    // The sample window an envelope analysis reads. Envelope noise floors are
    // quantile-based, and manufactured silence collapses them — a pure-noise
    // 65k record zero-padded to 131k grades ~60 dB SNR instead of ~6, and a
    // 25 ms delay prefix alone lifts a short noise record past the 20 dB
    // onset-lock floor — waving noise-only fronts through every SNR gate
    // (the onset lock, the stereo bridge, the cross-side ladder). The
    // authoritative answer is the caller's ValidSampleRange — the ApplyChain
    // metadata, immune to the amplitude of any individual sample and covering
    // BOTH the delay prefix and the FFT tail. Callers without the metadata
    // fall back to the padding-signature heuristic below, which can trim only
    // the tail.
    private static (int StartSample, int Length) AnalysisWindow(
        Complex[] impulseResponse,
        ValidSampleRange validRange)
    {
        if (validRange.IsKnown)
        {
            int start = Math.Clamp(
                validRange.StartSample, 0, impulseResponse.Length - 1);
            int end = Math.Clamp(
                validRange.EndSample, start + 1, impulseResponse.Length);
            return (start, end - start);
        }

        return (0, AnalysisLength(impulseResponse));
    }

    // Where the record's REAL content ends, by amplitude signature — the
    // fallback for callers without ApplyChain metadata: a trailing sub-floor
    // run behind a DENSE content region is ApplyChain's power-of-two padding
    // and is removed in full — whatever its share of the record, so a short
    // import padded far past its midpoint is caught too — while a sparse
    // record (a synthetic or windowed impulse, mostly digital silence by
    // nature) is analyzed whole.
    private static int AnalysisLength(Complex[] impulseResponse)
    {
        double peak = 0;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(impulseResponse[i].Real));
        }
        if (peak <= 0)
        {
            return impulseResponse.Length;
        }

        double floor = peak * SyntheticTailFloorRatio;
        int last = impulseResponse.Length - 1;
        while (last > 0 && Math.Abs(impulseResponse[last].Real) < floor)
        {
            last--;
        }
        int contentLength = last + 1;
        if (contentLength == impulseResponse.Length)
        {
            return impulseResponse.Length;
        }

        int aboveFloor = 0;
        for (int i = 0; i < contentLength; i++)
        {
            if (Math.Abs(impulseResponse[i].Real) >= floor)
            {
                aboveFloor++;
            }
        }
        return aboveFloor >= contentLength * PaddedContentMinimumDensity
            ? contentLength
            : impulseResponse.Length;
    }

    // The LAST crossing of the level before the peak — the peak's own rising
    // front, immune to anything earlier or later in the crop — with a linear
    // sub-sample refinement. A front still above the level at sample 0 reads
    // as 0 (never negative: the crop boundary is the earliest observable time).
    private static double RisingFrontCrossingMs(
        double[] envelope,
        int peakIndex,
        double level,
        int sampleRate)
    {
        int index = peakIndex;
        while (index > 0 && envelope[index - 1] >= level)
        {
            index--;
        }

        if (index == 0)
        {
            return 0.0;
        }

        double below = envelope[index - 1];
        double above = envelope[index];
        double fraction = above > below ? (level - below) / (above - below) : 0.0;
        return (index - 1 + fraction) * 1_000.0 / sampleRate;
    }

    /// <summary>
    /// Diagnostic crossover-local delay search. Both already-processed channel
    /// IRs are band-limited around the crossover (a smooth octave-wide band) and
    /// their normalized cross-correlation is evaluated in a short lag window,
    /// computed in the frequency domain from the band-weighted cross-spectrum.
    /// Positive peaks indicate normal polarity; strong negative troughs indicate
    /// the same delay would become a peak if the second channel were inverted.
    /// With <paramref name="phaseTransform"/> set the cross-spectrum is whitened
    /// (GCC-PHAT), so the peak tracks the pure phase delay independent of the two
    /// drivers' magnitude shapes.
    /// </summary>
    public static CorrelationAlignmentResult FindBandLimitedCorrelationDelay(
        Complex[] firstImpulseResponse,
        Complex[] secondImpulseResponse,
        int sampleRate,
        double centerFrequencyHz,
        double passOctaves = 1.0,
        double searchRangeMs = 3.0,
        double centerLagMs = 0.0,
        bool phaseTransform = false)
    {
        ArgumentNullException.ThrowIfNull(firstImpulseResponse);
        ArgumentNullException.ThrowIfNull(secondImpulseResponse);
        if (firstImpulseResponse.Length == 0 || secondImpulseResponse.Length == 0)
        {
            throw new ArgumentException("Impulse responses are required.");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(centerFrequencyHz > 0) || !(passOctaves > 0) || !(searchRangeMs > 0))
        {
            throw new ArgumentException("The correlation search settings are invalid.");
        }

        (double[] correlation, double normalizer, double lowHz, double highHz) =
            ComputeBandLimitedCorrelation(
                firstImpulseResponse,
                secondImpulseResponse,
                sampleRate,
                centerFrequencyHz,
                passOctaves,
                phaseTransform);
        // The lag window is centered on the arrival-based estimate, not on zero:
        // a low-frequency junction's relative delay is several milliseconds (the
        // driver arrivals differ that much), so a window around zero would miss it
        // the way the stage-2 fine search would miss it around the wrong base.
        int rangeSamples = Math.Max(1, (int)Math.Round(searchRangeMs / 1000.0 * sampleRate));
        int centerLag = (int)Math.Round(centerLagMs / 1000.0 * sampleRate);
        CorrelationDelayCandidate positive = FindCorrelationExtremum(
            correlation, centerLag, rangeSamples, normalizer, sampleRate,
            findMaximum: true, out int positiveLag);
        CorrelationDelayCandidate negative = FindCorrelationExtremum(
            correlation, centerLag, rangeSamples, normalizer, sampleRate,
            findMaximum: false, out int negativeLag);
        CorrelationDelayCandidate? positiveRival = FindSameSignRival(
            correlation, centerLag, rangeSamples, positiveLag, normalizer,
            sampleRate, findMaximum: true);
        CorrelationDelayCandidate? negativeRival = FindSameSignRival(
            correlation, centerLag, rangeSamples, negativeLag, normalizer,
            sampleRate, findMaximum: false);

        return new CorrelationAlignmentResult(
            centerFrequencyHz,
            lowHz,
            highHz,
            searchRangeMs,
            positive,
            negative,
            positiveRival,
            negativeRival);
    }

    // The shared core of the band-limited correlation searches and curves: the
    // raw (unnormalized) correlation sequence over all circular lags plus the
    // normalizer that maps it into [-1, 1], and the smooth band actually used.
    private static (double[] Correlation, double Normalizer, double LowHz, double HighHz)
        ComputeBandLimitedCorrelation(
            Complex[] firstImpulseResponse,
            Complex[] secondImpulseResponse,
            int sampleRate,
            double centerFrequencyHz,
            double passOctaves,
            bool phaseTransform)
    {
        double nyquist = sampleRate / 2.0;
        double halfOctaves = passOctaves / 2.0;
        double lowHz = Math.Max(20.0, centerFrequencyHz / Math.Pow(2.0, halfOctaves));
        double highHz = Math.Min(nyquist * 0.95, centerFrequencyHz * Math.Pow(2.0, halfOctaves));
        if (highHz <= lowHz)
        {
            highHz = Math.Min(nyquist * 0.95, Math.Max(lowHz * Math.Sqrt(2.0), lowHz + 1.0));
        }

        // Band-limited cross-correlation without an explicit FIR: filtering both
        // IRs by the same band-pass and correlating them equals inverse-
        // transforming their cross-spectrum weighted by the band's SQUARED
        // magnitude. That drops the windowed-sinc's taps, latency and side-lobe
        // ringing, and costs one FFT pair instead of two convolutions. The band
        // is a smooth raised cosine over log frequency, so the correlation it
        // weights stays clean. Padding to len1+len2 keeps the lags we read free
        // of circular wrap-around.
        int fftLength = DspMath.NextPowerOfTwo(
            firstImpulseResponse.Length + secondImpulseResponse.Length);
        Complex[] firstSpectrum = ForwardSpectrum(firstImpulseResponse, fftLength);
        Complex[] secondSpectrum = ForwardSpectrum(secondImpulseResponse, fftLength);

        // In GCC-PHAT mode each bin's cross term is whitened to unit magnitude, so
        // the delay peak depends only on the phase difference between the channels
        // and not on either one's magnitude shape — a sharper, shape-independent
        // delay estimate. The raw mode keeps the true amplitude cross-spectrum.
        var crossSpectrum = new Complex[fftLength];
        double firstEnergy = 0;
        double secondEnergy = 0;
        double weightSum = 0;
        for (int k = 0; k < fftLength; k++)
        {
            double frequency = (double)k / fftLength * sampleRate;
            if (frequency > nyquist)
            {
                // Bins above Nyquist are the conjugate mirror of a real band.
                frequency = sampleRate - frequency;
            }

            double weightSquared = BandWeight(frequency, lowHz, highHz);
            weightSquared *= weightSquared;
            Complex a = firstSpectrum[k];
            Complex b = secondSpectrum[k];
            Complex cross = a * Complex.Conjugate(b);
            if (phaseTransform)
            {
                double magnitude = cross.Magnitude;
                if (magnitude > 1e-20)
                {
                    // Only bins that actually contribute a unit phasor count toward
                    // the normalizer, so a perfect phase alignment still reaches 1.
                    crossSpectrum[k] = weightSquared * cross / magnitude;
                    weightSum += weightSquared;
                }
            }
            else
            {
                crossSpectrum[k] = weightSquared * cross;
                firstEnergy += weightSquared * (a.Real * a.Real + a.Imaginary * a.Imaginary);
                secondEnergy += weightSquared * (b.Real * b.Real + b.Imaginary * b.Imaginary);
            }
        }

        Fourier.Inverse(crossSpectrum, FourierOptions.Matlab);
        var correlation = new double[fftLength];
        for (int i = 0; i < fftLength; i++)
        {
            correlation[i] = crossSpectrum[i].Real;
        }

        // The inverse transform already carries the 1/N of the correlation. In raw
        // mode the Parseval energy sums carry an N, so the coefficient normalizer
        // is sqrt(Ea·Eb)/N; in PHAT mode every bin is a unit phasor, so a perfect
        // phase alignment sums to Σ W², making the normalizer (Σ W²)/N. Either way
        // the result lands in [-1, 1].
        double normalizer = phaseTransform
            ? weightSum / fftLength
            : Math.Sqrt(firstEnergy * secondEnergy) / fftLength;
        return (correlation, normalizer, lowHz, highHz);
    }

    /// <summary>
    /// The band-limited normalized cross-correlation of two processed impulse
    /// responses as a drawable curve: X is the lag in ms — the delay that,
    /// added to the SECOND response, would align it with the first at that
    /// point of the curve — over
    /// <paramref name="centerLagMs"/> ± <paramref name="searchRangeMs"/> at
    /// sample resolution; Y is the correlation coefficient in [-1, 1].
    /// Positive lobes are normal-polarity alignments, negative lobes are the
    /// same alignments with the second channel inverted — the comb the delay
    /// searches choose between, made visible. Same band weighting and
    /// normalization as <see cref="FindBandLimitedCorrelationDelay"/> (see
    /// there), including the <paramref name="phaseTransform"/> (GCC-PHAT)
    /// whitening mode.
    /// </summary>
    public static List<SignalPoint> BandLimitedCorrelationCurve(
        Complex[] firstImpulseResponse,
        Complex[] secondImpulseResponse,
        int sampleRate,
        double centerFrequencyHz,
        double passOctaves = 1.0,
        double searchRangeMs = 3.0,
        double centerLagMs = 0.0,
        bool phaseTransform = false)
    {
        ArgumentNullException.ThrowIfNull(firstImpulseResponse);
        ArgumentNullException.ThrowIfNull(secondImpulseResponse);
        if (firstImpulseResponse.Length == 0 || secondImpulseResponse.Length == 0)
        {
            throw new ArgumentException("Impulse responses are required.");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(centerFrequencyHz > 0) || !(passOctaves > 0) || !(searchRangeMs > 0))
        {
            throw new ArgumentException("The correlation curve settings are invalid.");
        }

        (double[] correlation, double normalizer, _, _) =
            ComputeBandLimitedCorrelation(
                firstImpulseResponse,
                secondImpulseResponse,
                sampleRate,
                centerFrequencyHz,
                passOctaves,
                phaseTransform);

        int fftLength = correlation.Length;
        int rangeSamples = Math.Max(1, (int)Math.Round(searchRangeMs / 1000.0 * sampleRate));
        int centerLag = (int)Math.Round(centerLagMs / 1000.0 * sampleRate);
        var points = new List<SignalPoint>(2 * rangeSamples + 1);
        for (int lag = centerLag - rangeSamples; lag <= centerLag + rangeSamples; lag++)
        {
            double value = correlation[TransferFunction.WrapIndex(lag, fftLength)];
            points.Add(new SignalPoint(
                lag * 1000.0 / sampleRate,
                normalizer > 0 ? value / normalizer : 0));
        }

        return points;
    }

    /// <summary>
    /// One probe of <see cref="JunctionLossSweep"/>: the junction's gated
    /// average loss and 1/6-octave dip with the variable channel delayed by
    /// <see cref="DelayMs"/> (and inverted, when the sweep's polarity is
    /// inverted).
    /// </summary>
    public sealed record JunctionSweepPoint(
        double DelayMs,
        double LossDb,
        double DipDb);

    /// <summary>
    /// The junction summation loss as a function of an EXTRA delay applied to
    /// the variable channel — the prior-free acoustic surface of one
    /// junction, as a drawable curve. Each probe is HONEST: the variable
    /// response is delayed by rotating its FULL spectrum (an exact fractional
    /// delay of the whole record) and the direct-sound gates are re-anchored
    /// on the delayed response by <see cref="MeasureSumLoss"/> — unlike the
    /// <see cref="SumLossEvaluator"/> rotation probe, whose gates stay pinned
    /// where the responses originally sat and which misgrades
    /// multi-millisecond deltas by whole dB. Both responses are placed into a
    /// guard-padded frame before the rotation — a shared silent prefix and
    /// suffix covering the sweep window — so no probed delay can wrap record
    /// content circularly: without the prefix, a negative delay on an
    /// early-peaked record carried the direct sound to the END of the array,
    /// the shared gate (anchored at the remaining, fixed channel) lost the
    /// variable channel entirely, and the "loss" of a one-channel sum read as
    /// a fake perfect 0 dB.
    /// </summary>
    public static List<JunctionSweepPoint> JunctionLossSweep(
        Complex[] variableImpulseResponse,
        Complex[] fixedImpulseResponse,
        int sampleRate,
        double bandLowHz,
        double bandHighHz,
        double startDelayMs,
        double endDelayMs,
        double stepMs,
        bool invertVariable)
    {
        ArgumentNullException.ThrowIfNull(variableImpulseResponse);
        ArgumentNullException.ThrowIfNull(fixedImpulseResponse);
        if (variableImpulseResponse.Length == 0 || fixedImpulseResponse.Length == 0)
        {
            throw new ArgumentException("Impulse responses are required.");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(stepMs > 0) || !(endDelayMs > startDelayMs))
        {
            throw new ArgumentException("The sweep window is invalid.");
        }

        // The guard covers the widest probed shift in either direction (plus a
        // few samples for the fractional-delay sinc tails). The SAME offset is
        // applied to both channels, so their relative timing — the only thing
        // the gate-re-anchoring measurement reads — is untouched.
        int guardSamples = 8 + (int)Math.Ceiling(
            Math.Max(Math.Abs(startDelayMs), Math.Abs(endDelayMs))
            / 1000.0 * sampleRate);
        int contentLength = Math.Max(
            variableImpulseResponse.Length, fixedImpulseResponse.Length);
        int fftLength = DspMath.NextPowerOfTwo(contentLength + 2 * guardSamples);
        var variableFrame = new Complex[fftLength];
        variableImpulseResponse.CopyTo(variableFrame, guardSamples);
        var fixedFrame = new Complex[fftLength];
        fixedImpulseResponse.CopyTo(fixedFrame, guardSamples);
        Complex[] spectrum = ForwardSpectrum(variableFrame, fftLength);
        var points = new List<JunctionSweepPoint>();
        var delayed = new Complex[fftLength];
        for (double delayMs = startDelayMs;
            delayMs <= endDelayMs + 1e-9;
            delayMs += stepMs)
        {
            double sign = invertVariable ? -1.0 : 1.0;
            for (int k = 0; k < fftLength; k++)
            {
                // The rotation frequency follows the conjugate mirror above
                // Nyquist, so the delayed sequence stays real.
                double frequency = (double)k / fftLength * sampleRate;
                if (frequency > sampleRate / 2.0)
                {
                    frequency -= sampleRate;
                }

                delayed[k] = sign * spectrum[k] * Complex.Exp(
                    new Complex(0, -Math.Tau * frequency * delayMs / 1000.0));
            }

            Fourier.Inverse(delayed, FourierOptions.Matlab);
            (double LossDb, double DipDb)? loss = MeasureSumLoss(
                delayed,
                new List<Complex[]> { fixedFrame },
                sampleRate,
                bandLowHz,
                bandHighHz);
            if (loss is { } value)
            {
                points.Add(new JunctionSweepPoint(
                    delayMs, value.LossDb, value.DipDb));
            }
        }

        return points;
    }

    // The strongest local extremum OF THE MAIN EXTREMUM'S SIGN outside its own
    // lobe — the contiguous same-sign region around it — i.e. the same-polarity
    // rival one period over (positive rivals for the peak, negative for the
    // trough). A window boundary lag also qualifies when an opposite-sign gap
    // separates it from the main lobe: a rival cut by the window still
    // testifies, with its truncated value as a bound. Lags still connected to
    // the main lobe never qualify, so the extremum's own slope cannot
    // masquerade as a rival. Null when the window holds no separated same-sign
    // structure.
    private static CorrelationDelayCandidate? FindSameSignRival(
        double[] correlation,
        int centerLag,
        int rangeSamples,
        int mainLag,
        double normalizer,
        int sampleRate,
        bool findMaximum)
    {
        int fftLength = correlation.Length;
        double sign = findMaximum ? 1.0 : -1.0;
        double Value(int lag) =>
            sign * correlation[TransferFunction.WrapIndex(lag, fftLength)];

        int windowLow = centerLag - rangeSamples;
        int windowHigh = centerLag + rangeSamples;
        int lobeLow = mainLag;
        while (lobeLow > windowLow && Value(lobeLow - 1) > 0)
        {
            lobeLow--;
        }
        int lobeHigh = mainLag;
        while (lobeHigh < windowHigh && Value(lobeHigh + 1) > 0)
        {
            lobeHigh++;
        }

        int bestLag = 0;
        double best = 0;
        for (int lag = windowLow; lag <= windowHigh; lag++)
        {
            if (lag >= lobeLow && lag <= lobeHigh)
            {
                continue;
            }
            double value = Value(lag);
            if (value <= best)
            {
                continue;
            }
            bool boundary = lag == windowLow || lag == windowHigh;
            if (boundary || (value >= Value(lag - 1) && value >= Value(lag + 1)))
            {
                best = value;
                bestLag = lag;
            }
        }
        if (best <= 0)
        {
            return null;
        }

        int edgeGuard = Math.Min(CorrelationEdgeGuardSamples, rangeSamples - 1);
        bool edgePinned = Math.Abs(bestLag - centerLag) >= rangeSamples - edgeGuard;
        double refinedLag = edgePinned
            ? bestLag
            : TransferFunction.RefinePeakLag(correlation, bestLag, fftLength, sign);
        return new CorrelationDelayCandidate(
            refinedLag * 1000.0 / sampleRate,
            normalizer > 0 ? sign * best / normalizer : 0,
            InvertPolarity: !findMaximum,
            edgePinned);
    }

    // A smooth band-pass magnitude: a raised cosine over log frequency, one at
    // the band's geometric center and tapering to zero at the octave edges.
    // Unlike a brickwall it folds no ringing into the correlation it weights.
    private static double BandWeight(double frequencyHz, double lowHz, double highHz)
    {
        if (frequencyHz <= lowHz || frequencyHz >= highHz)
        {
            return 0;
        }

        double position = (Math.Log2(frequencyHz) - Math.Log2(lowHz)) /
            (Math.Log2(highHz) - Math.Log2(lowHz));
        return 0.5 - 0.5 * Math.Cos(Math.Tau * position);
    }

    // How close (in lag samples) to the search-window boundary an extremum may
    // sit before it is flagged edge-pinned. A truncated lobe's argmax lands on
    // the boundary itself or, after the discrete grid samples its slope, one
    // sample inside — two samples covers both without reaching lag positions a
    // genuinely interior lobe would occupy (windows are hundreds of samples).
    private const int CorrelationEdgeGuardSamples = 2;

    // The extremum inside the lag window, refined to sub-sample precision with the
    // shared windowed-sinc interpolation (a plain 3-point parabola systematically
    // mislocates a sinc-shaped correlation peak). Positive lags are read at their
    // circular index lag mod N; an edge-pinned extremum stays at its integer lag.
    private static CorrelationDelayCandidate FindCorrelationExtremum(
        double[] correlation,
        int centerLag,
        int rangeSamples,
        double normalizer,
        int sampleRate,
        bool findMaximum,
        out int extremumLag)
    {
        int fftLength = correlation.Length;
        double sign = findMaximum ? 1.0 : -1.0;
        int bestLag = centerLag;
        double best = double.NegativeInfinity;
        for (int lag = centerLag - rangeSamples; lag <= centerLag + rangeSamples; lag++)
        {
            double value = sign * correlation[TransferFunction.WrapIndex(lag, fftLength)];
            if (value > best)
            {
                best = value;
                bestLag = lag;
            }
        }

        extremumLag = bestLag;
        int distance = Math.Abs(bestLag - centerLag);
        // The guard never consumes the whole window: a degenerate few-sample
        // window keeps a non-edge center instead of flagging every lag.
        int edgeGuard = Math.Min(CorrelationEdgeGuardSamples, rangeSamples - 1);
        bool edgePinned = distance >= rangeSamples - edgeGuard;
        // An edge-pinned extremum keeps its integer lag: sub-sample refinement
        // on a cut lobe would drift the reported position toward the true
        // extremum OUTSIDE the window, misstating what was measured.
        double refinedLag = edgePinned
            ? bestLag
            : TransferFunction.RefinePeakLag(correlation, bestLag, fftLength, sign);
        return new CorrelationDelayCandidate(
            refinedLag * 1000.0 / sampleRate,
            normalizer > 0 ? sign * best / normalizer : 0,
            !findMaximum,
            edgePinned);
    }

    // One spectrum bin of the alignment problem: the combined fixed spectrum,
    // the variable spectrum, the 1/f weight that turns a linear-bin average
    // into a log-frequency one, and the precomputed phase-blind magnitude sum
    // (the sum-loss denominator).
    private readonly record struct AlignmentBin(
        double OmegaMs,
        Complex FixedSum,
        Complex Variable,
        double LogWeight,
        double MagnitudeSum);

    // The direct-sound gate applied to every response before the alignment
    // spectra are taken: the same shape as the panel's default frequency-
    // response window (4096 samples, 256-sample cosine fades, anchored one
    // fade-length before the earliest channel peak). Reading the full IR
    // instead would fold the entire room decay into every bin — hundreds of
    // milliseconds of reverberation whose comb structure the alignment cannot
    // change — so the search would optimize (and be misled by) reflections
    // while the panel displays the gated direct sound. One gate shared by all
    // channels, like the metric's shared anchor, so the loss keeps its 0 dB
    // ceiling.
    private const int AlignmentGateLengthSamples = 4096;
    private const int AlignmentGateFadeSamples = 256;

    // The per-bin spectra inside the frequency window, decimated to a bounded
    // bin count so the search stays fast for long IRs. The fixed channels act
    // as one combined source (superposition).
    private static List<AlignmentBin> BuildAlignmentBins(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        double minDelayMs,
        double maxDelayMs)
    {
        ArgumentNullException.ThrowIfNull(variableImpulseResponse);
        ArgumentNullException.ThrowIfNull(fixedImpulseResponses);
        if (variableImpulseResponse.Length == 0 || fixedImpulseResponses.Count == 0)
        {
            throw new ArgumentException("Impulse responses are required.");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(minFrequencyHz > 0) || !(maxFrequencyHz > minFrequencyHz) ||
            minDelayMs >= maxDelayMs)
        {
            throw new ArgumentException("The search window is invalid.");
        }

        int anchor = FindPeakIndex(variableImpulseResponse);
        foreach (Complex[] ir in fixedImpulseResponses)
        {
            anchor = Math.Min(anchor, FindPeakIndex(ir));
        }

        // The earliest arrival must land on the window's plateau, never inside
        // the fade-in: a fade would attenuate the channels' arrivals unequally
        // (they sit at different fade depths) and bias the loss. When the peak
        // sits closer to the start than a full fade, the fade shrinks to fit.
        int leftFadeSamples = Math.Min(AlignmentGateFadeSamples, anchor);
        int gateStart = anchor - leftFadeSamples;
        double[] gate = Windowing.TukeyWindow(
            AlignmentGateLengthSamples,
            2.0 * leftFadeSamples / AlignmentGateLengthSamples,
            2.0 * AlignmentGateFadeSamples / AlignmentGateLengthSamples);

        int length = AlignmentGateLengthSamples;
        Complex[] variableSpectrum = ForwardSpectrum(
            GateDirectSound(variableImpulseResponse, gateStart, gate), length);
        var fixedSpectrum = new Complex[length];
        foreach (Complex[] ir in fixedImpulseResponses)
        {
            Complex[] spectrum = ForwardSpectrum(
                GateDirectSound(ir, gateStart, gate), length);
            for (int i = 0; i < length; i++)
            {
                fixedSpectrum[i] += spectrum[i];
            }
        }

        int firstBin = Math.Max(1, (int)Math.Ceiling(minFrequencyHz * length / sampleRate));
        int lastBin = Math.Min(length / 2 - 1, (int)Math.Floor(maxFrequencyHz * length / sampleRate));
        var bins = new List<AlignmentBin>();
        if (lastBin < firstBin)
        {
            return bins;
        }

        int stride = Math.Max(1, (lastBin - firstBin + 1) / 4_096);
        for (int bin = firstBin; bin <= lastBin; bin += stride)
        {
            double magnitudeSum =
                fixedSpectrum[bin].Magnitude + variableSpectrum[bin].Magnitude;
            if (magnitudeSum > 0)
            {
                double frequencyHz = bin * (double)sampleRate / length;
                // ω per millisecond of delay for this bin's frequency.
                double omegaMs = Math.Tau * frequencyHz / 1_000.0;
                bins.Add(new AlignmentBin(
                    omegaMs,
                    fixedSpectrum[bin],
                    variableSpectrum[bin],
                    1.0 / frequencyHz,
                    magnitudeSum));
            }
        }

        return bins;
    }

    // The per-bin cross spectrum conj(F)·V for the correlation-based delay
    // search — <see cref="FindBestDelayMs"/> keeps the correlation objective
    // because without the polarity freedom the half-period impostor cannot win.
    private static List<(double OmegaMs, Complex Cross)> BuildCrossTerms(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        double minDelayMs,
        double maxDelayMs)
    {
        var crossTerms = new List<(double OmegaMs, Complex Cross)>();
        foreach (AlignmentBin bin in BuildAlignmentBins(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs,
            maxDelayMs))
        {
            Complex cross = Complex.Conjugate(bin.FixedSum) * bin.Variable;
            if (cross != Complex.Zero)
            {
                crossTerms.Add((bin.OmegaMs, cross));
            }
        }

        return crossTerms;
    }

    // Coarse grid, then two refinement passes around the best candidate. The
    // coarse step stays well below the shortest period in the window, so the
    // refinement cannot lock onto a neighboring correlation lobe. With invert
    // allowed the score is |correlation| and the winner's sign decides the flip.
    private static AlignmentResult SearchBestDelay(
        List<(double OmegaMs, Complex Cross)> crossTerms,
        double minDelayMs,
        double maxDelayMs,
        double maxFrequencyHz,
        bool allowInvert)
    {
        double Correlation(double delayMs)
        {
            double sum = 0;
            foreach ((double omegaMs, Complex cross) in crossTerms)
            {
                sum += (cross * Complex.Exp(new Complex(0, -omegaMs * delayMs))).Real;
            }

            return sum;
        }

        double Score(double correlation) =>
            allowInvert ? Math.Abs(correlation) : correlation;

        // The coarse grid is evaluated transposed — outer loop over bins, inner
        // over delays — so each bin rotates by an incremental phasor (one complex
        // multiply per delay) instead of a full Complex.Exp per (bin, delay).
        double coarseStep = Math.Min(0.02, 250.0 / maxFrequencyHz / 4.0);
        int gridCount = Math.Max(
            1,
            (int)Math.Floor((maxDelayMs - minDelayMs) / coarseStep + 1e-9) + 1);
        var gridScores = new double[gridCount];
        foreach ((double omegaMs, Complex cross) in crossTerms)
        {
            Complex rotated = cross * Complex.Exp(new Complex(0, -omegaMs * minDelayMs));
            Complex stepPhasor = Complex.Exp(new Complex(0, -omegaMs * coarseStep));
            for (int i = 0; i < gridCount; i++)
            {
                gridScores[i] += rotated.Real;
                rotated *= stepPhasor;
            }
        }

        double best = minDelayMs;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < gridCount; i++)
        {
            double score = Score(gridScores[i]);
            if (score > bestScore)
            {
                bestScore = score;
                best = minDelayMs + i * coarseStep;
            }
        }

        double step = coarseStep;
        for (int pass = 0; pass < 2; pass++)
        {
            double from = best - step;
            double to = best + step;
            step /= 10.0;
            for (double delay = from; delay <= to; delay += step)
            {
                double score = Score(Correlation(delay));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = delay;
                }
            }
        }

        best = Math.Clamp(best, minDelayMs, maxDelayMs);
        return new AlignmentResult(best, allowInvert && Correlation(best) < 0);
    }

    // A bin's loss ratio is floored at -60 dB so one perfectly cancelled bin
    // cannot dominate the unsmoothed average the way it never would on the
    // smoothed display curve.
    private const double MinBinAmplitudeRatio = 1e-3;

    // The prior penalty at one sigma from the arrival-based estimate. Gentle on
    // purpose: pair-loss differences between genuine candidates run tenths of a
    // dB, so the penalty only breaks near-ties and deters far lobes.
    private const double PriorPenaltyDbAtSigma = 0.25;

    // Candidate reporting: how far behind the winner a local optimum may score
    // and still be returned, and how many candidates are returned at most.
    // Each polarity seeds its own optima, so one lobe can contribute two
    // candidates — the cap leaves room for three lobes even then.
    private const double CandidateGapDb = 1.5;
    private const int MaxAlignmentCandidates = 6;

    /// <summary>
    /// How much a candidate's deepest smoothed notch counts against its score,
    /// per dB the notch falls below the candidate's own in-band average
    /// (penalty = weight × (DipDb − LossDb), ≤ 0). The average alone cannot
    /// tell a smooth −0.7 dB loss from a −0.7 dB average hiding a −5 dB
    /// cancellation notch, so without this the selection tie-breaks (closeness
    /// to the arrival, the wide-window promotion margin) treat them as a
    /// near-tie and routinely keep the notched one. Penalizing the excess over
    /// the average — not the dip itself — leaves a uniformly lossy candidate
    /// unpunished twice. Same weight as the dip penalty in
    /// <see cref="CrossoverAutoSetup"/>.
    /// </summary>
    public const double DipExcessPenaltyWeight = 0.5;

    // Coarse grid, then two refinement passes per surviving local optimum —
    // the same grid scheme as the correlation search, but scoring each delay
    // by the metric the tool actually reports: the log-frequency-weighted
    // average summation loss. Both polarities are evaluated at every delay
    // (they share the rotated spectrum), but each polarity seeds and refines
    // its own local optima: on a max-of-both envelope one polarity edging the
    // other across a whole basin would hide the loser's peak entirely, and the
    // downstream preference for normal polarity within a margin
    // (AlignmentSelection) would have no normal candidate left to prefer.
    // Every local optimum of the coarse grid within the candidate gap is
    // refined and reported, best first. The dip-excess penalty is folded
    // into each optimum's score only after refinement: within one lobe the dip
    // varies slowly with delay, so it re-ranks the lobes against each other
    // without needing to be paid on every grid point.
    private static List<AlignmentCandidate> SearchAlignmentCandidatesByLoss(
        List<AlignmentBin> bins,
        double minDelayMs,
        double maxDelayMs,
        double maxFrequencyHz,
        double? priorDelayMs,
        double priorSigmaMs,
        bool? forcedPolarity,
        out IReadOnlyList<AlignmentCandidate> allOptima)
    {
        double weightSum = 0;
        foreach (AlignmentBin bin in bins)
        {
            weightSum += bin.LogWeight;
        }

        double EvaluatePolarity(double delayMs, bool invert)
        {
            double loss = 0;
            foreach (AlignmentBin bin in bins)
            {
                Complex variable = bin.Variable * Complex.Exp(
                    new Complex(0, -bin.OmegaMs * delayMs));
                Complex sum = invert
                    ? bin.FixedSum - variable
                    : bin.FixedSum + variable;
                loss += bin.LogWeight * Math.Log10(Math.Max(
                    sum.Magnitude / bin.MagnitudeSum,
                    MinBinAmplitudeRatio));
            }

            return loss * 20.0 / weightSum;
        }

        double PriorPenaltyDb(double delayMs)
        {
            if (priorDelayMs is { } prior && priorSigmaMs > 0)
            {
                double distance = (delayMs - prior) / priorSigmaMs;
                return PriorPenaltyDbAtSigma * distance * distance;
            }

            return 0;
        }

        AlignmentCandidate Scored(double delayMs, bool invert) => new(
            delayMs,
            invert,
            EvaluatePolarity(delayMs, invert) - PriorPenaltyDb(delayMs));

        // The coarse grid is evaluated transposed — outer loop over bins, inner
        // over delays — replacing a Complex.Exp per (bin, delay) with one complex
        // multiply. The refinement passes below still score arbitrary delays
        // through Scored(); they touch only a few dozen points per seed.
        double coarseStep = Math.Min(0.02, 250.0 / maxFrequencyHz / 4.0);
        int gridCount = Math.Max(
            1,
            (int)Math.Floor((maxDelayMs - minDelayMs) / coarseStep + 1e-9) + 1);
        var normalDb = new double[gridCount];
        var invertedDb = new double[gridCount];
        foreach (AlignmentBin bin in bins)
        {
            Complex rotated = bin.Variable * Complex.Exp(
                new Complex(0, -bin.OmegaMs * minDelayMs));
            Complex stepPhasor = Complex.Exp(new Complex(0, -bin.OmegaMs * coarseStep));
            for (int i = 0; i < gridCount; i++)
            {
                normalDb[i] += bin.LogWeight * Math.Log10(Math.Max(
                    (bin.FixedSum + rotated).Magnitude / bin.MagnitudeSum,
                    MinBinAmplitudeRatio));
                invertedDb[i] += bin.LogWeight * Math.Log10(Math.Max(
                    (bin.FixedSum - rotated).Magnitude / bin.MagnitudeSum,
                    MinBinAmplitudeRatio));
                rotated *= stepPhasor;
            }
        }

        // Local optima of each polarity's own coarse grid (window edges
        // included): each is the seed of one correlation lobe of that polarity.
        // When the polarity is forced (inherited from a stereo counterpart), only
        // that polarity's grid is seeded, so every candidate is honestly evaluated
        // for the final sign — the reported delay and score always belong to it.
        var seeds = new List<AlignmentCandidate>();
        (double[] Accumulated, bool Invert)[] grids = forcedPolarity switch
        {
            false => [(normalDb, false)],
            true => [(invertedDb, true)],
            _ => [(normalDb, false), (invertedDb, true)],
        };
        foreach ((double[] accumulated, bool invert) in grids)
        {
            var scores = new double[gridCount];
            for (int i = 0; i < gridCount; i++)
            {
                scores[i] = accumulated[i] * 20.0 / weightSum
                    - PriorPenaltyDb(minDelayMs + i * coarseStep);
            }

            for (int i = 0; i < gridCount; i++)
            {
                bool risesBefore = i == 0 || scores[i] >= scores[i - 1];
                bool fallsAfter = i == gridCount - 1 || scores[i] >= scores[i + 1];
                if (risesBefore && fallsAfter)
                {
                    seeds.Add(new AlignmentCandidate(
                        minDelayMs + i * coarseStep, invert, scores[i]));
                }
            }
        }

        var refined = new List<AlignmentCandidate>();
        foreach (AlignmentCandidate seed in seeds)
        {
            AlignmentCandidate best = seed;
            double step = coarseStep;
            for (int pass = 0; pass < 2; pass++)
            {
                // Clamp the refinement span to the window: a seed on an edge
                // would otherwise score points outside it, and the reported
                // score must belong to an in-window delay. The polarity stays
                // the seed's own — each candidate is one polarity's optimum.
                double from = Math.Max(minDelayMs, best.DelayMs - step);
                double to = Math.Min(maxDelayMs, best.DelayMs + step);
                step /= 10.0;
                for (double delay = from; delay <= to; delay += step)
                {
                    AlignmentCandidate candidate = Scored(delay, seed.InvertPolarity);
                    if (candidate.ScoreDb > best.ScoreDb)
                    {
                        best = candidate;
                    }
                }
            }

            refined.Add(best);
        }

        // Enrich every local optimum with the diagnostics the grid score alone
        // hides — the raw in-band average (no prior) and the deepest smoothed
        // notch — and fold the dip excess into the score before any ranking,
        // so a notch-ridden optimum cannot slip through the candidate cut and
        // the downstream near-tie margins as the "equal" of a smooth one.
        for (int i = 0; i < refined.Count; i++)
        {
            (double lossDb, double dipDb) = DetailedLoss(
                bins, weightSum, refined[i].DelayMs, refined[i].InvertPolarity);
            refined[i] = refined[i] with
            {
                ScoreDb = refined[i].ScoreDb
                    + DipExcessPenaltyWeight * (dipDb - lossDb),
                LossDb = lossDb,
                DipDb = dipDb,
            };
        }

        // Best first; drop shadows of a better candidate in the same basin and
        // everything far behind the winner. The UNCAPPED sorted set goes out
        // separately: rival-margin/confidence figures must see every optimum,
        // including the ones this cut removes from the selection list.
        refined.Sort((a, b) => b.ScoreDb.CompareTo(a.ScoreDb));
        allOptima = refined;
        var results = new List<AlignmentCandidate>();
        foreach (AlignmentCandidate candidate in refined)
        {
            if (candidate.ScoreDb < refined[0].ScoreDb - CandidateGapDb)
            {
                break;
            }
            if (results.Count >= MaxAlignmentCandidates)
            {
                break;
            }
            if (results.All(kept =>
                kept.InvertPolarity != candidate.InvertPolarity ||
                Math.Abs(kept.DelayMs - candidate.DelayMs) > coarseStep))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    // The raw in-band average loss (no prior/penalties) and the deepest
    // 1/6-octave-smoothed loss notch of one delay/polarity choice.
    private static (double LossDb, double DipDb) DetailedLoss(
        List<AlignmentBin> bins,
        double weightSum,
        double delayMs,
        bool invert)
    {
        var losses = new double[bins.Count];
        double total = 0;
        for (int i = 0; i < bins.Count; i++)
        {
            AlignmentBin bin = bins[i];
            Complex variable = bin.Variable * Complex.Exp(
                new Complex(0, -bin.OmegaMs * delayMs));
            Complex sum = invert
                ? bin.FixedSum - variable
                : bin.FixedSum + variable;
            double lossDb = 20 * Math.Log10(Math.Max(
                sum.Magnitude / bin.MagnitudeSum, MinBinAmplitudeRatio));
            losses[i] = lossDb;
            total += bin.LogWeight * lossDb;
        }

        // The dip reads the minimum of a 1/6-octave moving average, so a
        // single-bin modal notch cannot pose as the junction's dip while a
        // genuine cancellation trough still reads at full depth.
        double halfWindowRatio = Math.Pow(2, 1.0 / 12);
        double dip = 0;
        double windowSum = 0;
        int lo = 0;
        int hi = 0;
        for (int i = 0; i < bins.Count; i++)
        {
            double center = bins[i].OmegaMs;
            while (hi < bins.Count && bins[hi].OmegaMs <= center * halfWindowRatio)
            {
                windowSum += losses[hi];
                hi++;
            }
            while (bins[lo].OmegaMs < center / halfWindowRatio)
            {
                windowSum -= losses[lo];
                lo++;
            }

            dip = Math.Min(dip, windowSum / (hi - lo));
        }

        return (total / weightSum, dip);
    }

    /// <summary>
    /// Cuts a set of measured IRs to ONE shared direct-sound window: the same
    /// offset (just before the earliest channel's peak) for every channel, so
    /// the inter-channel timing survives intact. The alignment search, the
    /// gated sum loss and the band-limited arrival detector all read the
    /// direct sound near the peak, so running them on the crop instead of the
    /// full capture produces the same results (verified bit-identical final
    /// Auto delay cascades and junction losses on real measurements) at a
    /// fraction of the FFT cost — the capture tail only matters for display.
    /// </summary>
    public static Complex[][] CropSharedDirectSoundWindow(
        IReadOnlyList<Complex[]> impulseResponses,
        int cropLength,
        int prePeakSamples)
    {
        ArgumentNullException.ThrowIfNull(impulseResponses);
        if (cropLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cropLength));
        }

        int earliestPeak = impulseResponses.Min(FindPeakIndex);
        int start = Math.Max(0, earliestPeak - Math.Max(0, prePeakSamples));
        var cropped = new Complex[impulseResponses.Count][];
        for (int channel = 0; channel < impulseResponses.Count; channel++)
        {
            Complex[] ir = impulseResponses[channel];
            int length = Math.Clamp(ir.Length - start, 1, cropLength);
            var slice = new Complex[length];
            Array.Copy(ir, Math.Min(start, ir.Length - 1), slice, 0, length);
            cropped[channel] = slice;
        }

        return cropped;
    }

    /// <summary>
    /// Measures the summation loss of already-settled responses without any
    /// search: the same gated, log-frequency-weighted average and 1/6-octave
    /// dip the alignment score reads, at the responses' current timing. Used
    /// for junctions no search may touch (a mono channel pinned by the other
    /// side's pass). Null when the band holds no usable bins.
    /// </summary>
    public static (double LossDb, double DipDb)? MeasureSumLoss(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz)
    {
        // The delay window only gates parameter validation here — the
        // measurement itself evaluates the responses exactly as given.
        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs: -1,
            maxDelayMs: 1);
        if (bins.Count == 0)
        {
            return null;
        }

        double weightSum = 0;
        foreach (AlignmentBin bin in bins)
        {
            weightSum += bin.LogWeight;
        }

        return DetailedLoss(bins, weightSum, delayMs: 0, invert: false);
    }

    /// <summary>
    /// The gated band level (dB) of one processed response: the same
    /// direct-sound Tukey gate as the alignment spectra, anchored at the
    /// response's own peak, then the 1/f-weighted mean of the bin POWERS
    /// (|H|^2) inside the band, converted back to dB. Averaging energy rather
    /// than dB values is what lets the figure track perceived loudness and
    /// shrug off the narrow interference nulls a reflective cabin riddles the
    /// response with: a deep null drags a dB (geometric-mean) average down by
    /// most of its depth yet removes almost none of the band energy, so the
    /// power average barely moves. The absolute figure carries an arbitrary
    /// reference (the raw transfer-spectrum scale), so it is meant for
    /// DIFFERENCES between responses measured over the same band — e.g. the
    /// L−R level asymmetry of a stereo pair, the companion of the arrival Δ:
    /// timing (ITD) and level (ILD) steer the image together. Null when the
    /// band holds no bins.
    /// </summary>
    public static double? MeasureBandLevelDb(
        Complex[] impulseResponse,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "The impulse response is empty.",
                nameof(impulseResponse));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(minFrequencyHz > 0) || !(maxFrequencyHz > minFrequencyHz))
        {
            throw new ArgumentException("The band is invalid.");
        }

        int anchor = FindPeakIndex(impulseResponse);
        int leftFade = Math.Min(AlignmentGateFadeSamples, anchor);
        double[] gate = Windowing.TukeyWindow(
            AlignmentGateLengthSamples,
            2.0 * leftFade / AlignmentGateLengthSamples,
            2.0 * AlignmentGateFadeSamples / AlignmentGateLengthSamples);
        int length = AlignmentGateLengthSamples;
        Complex[] spectrum = ForwardSpectrum(
            GateDirectSound(impulseResponse, anchor - leftFade, gate), length);

        int firstBin = Math.Max(
            1, (int)Math.Ceiling(minFrequencyHz * length / sampleRate));
        int lastBin = Math.Min(
            length / 2 - 1, (int)Math.Floor(maxFrequencyHz * length / sampleRate));
        double total = 0;
        double weightSum = 0;
        for (int bin = firstBin; bin <= lastBin; bin++)
        {
            double frequencyHz = bin * (double)sampleRate / length;
            double weight = 1.0 / frequencyHz;
            double magnitude = spectrum[bin].Magnitude;
            total += weight * magnitude * magnitude;
            weightSum += weight;
        }

        // Convert the mean power back to dB once, at the end. The 1e-24 floor
        // mirrors the old per-bin 1e-12 magnitude floor (-240 dB).
        return weightSum > 0
            ? 10.0 * Math.Log10(Math.Max(total / weightSum, 1e-24))
            : null;
    }

    /// <summary>
    /// A reusable junction-loss probe for searches that slide ONE channel
    /// against fixed neighbors: the gated alignment spectra are built once
    /// from the responses as given, and every probe rotates the variable
    /// spectrum by e^{-jωΔ} — exactly an extra Δ ms of delay — instead of
    /// re-running the channels' full DSP chains per candidate delta.
    /// <c>Evaluate(0)</c> reproduces <see cref="MeasureSumLoss"/> on the same
    /// responses. The direct-sound gate stays anchored where the responses
    /// currently sit, which is exact for the probes' small deltas (a fraction
    /// of the gate length).
    /// </summary>
    public sealed class SumLossEvaluator
    {
        private readonly List<AlignmentBin> bins;
        private readonly double weightSum;

        private SumLossEvaluator(List<AlignmentBin> bins, double weightSum)
        {
            this.bins = bins;
            this.weightSum = weightSum;
        }

        /// <summary>
        /// Builds an evaluator over the given band — the same gate, bins and
        /// weights as <see cref="MeasureSumLoss"/>. Null when the band holds
        /// no usable bins.
        /// </summary>
        public static SumLossEvaluator? Create(
            Complex[] variableImpulseResponse,
            IReadOnlyList<Complex[]> fixedImpulseResponses,
            int sampleRate,
            double minFrequencyHz,
            double maxFrequencyHz)
        {
            List<AlignmentBin> bins = BuildAlignmentBins(
                variableImpulseResponse,
                fixedImpulseResponses,
                sampleRate,
                minFrequencyHz,
                maxFrequencyHz,
                minDelayMs: -1,
                maxDelayMs: 1);
            if (bins.Count == 0)
            {
                return null;
            }

            double weightSum = 0;
            foreach (AlignmentBin bin in bins)
            {
                weightSum += bin.LogWeight;
            }

            return new SumLossEvaluator(bins, weightSum);
        }

        /// <summary>
        /// The in-band average loss and 1/6-octave dip with the variable
        /// channel delayed by <paramref name="extraDelayMs"/> more.
        /// </summary>
        public (double LossDb, double DipDb) Evaluate(double extraDelayMs) =>
            DetailedLoss(bins, weightSum, extraDelayMs, invert: false);
    }

    private static Complex[] GateDirectSound(
        Complex[] impulseResponse,
        int gateStart,
        double[] gate)
    {
        var gated = new Complex[gate.Length];
        int count = Math.Min(gate.Length, impulseResponse.Length - gateStart);
        for (int i = 0; i < count; i++)
        {
            gated[i] = impulseResponse[gateStart + i] * gate[i];
        }

        return gated;
    }

    private static Complex[] ForwardSpectrum(Complex[] impulseResponse, int length)
    {
        var spectrum = new Complex[length];
        Array.Copy(impulseResponse, spectrum, Math.Min(impulseResponse.Length, length));
        Fourier.Forward(spectrum, FourierOptions.Matlab);
        return spectrum;
    }

    /// <summary>
    /// How far below the loudest combined level in a point's own neighborhood
    /// (±<see cref="SumLossLevelGateReferenceOctaves"/> octaves) the channels'
    /// combined magnitude may fall before the summation loss stops being measured
    /// at that point. Where every channel is filtered that far down, the "loss" is
    /// the phase arithmetic of two noise floors — it swings to deep fake dips well
    /// outside any driver's band — so those points become NaN: the drawn curve
    /// breaks and the average/dip read-outs skip them. Judging each point against a
    /// local reference rather than one global peak keeps a steeply tilted in-room
    /// response (loud bass, quiet treble) measured across its whole range instead
    /// of blanking everything an octave or two above the bass.
    /// </summary>
    public const double SumLossLevelGateDb = 25;

    /// <summary>
    /// The half-width, in octaves, of the neighborhood the summation-loss level
    /// gate (<see cref="SumLossLevelGateDb"/>) measures each point against. One
    /// octave to each side matches the band a crossover junction spans, so the
    /// gate reference tracks the local driver level rather than a distant global
    /// peak.
    /// </summary>
    public const double SumLossLevelGateReferenceOctaves = 1.0;

    /// <summary>
    /// The per-point summation-loss curve (dB, &lt;= 0): the complex sum minus the
    /// phase-blind magnitude sum of the channel curves, over their shared index
    /// grid (truncated to the shortest). Points whose combined magnitude sits more
    /// than <see cref="SumLossLevelGateDb"/> below the loudest combined level in
    /// their own ±<see cref="SumLossLevelGateReferenceOctaves"/>-octave
    /// neighborhood read NaN (see there). This is the single definition the panel's
    /// drawn "Sum loss" curve, <see cref="AverageSumLossDb"/> and
    /// <see cref="MinimumSumLossDb"/> all read, so the drawn and measured loss
    /// cannot drift apart.
    /// </summary>
    public static List<SignalPoint> SumLossCurve(
        IReadOnlyList<SignalPoint> sumCurve,
        IReadOnlyList<IReadOnlyList<SignalPoint>> channelCurves)
    {
        ArgumentNullException.ThrowIfNull(sumCurve);
        ArgumentNullException.ThrowIfNull(channelCurves);

        int count = sumCurve.Count;
        foreach (IReadOnlyList<SignalPoint> curve in channelCurves)
        {
            count = Math.Min(count, curve.Count);
        }

        count = Math.Max(0, count);
        var magnitudeSums = new double[count];
        for (int i = 0; i < count; i++)
        {
            double magnitudeSum = 0;
            foreach (IReadOnlyList<SignalPoint> curve in channelCurves)
            {
                magnitudeSum += DataHelper.DecibelsToAmplitude(curve[i].Y);
            }

            magnitudeSums[i] = magnitudeSum;
        }

        double[] localPeaks = LocalMagnitudePeaks(sumCurve, magnitudeSums, count);
        double gate = DataHelper.DecibelsToAmplitude(-SumLossLevelGateDb);
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            double gateFloor = localPeaks[i] * gate;
            bool measurable = localPeaks[i] > 0 && magnitudeSums[i] >= gateFloor;
            points.Add(new SignalPoint(
                sumCurve[i].X,
                measurable
                    ? sumCurve[i].Y - DataHelper.AmplitudeToDecibels(magnitudeSums[i])
                    : double.NaN));
        }

        return points;
    }

    /// <summary>
    /// For each point, the loudest combined magnitude within
    /// ±<see cref="SumLossLevelGateReferenceOctaves"/> octaves of it — the local
    /// level the summation-loss gate measures that point against, so a steadily
    /// tilted in-room response is judged region by region instead of against one
    /// global (typically bass) peak. Non-finite sums count as zero. Runs in O(n)
    /// with a monotonic deque over the frequency-sorted grid.
    /// </summary>
    private static double[] LocalMagnitudePeaks(
        IReadOnlyList<SignalPoint> curve,
        double[] magnitudeSums,
        int count)
    {
        var peaks = new double[count];
        if (count == 0)
        {
            return peaks;
        }

        var reference = new double[count];
        for (int i = 0; i < count; i++)
        {
            reference[i] = double.IsFinite(magnitudeSums[i]) ? magnitudeSums[i] : 0.0;
        }

        double ratio = Math.Pow(2, SumLossLevelGateReferenceOctaves);
        // Indices of a monotonically decreasing magnitude run over [start, end);
        // the front is the largest reference level inside the current window.
        var deque = new int[count];
        int head = 0;
        int tail = 0;
        int start = 0;
        int end = 0;
        for (int i = 0; i < count; i++)
        {
            double lower = curve[i].X / ratio;
            double upper = curve[i].X * ratio;

            // Grow to the right to cover every point within +1 octave. The bound
            // rises with i, so end never rewinds.
            while (end < count && curve[end].X <= upper)
            {
                while (tail > head && reference[deque[tail - 1]] <= reference[end])
                {
                    tail--;
                }

                deque[tail++] = end;
                end++;
            }

            // Drop points that fell below -1 octave. This bound also only rises.
            while (start < end && curve[start].X < lower)
            {
                if (tail > head && deque[head] == start)
                {
                    head++;
                }

                start++;
            }

            peaks[i] = tail > head ? reference[deque[head]] : reference[i];
        }

        return peaks;
    }

    /// <summary>
    /// The predicted average summation loss (dB, &lt;= 0) of a set of processed
    /// IRs inside a frequency window, computed straight from their spectra:
    /// the same 20·log10(|ΣH| / Σ|H|) the display read-out averages, evaluated
    /// on a 1/24-octave grid with 1/3-octave power smoothing instead of the
    /// display pipeline's gate and options — so an Auto delay proposal can
    /// quote a before/after figure without touching UI state. The optional
    /// per-channel linear gains let the caller preview gain changes the
    /// processing chain has not applied. Null when fewer than two channels or
    /// the window holds no grid points; a lone channel sums to itself.
    /// </summary>
    public static double? PredictedAverageSumLossDb(
        IReadOnlyList<Complex[]> impulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        IReadOnlyList<double>? linearGains = null)
    {
        ArgumentNullException.ThrowIfNull(impulseResponses);
        if (impulseResponses.Count < 2 || maxFrequencyHz <= minFrequencyHz)
        {
            return null;
        }

        int length = DspMath.NextPowerOfTwo(
            impulseResponses.Max(impulseResponse => impulseResponse.Length));
        int bins = length / 2 + 1;
        var sumSpectrum = new Complex[bins];
        var channelPower = new double[impulseResponses.Count][];
        for (int channel = 0; channel < impulseResponses.Count; channel++)
        {
            double gain = linearGains != null ? linearGains[channel] : 1.0;
            var spectrum = new Complex[length];
            Array.Copy(impulseResponses[channel], spectrum, impulseResponses[channel].Length);
            Fourier.Forward(spectrum, FourierOptions.Matlab);
            var power = new double[bins];
            for (int bin = 0; bin < bins; bin++)
            {
                Complex value = spectrum[bin] * gain;
                sumSpectrum[bin] += value;
                power[bin] = value.Real * value.Real + value.Imaginary * value.Imaginary;
            }

            channelPower[channel] = power;
        }

        var sumPower = new double[bins];
        for (int bin = 0; bin < bins; bin++)
        {
            sumPower[bin] =
                sumSpectrum[bin].Real * sumSpectrum[bin].Real +
                sumSpectrum[bin].Imaginary * sumSpectrum[bin].Imaginary;
        }

        // 1/24-octave grid, each point a plain power mean over ±1/6 octave:
        // wide enough that a sharp cancellation null reads as the audible
        // notch it is instead of a -infinity bin, mirroring the smoothed
        // display curves the panel read-out averages.
        const double gridStepOctaves = 1.0 / 24.0;
        double smoothingFactor = Math.Pow(2.0, 1.0 / 6.0);
        double binWidthHz = (double)sampleRate / length;
        double MeanPower(double[] power, double centerHz)
        {
            int first = Math.Max(1, (int)Math.Ceiling(centerHz / smoothingFactor / binWidthHz));
            int last = Math.Min(bins - 1, (int)Math.Floor(centerHz * smoothingFactor / binWidthHz));
            if (last < first)
            {
                return 0;
            }

            double sum = 0;
            for (int bin = first; bin <= last; bin++)
            {
                sum += power[bin];
            }

            return sum / (last - first + 1);
        }

        double totalDb = 0;
        int samples = 0;
        int steps = (int)Math.Floor(
            Math.Log2(maxFrequencyHz / minFrequencyHz) / gridStepOctaves);
        for (int i = 0; i <= steps; i++)
        {
            double frequency = minFrequencyHz * Math.Pow(2.0, i * gridStepOctaves);
            double magnitudeSum = 0;
            foreach (double[] power in channelPower)
            {
                magnitudeSum += Math.Sqrt(MeanPower(power, frequency));
            }

            double coherent = Math.Sqrt(MeanPower(sumPower, frequency));
            if (magnitudeSum <= 0 || coherent <= 0)
            {
                continue;
            }

            totalDb += 20.0 * Math.Log10(coherent / magnitudeSum);
            samples++;
        }

        return samples > 0 ? totalDb / samples : null;
    }

    /// <summary>
    /// The average summation loss (dB, &lt;= 0) inside the frequency window: how
    /// far the complex sum falls short of the phase-blind magnitude sum. The
    /// curves must share one frequency grid (index-aligned).
    /// </summary>
    public static double? AverageSumLossDb(
        IReadOnlyList<SignalPoint> sumCurve,
        IReadOnlyList<IReadOnlyList<SignalPoint>> channelCurves,
        double minFrequencyHz,
        double maxFrequencyHz)
    {
        ArgumentNullException.ThrowIfNull(sumCurve);
        ArgumentNullException.ThrowIfNull(channelCurves);
        if (channelCurves.Count == 0)
        {
            return null;
        }

        double total = 0;
        int samples = 0;
        foreach (SignalPoint point in SumLossCurve(sumCurve, channelCurves))
        {
            if (point.X < minFrequencyHz || point.X > maxFrequencyHz)
            {
                continue;
            }

            if (double.IsFinite(point.Y))
            {
                total += point.Y;
                samples++;
            }
        }

        return samples > 0 ? total / samples : null;
    }

    /// <summary>
    /// The deepest summation-loss point (dB, &lt;= 0) inside the frequency
    /// window — the companion to <see cref="AverageSumLossDb"/> that a narrow
    /// cancellation notch cannot hide from: the average barely moves on a
    /// sharp dip that is plainly audible. The curves must share one frequency
    /// grid (index-aligned) and are expected to be display-smoothed already.
    /// </summary>
    public static double? MinimumSumLossDb(
        IReadOnlyList<SignalPoint> sumCurve,
        IReadOnlyList<IReadOnlyList<SignalPoint>> channelCurves,
        double minFrequencyHz,
        double maxFrequencyHz)
    {
        ArgumentNullException.ThrowIfNull(sumCurve);
        ArgumentNullException.ThrowIfNull(channelCurves);
        if (channelCurves.Count == 0)
        {
            return null;
        }

        double? minimum = null;
        foreach (SignalPoint point in SumLossCurve(sumCurve, channelCurves))
        {
            if (point.X < minFrequencyHz || point.X > maxFrequencyHz)
            {
                continue;
            }

            if (double.IsFinite(point.Y) && (minimum == null || point.Y < minimum))
            {
                minimum = point.Y;
            }
        }

        return minimum;
    }

    /// <summary>
    /// Estimates the acoustic polarity of a measured impulse response from the
    /// sign of its first significant excursion — the direction the cone moves
    /// first. The global extremum is deliberately not used: band-limited drivers
    /// often ring up to a later lobe that is larger than (and opposite to) the
    /// actual arrival, which would misread a correctly wired driver as inverted.
    /// The first lobe reaching a quarter of the absolute peak marks the arrival.
    /// The threshold balances two failure modes: the leading lobe of a wide-band
    /// driver can be well under half the peak (the ringing overtakes it within a
    /// cycle), while anti-aliasing pre-ringing ahead of the arrival — whose lobe
    /// signs are arbitrary — stays around a tenth of the peak.
    /// </summary>
    public static PolarityEstimate EstimatePolarity(Complex[] impulseResponse)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);

        double peak = 0;
        foreach (Complex sample in impulseResponse)
        {
            peak = Math.Max(peak, Math.Abs(sample.Real));
        }

        if (peak <= 0)
        {
            return PolarityEstimate.Unknown;
        }

        double threshold = peak * 0.25;
        foreach (Complex sample in impulseResponse)
        {
            double value = sample.Real;
            if (Math.Abs(value) >= threshold)
            {
                return value > 0
                    ? PolarityEstimate.Positive
                    : PolarityEstimate.Negative;
            }
        }

        return PolarityEstimate.Unknown;
    }

    /// <summary>
    /// The index of the strongest sample — the window anchor of a processed IR.
    /// The summed response is anchored at the earliest channel peak instead (its
    /// own peak can sit between arrivals or vanish under cancellation).
    /// </summary>
    public static int FindPeakIndex(Complex[] impulseResponse)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "The impulse response is empty.",
                nameof(impulseResponse));
        }

        int peakIndex = 0;
        double peakMagnitudeSquared = 0.0;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            Complex sample = impulseResponse[i];
            double magnitudeSquared =
                sample.Real * sample.Real + sample.Imaginary * sample.Imaginary;
            if (magnitudeSquared > peakMagnitudeSquared)
            {
                peakMagnitudeSquared = magnitudeSquared;
                peakIndex = i;
            }
        }

        return peakIndex;
    }
}
