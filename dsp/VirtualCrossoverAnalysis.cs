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
/// One extremum of a band-limited time-domain cross-correlation search.
/// </summary>
public sealed record CorrelationDelayCandidate(
    double DelayMs,
    double Coefficient,
    bool InvertPolarity);

/// <summary>
/// Diagnostic result of a time-domain delay search around a crossover.
/// <see cref="DelayMs"/> is the delay to add to the second impulse response
/// passed to the search so it aligns with the first.
/// </summary>
public sealed record CorrelationAlignmentResult(
    double CenterFrequencyHz,
    double BandLowHz,
    double BandHighHz,
    double SearchRangeMs,
    CorrelationDelayCandidate PositivePeak,
    CorrelationDelayCandidate NegativeTrough)
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
        int sampleRate)
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
        double priorSigmaMs = 0)
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
            return Array.Empty<AlignmentCandidate>();
        }

        return SearchAlignmentCandidatesByLoss(
            bins,
            minDelayMs,
            maxDelayMs,
            maxFrequencyHz,
            priorDelayMs,
            priorSigmaMs);
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
        double highFrequencyHz) =>
        AnalyzeBandLimitedArrival(
            impulseResponse, sampleRate, lowFrequencyHz, highFrequencyHz)
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
        double highFrequencyHz)
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

        var samples = new double[impulseResponse.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = impulseResponse[i].Real;
        }

        // Gentle spectral fades and a moderate threshold: the zero-phase bandpass
        // rings symmetrically around the arrival, and steep edges plus a deep
        // threshold would let the detector fire on a pre-ringing lobe.
        return TimeAlignmentAnalysis.Analyze(
            samples,
            sampleRate,
            new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(low * high),
                BandpassPassOctaves = Math.Log2(high / low),
                BandpassFadeOctaves = 1.0,
                FirstPeakThresholdBelowMaxDb = 15
            });
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
        // The lag window is centered on the arrival-based estimate, not on zero:
        // a low-frequency junction's relative delay is several milliseconds (the
        // driver arrivals differ that much), so a window around zero would miss it
        // the way the stage-2 fine search would miss it around the wrong base.
        int rangeSamples = Math.Max(1, (int)Math.Round(searchRangeMs / 1000.0 * sampleRate));
        int centerLag = (int)Math.Round(centerLagMs / 1000.0 * sampleRate);
        CorrelationDelayCandidate positive = FindCorrelationExtremum(
            correlation, centerLag, rangeSamples, normalizer, sampleRate,
            findMaximum: true);
        CorrelationDelayCandidate negative = FindCorrelationExtremum(
            correlation, centerLag, rangeSamples, normalizer, sampleRate,
            findMaximum: false);

        return new CorrelationAlignmentResult(
            centerFrequencyHz,
            lowHz,
            highHz,
            searchRangeMs,
            positive,
            negative);
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
        bool findMaximum)
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

        bool interior = Math.Abs(bestLag - centerLag) < rangeSamples;
        double refinedLag = interior
            ? TransferFunction.RefinePeakLag(correlation, bestLag, fftLength, sign)
            : bestLag;
        return new CorrelationDelayCandidate(
            refinedLag * 1000.0 / sampleRate,
            normalizer > 0 ? sign * best / normalizer : 0,
            !findMaximum);
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
        double priorSigmaMs)
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
        var seeds = new List<AlignmentCandidate>();
        foreach ((double[] accumulated, bool invert) in
            new[] { (normalDb, false), (invertedDb, true) })
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
        // everything far behind the winner.
        refined.Sort((a, b) => b.ScoreDb.CompareTo(a.ScoreDb));
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
    /// How far below the loudest in-curve level the channels' combined magnitude
    /// may fall before the summation loss stops being measured at that point.
    /// Where every channel is filtered that far down, the "loss" is the phase
    /// arithmetic of two noise floors — it swings to deep fake dips well outside
    /// any driver's band — so those points become NaN: the drawn curve breaks
    /// and the average/dip read-outs skip them.
    /// </summary>
    public const double SumLossLevelGateDb = 40;

    /// <summary>
    /// The per-point summation-loss curve (dB, &lt;= 0): the complex sum minus the
    /// phase-blind magnitude sum of the channel curves, over their shared index
    /// grid (truncated to the shortest). Points where the combined magnitude sits
    /// more than <see cref="SumLossLevelGateDb"/> below its in-curve peak read
    /// NaN (see there). This is the single definition the panel's drawn
    /// "Sum loss" curve, <see cref="AverageSumLossDb"/> and
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

        var magnitudeSums = new double[Math.Max(0, count)];
        double peakMagnitude = 0;
        for (int i = 0; i < count; i++)
        {
            double magnitudeSum = 0;
            foreach (IReadOnlyList<SignalPoint> curve in channelCurves)
            {
                magnitudeSum += DataHelper.DecibelsToAmplitude(curve[i].Y);
            }

            magnitudeSums[i] = magnitudeSum;
            if (double.IsFinite(magnitudeSum))
            {
                peakMagnitude = Math.Max(peakMagnitude, magnitudeSum);
            }
        }

        double gateFloor =
            peakMagnitude * DataHelper.DecibelsToAmplitude(-SumLossLevelGateDb);
        var points = new List<SignalPoint>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            points.Add(new SignalPoint(
                sumCurve[i].X,
                magnitudeSums[i] >= gateFloor
                    ? sumCurve[i].Y - DataHelper.AmplitudeToDecibels(magnitudeSums[i])
                    : double.NaN));
        }

        return points;
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
