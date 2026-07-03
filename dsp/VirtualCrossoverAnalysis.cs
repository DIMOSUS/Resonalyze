using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// The outcome of a phase-alignment search: the delay to add and whether the
/// variable channel sums best with its polarity flipped.
/// </summary>
public readonly record struct AlignmentResult(double DelayMs, bool InvertPolarity);

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

        var spectrum = new Complex[length];
        Array.Copy(impulseResponse, spectrum, impulseResponse.Length);
        Fourier.Forward(spectrum, FourierOptions.Matlab);

        PreparedChain preparedChain = PreparedChain.Create(chain, sampleRate);
        int half = length / 2;
        spectrum[0] *= preparedChain.Response(0.0, sampleRate);
        for (int i = 1; i < half; i++)
        {
            Complex response = preparedChain.Response(i * (double)sampleRate / length, sampleRate);
            spectrum[i] *= response;
            spectrum[length - i] *= Complex.Conjugate(response);
        }
        // The Nyquist bin has no conjugate partner; a real scale keeps a real
        // impulse real (the discarded imaginary part is a half-sample artifact).
        spectrum[half] *= preparedChain.Response(sampleRate / 2.0, sampleRate).Real;

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return spectrum;
    }

    private sealed class PreparedChain
    {
        private readonly double linearGain;
        private readonly double delayMs;
        private readonly BiquadCoefficients[] sections;

        private PreparedChain(
            double linearGain,
            double delayMs,
            BiquadCoefficients[] sections)
        {
            this.linearGain = linearGain;
            this.delayMs = delayMs;
            this.sections = sections;
        }

        public static PreparedChain Create(DspChannelChain chain, int sampleRate)
        {
            double linearGain = Math.Pow(10.0, chain.GainDb / 20.0) *
                (chain.InvertPolarity ? -1.0 : 1.0);
            var sections = new List<BiquadCoefficients>();

            if (chain.Crossover is { Kind: not CrossoverKind.Off } crossover)
            {
                AddCrossoverSections(sections, crossover, sampleRate);
            }

            if (chain.Peq is { } peq)
            {
                linearGain *= Math.Pow(10.0, peq.PreampDb / 20.0);
                foreach (PeqBand band in peq.Bands)
                {
                    if (band.GainDb == 0 || band.Q <= 0 || band.FrequencyHz <= 0)
                    {
                        continue;
                    }

                    sections.Add(PeakingBiquad.Compute(band, sampleRate));
                }
            }

            return new PreparedChain(linearGain, chain.DelayMs, sections.ToArray());
        }

        public Complex Response(double frequencyHz, double sampleRateHz)
        {
            double omega = Math.Tau * frequencyHz / sampleRateHz;
            Complex z1 = Complex.Exp(new Complex(0, -omega));
            Complex response = linearGain * Complex.Exp(
                new Complex(0, -Math.Tau * frequencyHz * delayMs / 1_000.0));
            foreach (BiquadCoefficients section in sections)
            {
                response *= BiquadResponse.Evaluate(section, z1);
            }

            return response;
        }

        private static void AddCrossoverSections(
            List<BiquadCoefficients> sections,
            CrossoverSpec spec,
            double sampleRate)
        {
            if (spec.Kind is CrossoverKind.LowPass or CrossoverKind.BandPass)
            {
                CrossoverEdge edge = spec.LowPassEdge
                    ?? throw new InvalidOperationException(
                        "The crossover kind requires a low-pass edge.");
                sections.AddRange(CrossoverFilter.BuildSections(
                    edge, highPass: false, sampleRate));
            }
            if (spec.Kind is CrossoverKind.HighPass or CrossoverKind.BandPass)
            {
                CrossoverEdge edge = spec.HighPassEdge
                    ?? throw new InvalidOperationException(
                        "The crossover kind requires a high-pass edge.");
                sections.AddRange(CrossoverFilter.BuildSections(
                    edge, highPass: true, sampleRate));
            }
        }
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
    /// Like <see cref="FindBestDelayMs"/>, but also tries the inverted polarity
    /// of the variable channel: the search maximizes |correlation|, and a
    /// negative winner means the channel sums best flipped. The returned invert
    /// flag is relative to the variable IR as passed in (XOR it onto the
    /// channel's current polarity switch).
    /// </summary>
    public static AlignmentResult FindBestAlignment(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        double minDelayMs,
        double maxDelayMs)
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
            return new AlignmentResult(0, false);
        }

        return SearchBestDelay(
            crossTerms, minDelayMs, maxDelayMs, maxFrequencyHz, allowInvert: true);
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

    // The per-bin cross spectrum conj(F)·V inside the frequency window, decimated
    // to a bounded bin count so the search stays fast for long IRs. The fixed
    // channels act as one combined source (superposition).
    private static List<(double OmegaMs, Complex Cross)> BuildCrossTerms(
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
        var crossTerms = new List<(double OmegaMs, Complex Cross)>();
        if (lastBin < firstBin)
        {
            return crossTerms;
        }

        int stride = Math.Max(1, (lastBin - firstBin + 1) / 4_096);
        for (int bin = firstBin; bin <= lastBin; bin += stride)
        {
            Complex cross = Complex.Conjugate(fixedSpectrum[bin]) * variableSpectrum[bin];
            if (cross != Complex.Zero)
            {
                // ω per millisecond of delay for this bin's frequency.
                double omegaMs = Math.Tau * (bin * (double)sampleRate / length) / 1_000.0;
                crossTerms.Add((omegaMs, cross));
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
        double peakMagnitude = 0.0;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            double magnitude = impulseResponse[i].Magnitude;
            if (magnitude > peakMagnitude)
            {
                peakMagnitude = magnitude;
                peakIndex = i;
            }
        }

        return peakIndex;
    }
}
