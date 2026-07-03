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
/// prior-penalized average-loss score. Near a steep crossover several such
/// candidates — the true alignment and its (flip + half-period shift)
/// impostors — can score within fractions of a dB of each other inside the
/// pair band, so the caller may need external evidence to pick between them.
/// <see cref="LossDb"/> is the raw in-band average without the prior penalty;
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
    // filter ringing tails stay linear instead of wrapping around. 8192 samples
    // cover ~170 ms at 48 kHz — far beyond any crossover or high-Q PEQ decay.
    private const int FilterTailPadding = 8192;

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
        int length = DspMath.NextPowerOfTwo(
            impulseResponse.Length + delaySamples + FilterTailPadding);
        PreparedDspResponse preparedChain = PreparedDspResponse.Create(chain, sampleRate);
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
        double high = Math.Clamp(highFrequencyHz, low * Math.Sqrt(2.0), 20_000);

        var samples = new double[impulseResponse.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = impulseResponse[i].Real;
        }

        // Gentle spectral fades and a moderate threshold: the zero-phase bandpass
        // rings symmetrically around the arrival, and steep edges plus a deep
        // threshold would let the detector fire on a pre-ringing lobe.
        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
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
        return result.FirstArrivalDelayMilliseconds;
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

        int length = DspMath.NextPowerOfTwo(Math.Max(
            variableImpulseResponse.Length,
            fixedImpulseResponses.Max(ir => ir.Length)));

        Complex[] variableSpectrum = ForwardSpectrum(variableImpulseResponse, length);
        var fixedSpectrum = new Complex[length];
        foreach (Complex[] ir in fixedImpulseResponses)
        {
            Complex[] spectrum = ForwardSpectrum(ir, length);
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

        double coarseStep = Math.Min(0.02, 250.0 / maxFrequencyHz / 4.0);
        double best = minDelayMs;
        double bestScore = double.NegativeInfinity;
        for (double delay = minDelayMs; delay <= maxDelayMs; delay += coarseStep)
        {
            double score = Score(Correlation(delay));
            if (score > bestScore)
            {
                bestScore = score;
                best = delay;
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
    private const double CandidateGapDb = 1.5;
    private const int MaxAlignmentCandidates = 4;

    // Coarse grid, then two refinement passes per surviving local optimum —
    // the same grid scheme as the correlation search, but scoring each delay
    // by the metric the tool actually reports: the log-frequency-weighted
    // average summation loss. Both polarities are evaluated at every delay
    // (they share the rotated spectrum), and the better one is kept alongside
    // the delay. Every local optimum of the coarse grid within the candidate
    // gap is refined and reported, best first.
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

        (double LossDb, bool Invert) Evaluate(double delayMs)
        {
            double normal = 0;
            double inverted = 0;
            foreach (AlignmentBin bin in bins)
            {
                Complex variable = bin.Variable * Complex.Exp(
                    new Complex(0, -bin.OmegaMs * delayMs));
                normal += bin.LogWeight * Math.Log10(Math.Max(
                    (bin.FixedSum + variable).Magnitude / bin.MagnitudeSum,
                    MinBinAmplitudeRatio));
                inverted += bin.LogWeight * Math.Log10(Math.Max(
                    (bin.FixedSum - variable).Magnitude / bin.MagnitudeSum,
                    MinBinAmplitudeRatio));
            }

            normal *= 20.0 / weightSum;
            inverted *= 20.0 / weightSum;
            return inverted > normal ? (inverted, true) : (normal, false);
        }

        AlignmentCandidate Scored(double delayMs)
        {
            (double lossDb, bool invert) = Evaluate(delayMs);
            double score = lossDb;
            if (priorDelayMs is { } prior && priorSigmaMs > 0)
            {
                double distance = (delayMs - prior) / priorSigmaMs;
                score -= PriorPenaltyDbAtSigma * distance * distance;
            }

            return new AlignmentCandidate(delayMs, invert, score);
        }

        double coarseStep = Math.Min(0.02, 250.0 / maxFrequencyHz / 4.0);
        var grid = new List<AlignmentCandidate>();
        for (double delay = minDelayMs; delay <= maxDelayMs; delay += coarseStep)
        {
            grid.Add(Scored(delay));
        }

        if (grid.Count == 0)
        {
            return [];
        }

        // Local optima of the coarse grid (window edges included): each is the
        // seed of one correlation lobe / polarity basin.
        var seeds = new List<AlignmentCandidate>();
        for (int i = 0; i < grid.Count; i++)
        {
            bool risesBefore = i == 0 || grid[i].ScoreDb >= grid[i - 1].ScoreDb;
            bool fallsAfter = i == grid.Count - 1 || grid[i].ScoreDb >= grid[i + 1].ScoreDb;
            if (risesBefore && fallsAfter)
            {
                seeds.Add(grid[i]);
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
                // score and polarity must belong to an in-window delay.
                double from = Math.Max(minDelayMs, best.DelayMs - step);
                double to = Math.Min(maxDelayMs, best.DelayMs + step);
                step /= 10.0;
                for (double delay = from; delay <= to; delay += step)
                {
                    AlignmentCandidate candidate = Scored(delay);
                    if (candidate.ScoreDb > best.ScoreDb)
                    {
                        best = candidate;
                    }
                }
            }

            refined.Add(best);
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
                Math.Abs(kept.DelayMs - candidate.DelayMs) > coarseStep))
            {
                results.Add(candidate);
            }
        }

        // Enrich the survivors with the diagnostics the score alone hides: the
        // raw in-band average (no prior) and the deepest smoothed notch.
        (double LossDb, double DipDb) DetailedLoss(double delayMs, bool invert)
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

        for (int i = 0; i < results.Count; i++)
        {
            (double lossDb, double dipDb) = DetailedLoss(
                results[i].DelayMs, results[i].InvertPolarity);
            results[i] = results[i] with { LossDb = lossDb, DipDb = dipDb };
        }

        return results;
    }

    private static Complex[] ForwardSpectrum(Complex[] impulseResponse, int length)
    {
        var spectrum = new Complex[length];
        Array.Copy(impulseResponse, spectrum, Math.Min(impulseResponse.Length, length));
        Fourier.Forward(spectrum, FourierOptions.Matlab);
        return spectrum;
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

        int count = sumCurve.Count;
        foreach (IReadOnlyList<SignalPoint> curve in channelCurves)
        {
            count = Math.Min(count, curve.Count);
        }

        double total = 0;
        int samples = 0;
        for (int i = 0; i < count; i++)
        {
            double frequency = sumCurve[i].X;
            if (frequency < minFrequencyHz || frequency > maxFrequencyHz)
            {
                continue;
            }

            double magnitudeSum = 0;
            foreach (IReadOnlyList<SignalPoint> curve in channelCurves)
            {
                magnitudeSum += DataHelper.DecibelsToAmplitude(curve[i].Y);
            }

            double loss = sumCurve[i].Y - DataHelper.AmplitudeToDecibels(magnitudeSum);
            if (double.IsFinite(loss))
            {
                total += loss;
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

        int count = sumCurve.Count;
        foreach (IReadOnlyList<SignalPoint> curve in channelCurves)
        {
            count = Math.Min(count, curve.Count);
        }

        double? minimum = null;
        for (int i = 0; i < count; i++)
        {
            double frequency = sumCurve[i].X;
            if (frequency < minFrequencyHz || frequency > maxFrequencyHz)
            {
                continue;
            }

            double magnitudeSum = 0;
            foreach (IReadOnlyList<SignalPoint> curve in channelCurves)
            {
                magnitudeSum += DataHelper.DecibelsToAmplitude(curve[i].Y);
            }

            double loss = sumCurve[i].Y - DataHelper.AmplitudeToDecibels(magnitudeSum);
            if (double.IsFinite(loss) && (minimum == null || loss < minimum))
            {
                minimum = loss;
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
