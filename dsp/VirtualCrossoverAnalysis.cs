using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public readonly record struct AlignmentResult(double DelayMs, bool InvertPolarity);

/// <summary>A local optimum of the penalized loss search; near a steep crossover the true alignment and a flip + half-period
/// impostor can tie. <see cref="LossDb"/> is the raw in-band average, <see cref="DipDb"/> the deepest 1/6-octave notch.</summary>
public sealed record AlignmentCandidate(
    double DelayMs,
    bool InvertPolarity,
    double ScoreDb,
    double LossDb = 0,
    double DipDb = 0);

/// <summary>Junction sum at current timing: loss and dip (dB, ≤ 0) and ripple of the summed magnitude (dB RMS, ≥ 0).</summary>
public sealed record JunctionSpectrumReading(double LossDb, double DipDb, double RippleDb);

/// <summary>One side of a junction for a read that times every side at once.</summary>
public sealed record JunctionAlignmentSide(
    Complex[] VariableImpulseResponse,
    Complex[] FixedImpulseResponse,
    int SampleRate,
    ValidSampleRange VariableValidRange = default,
    ValidSampleRange FixedValidRange = default);

/// <summary>One bin of a junction read: each side's gated level (arbitrary reference) and the read's 1/f weight.</summary>
public readonly record struct JunctionLevelBin(
    double FrequencyHz,
    double LogWeight,
    double FixedDb,
    double VariableDb);

/// <summary>Envelope crossings at 10/25/50 % of the first credible arrival's peak, on its rising front. Callers comparing
/// channels must gate on the spread of the DIFFERENCE and on <see cref="SnrDb"/>: noise alone gives stable-looking crossings.</summary>
public readonly record struct BroadbandOnsetEstimate(
    double EarlyMs,
    double OnsetMs,
    double LateMs,
    double SnrDb,
    bool IsValid);

/// <summary><see cref="EdgePinned"/>: found on the lag-window boundary, so position and magnitude are window artifacts.</summary>
public sealed record CorrelationDelayCandidate(
    double DelayMs,
    double Coefficient,
    bool InvertPolarity,
    bool EdgePinned = false);

/// <summary><see cref="DelayMs"/> is added to the second IR. Rivals: strongest same-sign extrema outside the main lobe;
/// opposite neighbours: nearest opposite-sign lobes (they bound a cycle-skip).
/// See docs/tech/virtual-dsp-analysis.md#band-limited-correlation.</summary>
public sealed record CorrelationAlignmentResult(
    double CenterFrequencyHz,
    double BandLowHz,
    double BandHighHz,
    double SearchRangeMs,
    CorrelationDelayCandidate PositivePeak,
    CorrelationDelayCandidate NegativeTrough,
    CorrelationDelayCandidate? PositiveRival = null,
    CorrelationDelayCandidate? NegativeRival = null,
    CorrelationDelayCandidate? PositiveOppositeNeighbor = null,
    CorrelationDelayCandidate? NegativeOppositeNeighbor = null)
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

public enum PolarityEstimate
{
    Unknown,
    Positive,
    Negative
}

/// <summary>Where measured content sits in a processed record (chain-delay prefix and FFT tail excluded); default = unknown.
/// See docs/tech/virtual-dsp-analysis.md#chain-application-and-the-valid-sample-range.</summary>
/// <param name="LeadSamples">Content the chain puts ahead of its main tap (a linear-phase FIR's pre-ring): windows opened
/// at a front open this much earlier. A length, not a position — crops and slides keep it.</param>
public readonly record struct ValidSampleRange(int StartSample, int EndSample, int LeadSamples = 0)
{
    public bool IsKnown => EndSample > StartSample;
}

/// <summary>Applies DSP chains to transfer IRs and sums them. All IRs share the loopback time reference, so a sample-wise
/// sum is what the microphone captures. See docs/tech/virtual-dsp-analysis.md.</summary>
public static class VirtualCrossoverAnalysis
{
    // Pad floor; the actual pad follows the chain's slowest pole (a low high-Q PEQ rings far past it and would wrap).
    private const int MinFilterTailPadding = 8192;
    private const int MaxFilterTailPadding = 262_144;
    private const double FilterTailDecayDb = 120.0;

    /// <summary>Multiplies the IR spectrum by the chain response. <paramref name="processorSampleRate"/> shapes the filters;
    /// <paramref name="sampleRate"/> is the record's.</summary>
    public static Complex[] ApplyChain(
        Complex[] impulseResponse,
        DspChannelChain chain,
        int sampleRate,
        int processorSampleRate) =>
        ApplyChain(impulseResponse, chain, sampleRate, processorSampleRate, out _);

    /// <summary>The <see cref="ValidSampleRange"/> ApplyChain reports, recomputed without re-running the chain.</summary>
    public static ValidSampleRange ChainValidRange(
        int inputLength,
        DspChannelChain chain,
        int sampleRate,
        int processorSampleRate,
        int outputLength)
    {
        ArgumentNullException.ThrowIfNull(chain);
        // Signed delay: a negative shift ends content earlier; the vacated tail is manufactured silence.
        double delaySamplesExact = chain.DelayMs / 1_000.0 * sampleRate;
        // FIR leading exact zeros shift content and N − 1 kernel samples extend it (convolution output is content), both
        // converted from processor to record samples.
        double firShiftSamples = 0;
        double firTailSamples = 0;
        double firLeadSamples = 0;
        if (chain.Fir is { } fir && processorSampleRate > 0)
        {
            double perProcessorSample = (double)sampleRate / processorSampleRate;
            firShiftSamples = fir.LeadingZeroCount * perProcessorSample;
            firTailSamples = (fir.Length - 1) * perProcessorSample;
            // Pre-ring: from the first non-zero tap to the largest (≈0 for a minimum-phase kernel).
            firLeadSamples = Math.Max(0, fir.PeakIndex - fir.LeadingZeroCount) * perProcessorSample;
        }

        int startSample = Math.Clamp(
            (int)Math.Floor(Math.Max(0.0, delaySamplesExact) + firShiftSamples),
            0,
            Math.Max(0, outputLength - 1));
        int endSample = Math.Min(
            outputLength,
            inputLength + (int)Math.Ceiling(delaySamplesExact + firTailSamples));
        return endSample > startSample
            ? new ValidSampleRange(
                startSample, endSample, (int)Math.Ceiling(firLeadSamples))
            : default;
    }

    /// <summary>ApplyChain that also reports the measured-content range; envelope/SNR analyses must read inside it.</summary>
    public static Complex[] ApplyChain(
        Complex[] impulseResponse,
        DspChannelChain chain,
        int sampleRate,
        int processorSampleRate,
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
        if (processorSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processorSampleRate));
        }

        int delaySamples = (int)Math.Ceiling(
            Math.Max(0.0, chain.DelayMs) / 1_000.0 * sampleRate);
        PreparedDspResponse preparedChain =
            PreparedDspResponse.Create(chain, processorSampleRate);
        int tailPadding = preparedChain.RequiredTailSamples(
            FilterTailDecayDb, MinFilterTailPadding, MaxFilterTailPadding, sampleRate);
        int length = DspMath.NextPowerOfTwo(
            impulseResponse.Length + delaySamples + tailPadding);
        validRange = ChainValidRange(
            impulseResponse.Length, chain, sampleRate, processorSampleRate, length);
        if (preparedChain.CanScaleInTimeDomain(sampleRate))
        {
            return preparedChain.ApplyTimeDomainScale(impulseResponse, length);
        }

        var spectrum = new Complex[length];
        Array.Copy(impulseResponse, spectrum, impulseResponse.Length);
        Fourier.Forward(spectrum, FourierOptions.Matlab);

        preparedChain.ApplyToSpectrum(spectrum, sampleRate);

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return spectrum;
    }

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

    /// <summary>Running sum of real samples, <paramref name="count"/> from <paramref name="start"/>, always integrated from the
    /// record start so the shown stretch never changes the step's shape.</summary>
    public static double[] StepResponse(Complex[] impulseResponse, int start, int count)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        double running = 0.0;
        for (int index = 0; index < start && index < impulseResponse.Length; index++)
        {
            running += impulseResponse[index].Real;
        }

        var step = new double[count];
        for (int i = 0; i < count; i++)
        {
            int index = start + i;
            if (index < impulseResponse.Length)
            {
                running += impulseResponse[index].Real;
            }

            step[i] = running;
        }

        return step;
    }

    /// <summary>Delay (ms) maximizing the in-window sum energy with the fixed channels (exact fractional-delay cross-correlation).
    /// Negative means advance the channel.</summary>
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

    /// <summary>Delay and polarity minimizing the log-weighted average sum loss in the window (raw correlation lets the
    /// flip + half-period impostor tie). Invert is relative to the IR as passed.
    /// See docs/tech/virtual-dsp-analysis.md#alignment-search-objective.</summary>
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

    /// <summary>Every near-optimal local optimum of <see cref="FindBestAlignment"/>, best first, for the caller to disambiguate.</summary>
    /// <param name="gateAnchorSample">Null (engine default): each response windowed at its own front. Non-null: one shared window.</param>
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
        bool? forcedPolarity = null,
        bool levelMatch = false,
        int? gateAnchorSample = null,
        ValidSampleRange variableValidRange = default,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges = null) =>
        FindAlignmentCandidates(
            variableImpulseResponse, fixedImpulseResponses, sampleRate,
            minFrequencyHz, maxFrequencyHz, minDelayMs, maxDelayMs,
            priorDelayMs, priorSigmaMs, forcedPolarity, levelMatch, out _,
            gateAnchorSample, variableValidRange, fixedValidRanges);

    /// <summary>Also reports every refined optimum uncapped: rival margins must not read the capped selection list.</summary>
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
        bool levelMatch,
        out IReadOnlyList<AlignmentCandidate> allOptima,
        int? gateAnchorSample = null,
        ValidSampleRange variableValidRange = default,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges = null)
    {
        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs,
            maxDelayMs,
            levelMatch,
            gateAnchorSample,
            variableValidRange,
            fixedValidRanges);
        if (bins.Count == 0 || !HoldsDelayEvidence(bins))
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

    private const double EvidenceNoiseFloorGateDb = 60;

    private const double MinEvidenceOctaves = 1.0 / 6.0;

    private const int MinEvidenceBins = 3;

    // Delay is observable only where both sides radiate over a usable width; otherwise the prior alone makes a candidate.
    // See docs/tech/virtual-dsp-analysis.md#delay-evidence-gate.
    private static bool HoldsDelayEvidence(List<AlignmentBin> bins)
    {
        // Raw magnitudes, before the level match (which would lift a -60 dB tail onto the gate).
        double signalPeak = 0;
        foreach (AlignmentBin bin in bins)
        {
            signalPeak = Math.Max(
                signalPeak,
                bin.FixedSum.Magnitude + bin.RawVariableMagnitude);
        }
        double levelFloor =
            signalPeak * Math.Pow(10.0, -EvidenceNoiseFloorGateDb / 20.0);
        double balanceRatio = Math.Pow(10.0, -OverlapReliabilityGateDb / 20.0);
        bool Evidence(AlignmentBin bin)
        {
            double weaker = Math.Min(
                bin.FixedSum.Magnitude, bin.RawVariableMagnitude);
            double stronger = Math.Max(
                bin.FixedSum.Magnitude, bin.RawVariableMagnitude);
            return bin.FixedSum.Magnitude + bin.RawVariableMagnitude >= levelFloor &&
                weaker >= stronger * balanceRatio;
        }

        // Bins are decimated and zero-dropped: adjacency is the smallest FFT-index step, so gaps are not credited.
        int minStep = int.MaxValue;
        for (int i = 0; i + 1 < bins.Count; i++)
        {
            minStep = Math.Min(minStep, bins[i + 1].FftBin - bins[i].FftBin);
        }

        double runOctaves = 0;
        int runBins = 1;
        for (int i = 0; i + 1 < bins.Count; i++)
        {
            bool adjacent = bins[i + 1].FftBin - bins[i].FftBin == minStep;
            if (adjacent && Evidence(bins[i]) && Evidence(bins[i + 1]))
            {
                // LogWeight is 1/f.
                runOctaves += Math.Log2(bins[i].LogWeight / bins[i + 1].LogWeight);
                runBins++;
                if (runOctaves >= MinEvidenceOctaves && runBins >= MinEvidenceBins)
                {
                    return true;
                }
            }
            else
            {
                runOctaves = 0;
                runBins = 1;
            }
        }
        return false;
    }

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

    /// <summary>Gate start for one response: its band-limited front, falling back to the peak and never later than the peak.
    /// See docs/tech/virtual-dsp-analysis.md#window-anchors.</summary>
    public static int FindGateAnchor(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        double bandLowHz,
        double bandHighHz,
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

        int peak = Math.Clamp(peakIndex, 0, impulseResponse.Length - 1);
        TimeAlignmentAnalysisResult arrival = AnalyzeBandLimitedArrival(
            impulseResponse, sampleRate, bandLowHz, bandHighHz, validRange);
        int anchor = peak;
        if (arrival.IsValid &&
            arrival.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            // Floored: half a sample early is covered by the fade; half late would put the plateau inside the front.
            int front = (int)Math.Floor(
                arrival.FirstArrivalDelayMilliseconds / 1_000.0 * sampleRate);
            anchor = Math.Min(front, peak);
        }

        // A linear-phase FIR's pre-ring reads as no front (its envelope rises symmetrically into the peak), yet a cut
        // through it breaks the pair's complementary sum. See docs/tech/virtual-dsp-analysis.md#window-anchors.
        if (validRange.LeadSamples > 0)
        {
            int floor = validRange.IsKnown ? validRange.StartSample : 0;
            anchor = Math.Min(anchor, Math.Max(floor, anchor - validRange.LeadSamples));
        }

        return Math.Clamp(anchor, 0, impulseResponse.Length - 1);
    }

    /// <summary>Direct sound cut to [front − T/2, front + 2.5T] with half-period fades (T = crossover period); past two
    /// periods cabin reflections take over the correlation. See docs/tech/virtual-dsp-analysis.md#direct-sound-cuts.</summary>
    public static Complex[] CutDirectSound(
        Complex[] impulseResponse,
        int sampleRate,
        double bandLowHz,
        double bandHighHz,
        double crossoverHz,
        ValidSampleRange validRange = default)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "The impulse response is empty.", nameof(impulseResponse));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(crossoverHz > 0))
        {
            throw new ArgumentException("The crossover is invalid.");
        }

        int front = FindGateAnchor(
            impulseResponse,
            FindPeakIndex(impulseResponse),
            sampleRate,
            bandLowHz,
            bandHighHz,
            validRange);
        var cut = new Complex[impulseResponse.Length];
        WriteDirectSoundWindow(
            impulseResponse, front, validRange.LeadSamples, sampleRate, crossoverHz, cut,
            destinationOffset: 0);
        return cut;
    }

    /// <summary>One span definition for CutDirectSound and the ladder's per-band cuts. <paramref name="leadSamples"/>
    /// lengthens the plateau by the pre-ring the front was moved over, so the cut still holds two periods past it.</summary>
    private static (int Start, int End, int Fade, int Plateau)
        DirectSoundWindowBounds(
            int front, int leadSamples, int sampleRate, double crossoverHz, int length)
    {
        double periodSamples = sampleRate / crossoverHz;
        int fade = Math.Max(8, (int)Math.Round(0.5 * periodSamples));
        int plateau = (int)Math.Round(2.0 * periodSamples) + Math.Max(0, leadSamples);
        return (
            Math.Max(0, front - fade),
            Math.Min(length, front + plateau + fade),
            fade,
            plateau);
    }

    /// <summary>Both cuts trimmed to their span union by one shared offset (lags unchanged); buffers sized to
    /// <paramref name="searchRangeMs"/>.</summary>
    public static (Complex[] Lower, Complex[] Upper) CutDirectSoundPair(
        Complex[] lowerImpulseResponse,
        Complex[] upperImpulseResponse,
        int sampleRate,
        double bandLowHz,
        double bandHighHz,
        double crossoverHz,
        double searchRangeMs,
        ValidSampleRange lowerValidRange = default,
        ValidSampleRange upperValidRange = default)
    {
        ArgumentNullException.ThrowIfNull(lowerImpulseResponse);
        ArgumentNullException.ThrowIfNull(upperImpulseResponse);
        if (lowerImpulseResponse.Length == 0 || upperImpulseResponse.Length == 0)
        {
            throw new ArgumentException("The impulse response is empty.");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(crossoverHz > 0) || !(searchRangeMs > 0))
        {
            throw new ArgumentException("The cut settings are invalid.");
        }

        int lowerFront = FindGateAnchor(
            lowerImpulseResponse, FindPeakIndex(lowerImpulseResponse),
            sampleRate, bandLowHz, bandHighHz, lowerValidRange);
        int upperFront = FindGateAnchor(
            upperImpulseResponse, FindPeakIndex(upperImpulseResponse),
            sampleRate, bandLowHz, bandHighHz, upperValidRange);
        return TrimmedDirectSoundPair(
            lowerImpulseResponse, upperImpulseResponse, sampleRate,
            crossoverHz, lowerFront, upperFront,
            lowerValidRange.LeadSamples, upperValidRange.LeadSamples, searchRangeMs);
    }

    // Buffers must cover content + searched lag, or the circular correlation aliases lobes into far lags.
    private static (Complex[] Lower, Complex[] Upper) TrimmedDirectSoundPair(
        Complex[] lowerImpulseResponse,
        Complex[] upperImpulseResponse,
        int sampleRate,
        double crossoverHz,
        int lowerFront,
        int upperFront,
        int lowerLeadSamples,
        int upperLeadSamples,
        double searchRangeMs)
    {
        (int lowerStart, int lowerEnd, _, _) = DirectSoundWindowBounds(
            lowerFront, lowerLeadSamples, sampleRate, crossoverHz, lowerImpulseResponse.Length);
        (int upperStart, int upperEnd, _, _) = DirectSoundWindowBounds(
            upperFront, upperLeadSamples, sampleRate, crossoverHz, upperImpulseResponse.Length);
        int spanStart = Math.Min(lowerStart, upperStart);
        int spanEnd = Math.Max(lowerEnd, upperEnd);
        int rangeSamples =
            (int)Math.Ceiling(searchRangeMs / 1_000.0 * sampleRate) + 2;
        int length = Math.Max(Math.Max(spanEnd - spanStart, rangeSamples), 1);
        var cutLower = new Complex[length];
        var cutUpper = new Complex[length];
        if (spanEnd > spanStart)
        {
            WriteDirectSoundWindow(
                lowerImpulseResponse, lowerFront, lowerLeadSamples, sampleRate, crossoverHz,
                cutLower, spanStart);
            WriteDirectSoundWindow(
                upperImpulseResponse, upperFront, upperLeadSamples, sampleRate, crossoverHz,
                cutUpper, spanStart);
        }

        return (cutLower, cutUpper);
    }

    private static void WriteDirectSoundWindow(
        Complex[] impulseResponse,
        int front,
        int leadSamples,
        int sampleRate,
        double crossoverHz,
        Complex[] destination,
        int destinationOffset)
    {
        (int start, int end, int fade, int plateau) = DirectSoundWindowBounds(
            front, leadSamples, sampleRate, crossoverHz, impulseResponse.Length);
        for (int i = start; i < end; i++)
        {
            double weight =
                i < front
                    ? 0.5 - 0.5 * Math.Cos(Math.PI * (i - start) / (double)fade)
                    : i >= front + plateau
                        ? 0.5 + 0.5 * Math.Cos(
                            Math.PI * (i - front - plateau) / (double)fade)
                        : 1.0;
            destination[i - destinationOffset] = impulseResponse[i] * weight;
        }
    }

    /// <summary>Narrowest band (a third of an octave) an arrival analysis accepts; narrower is refused, not widened.</summary>
    public static readonly double MinimumArrivalBandRatio = Math.Pow(2.0, 1.0 / 3.0);

    /// <summary>Band-limited arrival with validity and SNR; callers hinging on one arrival pair must gate on them.</summary>
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

        // Full 25 dB depth: a shallower search latches a soft direct rise onto a strong modal build-up.
        // See docs/tech/virtual-dsp-analysis.md#band-limited-arrival-and-broadband-onset.
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
            // Arrivals in full-record coordinates; envelope indices stay window-local.
            double startMs = startSample * 1_000.0 / sampleRate;
            result = result with
            {
                FirstArrivalPeakSample =
                    result.FirstArrivalPeakSample + startSample,
                FirstArrivalDelayMilliseconds =
                    result.FirstArrivalDelayMilliseconds + startMs,
                StrongestPeakSample = result.StrongestPeakSample + startSample,
                StrongestDelayMilliseconds =
                    result.StrongestDelayMilliseconds + startMs,
                EnergyOnsetSample = result.EnergyOnsetSample + startSample,
                EnergyOnsetDelayMilliseconds =
                    result.EnergyOnsetDelayMilliseconds + startMs
            };
        }
        return result;
    }

    /// <summary>Broadband onset: envelope crossings at 10/25/50 % of the first credible arrival's peak (not the global maximum).
    /// Free of the rise-time bias band envelope peaks carry between junction members.
    /// See docs/tech/virtual-dsp-analysis.md#band-limited-arrival-and-broadband-onset.</summary>
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
        double startMs = startSample * 1_000.0 / sampleRate;
        return new BroadbandOnsetEstimate(
            early + startMs, onset + startMs, late + startMs, snrDb, IsValid: true);
    }

    // −140 dB: below any real noise floor, above numerical residue.
    private const double SyntheticTailFloorRatio = 1e-7;

    // A measured record is dense up to its padding; a sparse (synthetic) record's silence is genuine.
    private const double PaddedContentMinimumDensity = 0.5;

    // Quantile noise floors collapse on manufactured silence: prefer the caller's valid range, else a tail-only heuristic.
    // See docs/tech/virtual-dsp-analysis.md#chain-application-and-the-valid-sample-range.
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

    // Last crossing before the peak, linearly refined; clamped at 0.
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

    /// <summary>Band-limited normalized cross-correlation around the crossover; troughs = the same delay with the second
    /// channel inverted. <paramref name="phaseTransform"/> whitens it (GCC-PHAT).</summary>
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
        // Centred on the arrival estimate: low junctions differ by several ms.
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

        CorrelationDelayCandidate? positiveNeighbor = FindNearestOppositeExtremum(
            correlation, centerLag, rangeSamples, positiveLag, normalizer,
            sampleRate, mainIsMaximum: true);
        CorrelationDelayCandidate? negativeNeighbor = FindNearestOppositeExtremum(
            correlation, centerLag, rangeSamples, negativeLag, normalizer,
            sampleRate, mainIsMaximum: false);

        return new CorrelationAlignmentResult(
            centerFrequencyHz,
            lowHz,
            highHz,
            searchRangeMs,
            positive,
            negative,
            positiveRival,
            negativeRival,
            positiveNeighbor,
            negativeNeighbor);
    }

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

        // Band-pass correlation = inverse FFT of the cross-spectrum weighted by |band|²; len1+len2 padding keeps read lags wrap-free.
        int fftLength = DspMath.NextPowerOfTwo(
            firstImpulseResponse.Length + secondImpulseResponse.Length);
        Complex[] firstSpectrum = ForwardSpectrum(firstImpulseResponse, fftLength);
        Complex[] secondSpectrum = ForwardSpectrum(secondImpulseResponse, fftLength);

        var crossSpectrum = new Complex[fftLength];
        double firstEnergy = 0;
        double secondEnergy = 0;
        double weightSum = 0;
        for (int k = 0; k < fftLength; k++)
        {
            double frequency = (double)k / fftLength * sampleRate;
            if (frequency > nyquist)
            {
                // Bins above Nyquist are the conjugate mirror.
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
                    // Only contributing bins count, so a perfect alignment reaches 1.
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

        // Normalizer: raw sqrt(Ea·Eb)/N, PHAT (Σ W²)/N; both land in [-1, 1].
        double normalizer = phaseTransform
            ? weightSum / fftLength
            : Math.Sqrt(firstEnergy * secondEnergy) / fftLength;
        return (correlation, normalizer, lowHz, highHz);
    }

    /// <summary>The correlation of <see cref="FindBandLimitedCorrelationDelay"/> as a curve: X = lag (ms) to add to the second
    /// response, Y in [-1, 1].</summary>
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

    /// <summary>One ladder band: <see cref="LagMs"/> to add to the upper channel; <see cref="PeakR"/> attainable and
    /// <see cref="CurrentR"/> lag-0 coherence. No polarity.</summary>
    public sealed record ArrivalCoherencePoint(
        double FrequencyHz,
        double LagMs,
        double PeakR,
        double CurrentR,
        double HalfPeriodMs);

    // No polarity: a 2/3-octave probe's packet is 4.3x the lobe spacing at every frequency.
    // See docs/tech/virtual-dsp-analysis.md#arrival-coherence-ladder.

    public const double ArrivalCoherenceStepOctaves = 1.0 / 6;

    public const double ArrivalCoherenceBandOctaves = 2.0 / 3;

    /// <summary>Bands where the weaker direct cut sits more than this below the stronger are dropped (PHAT reads filtered remnants).</summary>
    public const double ArrivalCoherenceLevelGateDb = 25;

    /// <summary>Arrival-coherence ladder: per 1/6-octave probe, the envelope maximum of GCC-PHAT on direct cuts scaled to the probe.
    /// A diagnostic: at low junctions cabin modes rule, so its lags are not move recommendations.
    /// See docs/tech/virtual-dsp-analysis.md#arrival-coherence-ladder.</summary>
    public static List<ArrivalCoherencePoint> ArrivalCoherenceLadder(
        Complex[] lowerImpulseResponse,
        Complex[] upperImpulseResponse,
        int sampleRate,
        double bandLowHz,
        double bandHighHz,
        double crossoverHz,
        ValidSampleRange lowerValidRange = default,
        ValidSampleRange upperValidRange = default)
    {
        ArgumentNullException.ThrowIfNull(lowerImpulseResponse);
        ArgumentNullException.ThrowIfNull(upperImpulseResponse);
        if (lowerImpulseResponse.Length == 0 || upperImpulseResponse.Length == 0)
        {
            throw new ArgumentException("Impulse responses are required.");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(crossoverHz > 0) ||
            !(bandLowHz > 0) || !(bandHighHz > bandLowHz))
        {
            throw new ArgumentException("The junction band is invalid.");
        }

        // Fronts are band-independent: read once per channel.
        int lowerFront = FindGateAnchor(
            lowerImpulseResponse, FindPeakIndex(lowerImpulseResponse),
            sampleRate, bandLowHz, bandHighHz, lowerValidRange);
        int upperFront = FindGateAnchor(
            upperImpulseResponse, FindPeakIndex(upperImpulseResponse),
            sampleRate, bandLowHz, bandHighHz, upperValidRange);

        var frequencies = new List<double>();
        double step = Math.Pow(2.0, ArrivalCoherenceStepOctaves);
        for (double frequency = bandLowHz;
            frequency <= bandHighHz * 1.0001;
            frequency *= step)
        {
            frequencies.Add(frequency);
        }

        return frequencies
            .AsParallel()
            .AsOrdered()
            .Select(frequency => ProbeArrivalCoherenceBand(
                lowerImpulseResponse, upperImpulseResponse, sampleRate,
                frequency, lowerFront, upperFront,
                lowerValidRange.LeadSamples, upperValidRange.LeadSamples))
            .Where(point => point != null)
            .Select(point => point!)
            .ToList();
    }

    // Cuts trimmed to their span union with one offset: lags unchanged, FFTs a hundredfold smaller.
    private static ArrivalCoherencePoint? ProbeArrivalCoherenceBand(
        Complex[] lowerImpulseResponse,
        Complex[] upperImpulseResponse,
        int sampleRate,
        double frequency,
        int lowerFront,
        int upperFront,
        int lowerLeadSamples,
        int upperLeadSamples)
    {
        double displayMs = Math.Max(3.0, 1.5 * 1000.0 / frequency);
        (Complex[] cutLower, Complex[] cutUpper) = TrimmedDirectSoundPair(
            lowerImpulseResponse, upperImpulseResponse, sampleRate, frequency,
            lowerFront, upperFront, lowerLeadSamples, upperLeadSamples,
            searchRangeMs: 2.0 * displayMs);
        if (!ArrivalCoherenceBandBalanced(
            cutLower, cutUpper, sampleRate, frequency))
        {
            return null;
        }

        List<SignalPoint> curve = BandLimitedCorrelationCurve(
            cutLower, cutUpper, sampleRate, frequency,
            ArrivalCoherenceBandOctaves, 2.0 * displayMs,
            centerLagMs: 0, phaseTransform: true);
        if (curve.Count < 3)
        {
            return null;
        }

        double[] envelope = SignalEnvelope.Envelope(
            curve.Select(point => point.Y).ToList());
        int best = -1;
        double bestValue = 0;
        int zeroIndex = 0;
        double zeroDistance = double.MaxValue;
        for (int i = 1; i < curve.Count - 1; i++)
        {
            double lag = curve[i].X;
            if (Math.Abs(lag) < zeroDistance)
            {
                zeroDistance = Math.Abs(lag);
                zeroIndex = i;
            }

            if (Math.Abs(lag) <= displayMs && envelope[i] > bestValue)
            {
                bestValue = envelope[i];
                best = i;
            }
        }

        if (best < 1 || bestValue <= 0)
        {
            return null;
        }

        double stepMs = curve[1].X - curve[0].X;
        double lagMs = curve[best].X + stepMs *
            SignalEnvelope.FindFractionalPeakOffset(
                envelope[best - 1], envelope[best], envelope[best + 1]);
        return new ArrivalCoherencePoint(
            frequency,
            lagMs,
            Math.Min(1.0, bestValue),
            Math.Min(1.0, envelope[zeroIndex]),
            500.0 / frequency);
    }

    private static bool ArrivalCoherenceBandBalanced(
        Complex[] firstCut,
        Complex[] secondCut,
        int sampleRate,
        double centerFrequencyHz)
    {
        double nyquist = sampleRate / 2.0;
        double halfOctaves = ArrivalCoherenceBandOctaves / 2.0;
        double lowHz = Math.Max(
            20.0, centerFrequencyHz / Math.Pow(2.0, halfOctaves));
        double highHz = Math.Min(
            nyquist * 0.95, centerFrequencyHz * Math.Pow(2.0, halfOctaves));
        if (highHz <= lowHz)
        {
            return false;
        }

        int fftLength = DspMath.NextPowerOfTwo(
            Math.Max(firstCut.Length, secondCut.Length));
        double firstEnergy = BandWeightedEnergy(
            ForwardSpectrum(firstCut, fftLength), sampleRate, lowHz, highHz);
        double secondEnergy = BandWeightedEnergy(
            ForwardSpectrum(secondCut, fftLength), sampleRate, lowHz, highHz);
        if (firstEnergy <= 0 || secondEnergy <= 0)
        {
            return false;
        }

        double balanceDb = 10.0 * Math.Log10(
            Math.Min(firstEnergy, secondEnergy) /
            Math.Max(firstEnergy, secondEnergy));
        return balanceDb >= -ArrivalCoherenceLevelGateDb;
    }

    // Same weighting walk as ComputeBandLimitedCorrelation.
    private static double BandWeightedEnergy(
        Complex[] spectrum, int sampleRate, double lowHz, double highHz)
    {
        double nyquist = sampleRate / 2.0;
        double energy = 0;
        for (int k = 0; k < spectrum.Length; k++)
        {
            double frequency = (double)k / spectrum.Length * sampleRate;
            if (frequency > nyquist)
            {
                frequency = sampleRate - frequency;
            }

            double weight = BandWeight(frequency, lowHz, highHz);
            if (weight <= 0)
            {
                continue;
            }

            Complex value = spectrum[k];
            energy += weight * weight *
                (value.Real * value.Real + value.Imaginary * value.Imaginary);
        }

        return energy;
    }

    /// <summary>How many coherent bands (PeakR at least <paramref name="minPeakR"/>) put the upper channel within a quarter
    /// period of the candidate. A count, not a mean miss.</summary>
    internal static int CountLadderAgreement(
        IReadOnlyList<ArrivalCoherencePoint> ladder,
        double delayMs,
        double quarterPeriodMs,
        double minPeakR)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quarterPeriodMs);
        int agreeing = 0;
        foreach (ArrivalCoherencePoint point in ladder)
        {
            if (point.PeakR >= minPeakR &&
                Math.Abs(point.LagMs - delayMs) <= quarterPeriodMs)
            {
                agreeing++;
            }
        }

        return agreeing;
    }
    public sealed record JunctionSweepPoint(
        double DelayMs,
        double LossDb,
        double DipDb);

    /// <summary>Prior-free junction loss versus extra delay on the variable channel. Each channel is windowed once and probes
    /// rotate the cut, so with the search's settings this IS the searched surface.
    /// See docs/tech/virtual-dsp-analysis.md#junction-loss-sweep-and-sumlossevaluator.</summary>
    public static List<JunctionSweepPoint> JunctionLossSweep(
        Complex[] variableImpulseResponse,
        Complex[] fixedImpulseResponse,
        int sampleRate,
        double bandLowHz,
        double bandHighHz,
        double startDelayMs,
        double endDelayMs,
        double stepMs,
        bool invertVariable,
        int? gateAnchorSample = null,
        bool levelMatch = false,
        ValidSampleRange variableValidRange = default,
        ValidSampleRange fixedValidRange = default)
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

        // The anchor passes through untouched: null means per-response fronts, exactly as the Auto search reads.
        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            new List<Complex[]> { fixedImpulseResponse },
            sampleRate,
            bandLowHz,
            bandHighHz,
            startDelayMs,
            endDelayMs,
            levelMatch,
            gateAnchorSample,
            variableValidRange,
            fixedValidRanges: new[] { fixedValidRange });
        if (bins.Count == 0)
        {
            return [];
        }

        double weightSum = 0;
        foreach (AlignmentBin bin in bins)
        {
            weightSum += bin.LogWeight;
        }

        return SweepBins(
            bins, weightSum,
            SweepDelays(startDelayMs, endDelayMs, stepMs), invertVariable);
    }

    /// <summary>Both polarities from one set of bins (inversion is only a sign in the probe).</summary>
    public static (List<JunctionSweepPoint> Normal, List<JunctionSweepPoint> Inverted)
        JunctionLossSweepBothPolarities(
            Complex[] variableImpulseResponse,
            Complex[] fixedImpulseResponse,
            int sampleRate,
            double bandLowHz,
            double bandHighHz,
            double startDelayMs,
            double endDelayMs,
            double stepMs,
            int? gateAnchorSample = null,
            bool levelMatch = false,
            ValidSampleRange variableValidRange = default,
            ValidSampleRange fixedValidRange = default)
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

        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            new List<Complex[]> { fixedImpulseResponse },
            sampleRate,
            bandLowHz,
            bandHighHz,
            startDelayMs,
            endDelayMs,
            levelMatch,
            gateAnchorSample,
            variableValidRange,
            fixedValidRanges: new[] { fixedValidRange });
        if (bins.Count == 0)
        {
            return ([], []);
        }

        double weightSum = 0;
        foreach (AlignmentBin bin in bins)
        {
            weightSum += bin.LogWeight;
        }

        List<double> delays = SweepDelays(startDelayMs, endDelayMs, stepMs);
        return (
            SweepBins(bins, weightSum, delays, invert: false),
            SweepBins(bins, weightSum, delays, invert: true));
    }

    private static List<double> SweepDelays(
        double startDelayMs, double endDelayMs, double stepMs)
    {
        var delays = new List<double>();
        for (double delayMs = startDelayMs;
            delayMs <= endDelayMs + 1e-9;
            delayMs += stepMs)
        {
            delays.Add(delayMs);
        }

        return delays;
    }

    // Probes read immutable bins, so they run in parallel.
    private static List<JunctionSweepPoint> SweepBins(
        List<AlignmentBin> bins,
        double weightSum,
        List<double> delays,
        bool invert)
    {
        var points = new JunctionSweepPoint[delays.Count];
        Parallel.For(0, delays.Count, i =>
        {
            (double lossDb, double dipDb) = DetailedLoss(
                bins, weightSum, delays[i], invert);
            points[i] = new JunctionSweepPoint(delays[i], lossDb, dipDb);
        });
        return [.. points];
    }

    // Nearest opposite-sign lobe beside the main extremum (adjacency, not strength): it bounds a cycle-skip.
    private static CorrelationDelayCandidate? FindNearestOppositeExtremum(
        double[] correlation,
        int centerLag,
        int rangeSamples,
        int mainLag,
        double normalizer,
        int sampleRate,
        bool mainIsMaximum)
    {
        int fftLength = correlation.Length;
        // Positive where the OPPOSITE polarity's lobes are.
        double sign = mainIsMaximum ? -1.0 : 1.0;
        double Value(int lag) =>
            sign * correlation[TransferFunction.WrapIndex(lag, fftLength)];

        int windowLow = centerLag - rangeSamples;
        int windowHigh = centerLag + rangeSamples;
        // Extremum of the first contiguous opposite-sign region, not the first local extremum (a shoulder would shorten the spacing).
        int? LobeCrestOnSide(int step)
        {
            int lag = mainLag + step;
            while (lag > windowLow && lag < windowHigh && Value(lag) <= 0)
            {
                lag += step;
            }
            if (lag <= windowLow || lag >= windowHigh)
            {
                return null;
            }

            int crest = lag;
            while (lag > windowLow && lag < windowHigh && Value(lag) > 0)
            {
                if (Value(lag) > Value(crest))
                {
                    crest = lag;
                }
                lag += step;
            }

            return crest;
        }

        int? left = LobeCrestOnSide(-1);
        int? right = LobeCrestOnSide(1);
        int? nearest = (left, right) switch
        {
            (null, null) => null,
            (null, { } r) => r,
            ({ } l, null) => l,
            ({ } l, { } r) =>
                Math.Abs(l - mainLag) <= Math.Abs(r - mainLag) ? l : r
        };
        if (nearest is not { } bestLag)
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
            normalizer > 0 ? sign * Value(bestLag) / normalizer : 0,
            InvertPolarity: mainIsMaximum,
            edgePinned);
    }

    // Strongest same-sign extremum outside the main lobe's contiguous region; a boundary lag counts only past an opposite-sign gap.
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

    // Raised cosine over log frequency: no brickwall ringing in the correlation.
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

    // A truncated lobe's argmax lands on the boundary or one sample inside.
    private const int CorrelationEdgeGuardSamples = 2;

    // Sinc interpolation (a parabola mislocates sinc peaks); an edge-pinned extremum keeps its integer lag.
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
        int edgeGuard = Math.Min(CorrelationEdgeGuardSamples, rangeSamples - 1);
        bool edgePinned = distance >= rangeSamples - edgeGuard;
        double refinedLag = edgePinned
            ? bestLag
            : TransferFunction.RefinePeakLag(correlation, bestLag, fftLength, sign);
        return new CorrelationDelayCandidate(
            refinedLag * 1000.0 / sampleRate,
            normalizer > 0 ? sign * best / normalizer : 0,
            !findMaximum,
            edgePinned);
    }

    // LogWeight = 1/f makes the linear-bin average log-frequency; the magnitude sum is the loss denominator.
    private readonly record struct AlignmentBin(
        double OmegaMs,
        Complex FixedSum,
        Complex Variable,
        double LogWeight,
        double MagnitudeSum,
        int FftBin,
        // Before the level match: the evidence gate judges the raw balance.
        double RawVariableMagnitude);

    // Junction windows are band-sized in time (below); 4096/256 samples at 48 kHz is the reference.
    // See docs/tech/virtual-dsp-analysis.md#junction-direct-sound-gate (incl. the declined chain-free anchor).
    private const int AlignmentGateReferenceRate = 48_000;
    private const int AlignmentGateReferenceSamples = 4096;
    private const int AlignmentGateReferenceFadeSamples = 256;

    /// <summary>Zero-padding factor over the gate: buys spectrum sampling, not resolution.
    /// See docs/tech/virtual-dsp-analysis.md#junction-window-length-and-fft-sampling.</summary>
    private const int AlignmentFftInterpolationFactor = 4;

    /// <summary>Window length in low-edge periods (~8.66): the shortest whose kernel fits inside the 1/6-octave dip average.</summary>
    private static readonly double AlignmentGateBandLowEdgeCycles =
        1.0 / (Math.Pow(2, 1.0 / 12) - Math.Pow(2, -1.0 / 12));

    /// <summary>Floor guards degenerate HF bands; the public ceiling bounds FFT work near 20 Hz and sizes the Auto delay crop.</summary>
    private const double MinimumAlignmentGateMs = 4.0;
    public const double MaximumAlignmentGateMs = 350.0;

    private const int AlignmentGateFadeFraction = 16;

    private static int AlignmentGateSamples(int sampleRate, double bandLowHz)
    {
        double windowMs = Math.Clamp(
            AlignmentGateBandLowEdgeCycles / bandLowHz * 1_000.0,
            MinimumAlignmentGateMs,
            MaximumAlignmentGateMs);
        return (int)Math.Round(windowMs / 1_000.0 * sampleRate);
    }

    private static int AlignmentGateFadeSamples(int sampleRate, double bandLowHz) =>
        AlignmentGateSamples(sampleRate, bandLowHz) / AlignmentGateFadeFraction;

    private static int AlignmentFftLength(int sampleRate, double bandLowHz) =>
        DspMath.NextPowerOfTwo(
            AlignmentGateSamples(sampleRate, bandLowHz)
            * AlignmentFftInterpolationFactor);

    // Fixed reference-length gate for MeasureBandLevelDb: a level read needs no band-sized resolution.
    private static int AlignmentGateSamples(int sampleRate) => (int)Math.Round(
        (double)AlignmentGateReferenceSamples * sampleRate / AlignmentGateReferenceRate);

    private static int AlignmentGateFadeSamples(int sampleRate) => (int)Math.Round(
        (double)AlignmentGateReferenceFadeSamples * sampleRate / AlignmentGateReferenceRate);

    private static int AlignmentFftLength(int sampleRate) =>
        DspMath.NextPowerOfTwo(
            AlignmentGateSamples(sampleRate) * AlignmentFftInterpolationFactor);

    /// <summary>Cap on the search-side level match (= <see cref="OverlapReliabilityGateDb"/>); past it the log asks to level gains.</summary>
    public const double LevelMatchCapDb = 30;

    /// <summary>In-band level imbalance (dB, positive = variable quieter), weighted like the level match; null without content.</summary>
    public static double? MeasureInBandImbalanceDb(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        ValidSampleRange variableValidRange = default,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges = null)
    {
        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs: -1,
            maxDelayMs: 1,
            levelMatch: false,
            gateAnchorSample: null,
            variableValidRange,
            fixedValidRanges);
        double variableLevel = 0;
        double fixedLevel = 0;
        foreach (AlignmentBin bin in bins)
        {
            variableLevel += bin.RawVariableMagnitude * bin.LogWeight;
            fixedLevel += bin.FixedSum.Magnitude * bin.LogWeight;
        }
        return variableLevel > 0 && fixedLevel > 0
            ? 20.0 * Math.Log10(fixedLevel / variableLevel)
            : null;
    }

    // Spectra decimated to at most 4096 bins; the fixed channels combine by superposition.
    private static List<AlignmentBin> BuildAlignmentBins(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        double minDelayMs,
        double maxDelayMs,
        bool levelMatch = false,
        int? gateAnchorSample = null,
        ValidSampleRange variableValidRange = default,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges = null)
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

        // The window opens one fade before the front so content lands on the plateau; a front within a fade of sample 0 shrinks the fade.
        int gateSamples = AlignmentGateSamples(sampleRate, minFrequencyHz);
        int fadeSamples = AlignmentGateFadeSamples(sampleRate, minFrequencyHz);
        int length = AlignmentFftLength(sampleRate, minFrequencyHz);
        // A pre-ring lead (FIR) lengthens that response's window by the lead, so its reach past the front is unchanged.
        int maxLeadSamples = gateAnchorSample.HasValue
            ? 0
            : Math.Max(
                variableValidRange.LeadSamples,
                fixedValidRanges?.Max(range => (int?)range.LeadSamples) ?? 0);
        if (maxLeadSamples > 0)
        {
            length = DspMath.NextPowerOfTwo(
                (gateSamples + maxLeadSamples) * AlignmentFftInterpolationFactor);
        }

        // Restored to absolute time by the window start's linear phase.
        Complex[] CutSpectrum(Complex[] impulseResponse, int anchor, int leadSamples)
        {
            int clamped = Math.Clamp(anchor, 0, impulseResponse.Length - 1);
            int leftFadeSamples = Math.Min(fadeSamples, clamped);
            int gateStart = clamped - leftFadeSamples;
            int windowSamples = gateSamples + Math.Max(0, leadSamples);
            double[] gate = Windowing.TukeyWindow(
                windowSamples,
                2.0 * leftFadeSamples / windowSamples,
                2.0 * fadeSamples / windowSamples);
            Complex[] spectrum = ForwardSpectrum(
                GateDirectSound(impulseResponse, gateStart, gate), length);
            if (gateStart > 0)
            {
                double startSeconds = (double)gateStart / sampleRate;
                for (int bin = 1; bin <= length / 2; bin++)
                {
                    double frequencyHz = bin * (double)sampleRate / length;
                    Complex rotation = Complex.Exp(
                        new Complex(0, -Math.Tau * frequencyHz * startSeconds));
                    spectrum[bin] *= rotation;
                    if (bin < length / 2)
                    {
                        spectrum[length - bin] *= Complex.Conjugate(rotation);
                    }
                }
            }

            return spectrum;
        }

        Complex[] variableSpectrum;
        var fixedSpectrum = new Complex[length];
        if (gateAnchorSample is { } sharedAnchor)
        {
            // One shared window: co-located cuts need no absolute-time restoration.
            int anchor = Math.Clamp(
                sharedAnchor, 0, variableImpulseResponse.Length - 1);
            int leftFadeSamples = Math.Min(fadeSamples, anchor);
            int gateStart = anchor - leftFadeSamples;
            double[] gate = Windowing.TukeyWindow(
                gateSamples,
                2.0 * leftFadeSamples / gateSamples,
                2.0 * fadeSamples / gateSamples);
            variableSpectrum = ForwardSpectrum(
                GateDirectSound(variableImpulseResponse, gateStart, gate), length);
            foreach (Complex[] ir in fixedImpulseResponses)
            {
                Complex[] spectrum = ForwardSpectrum(
                    GateDirectSound(ir, gateStart, gate), length);
                for (int i = 0; i < length; i++)
                {
                    fixedSpectrum[i] += spectrum[i];
                }
            }
        }
        else
        {
            // Default: each response at its own front (within its valid range: a delay prefix inflates SNR); the window travels with it.
            variableSpectrum = CutSpectrum(
                variableImpulseResponse,
                FindGateAnchor(
                    variableImpulseResponse,
                    FindPeakIndex(variableImpulseResponse),
                    sampleRate,
                    minFrequencyHz,
                    maxFrequencyHz,
                    variableValidRange),
                variableValidRange.LeadSamples);
            for (int index = 0; index < fixedImpulseResponses.Count; index++)
            {
                Complex[] ir = fixedImpulseResponses[index];
                ValidSampleRange fixedRange = fixedValidRanges != null &&
                    index < fixedValidRanges.Count
                        ? fixedValidRanges[index]
                        : default;
                Complex[] spectrum = CutSpectrum(
                    ir,
                    FindGateAnchor(
                        ir, FindPeakIndex(ir), sampleRate,
                        minFrequencyHz, maxFrequencyHz, fixedRange),
                    fixedRange.LeadSamples);
                for (int i = 0; i < length; i++)
                {
                    fixedSpectrum[i] += spectrum[i];
                }
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

        // Search-only gain match in the band: lobe contrast needs equal levels.
        // See docs/tech/virtual-dsp-analysis.md#search-side-level-match.
        double variableScale = 1.0;
        if (levelMatch)
        {
            double variableLevel = 0;
            double fixedLevel = 0;
            for (int bin = firstBin; bin <= lastBin; bin += stride)
            {
                double frequencyHz = bin * (double)sampleRate / length;
                variableLevel += variableSpectrum[bin].Magnitude / frequencyHz;
                fixedLevel += fixedSpectrum[bin].Magnitude / frequencyHz;
            }
            if (variableLevel > 0 && fixedLevel > 0)
            {
                double cap = Math.Pow(10.0, LevelMatchCapDb / 20.0);
                variableScale = Math.Clamp(
                    fixedLevel / variableLevel, 1.0 / cap, cap);
            }
        }

        for (int bin = firstBin; bin <= lastBin; bin += stride)
        {
            Complex variableValue = variableSpectrum[bin] * variableScale;
            double magnitudeSum =
                fixedSpectrum[bin].Magnitude + variableValue.Magnitude;
            if (magnitudeSum > 0)
            {
                double frequencyHz = bin * (double)sampleRate / length;
                double omegaMs = Math.Tau * frequencyHz / 1_000.0;
                bins.Add(new AlignmentBin(
                    omegaMs,
                    fixedSpectrum[bin],
                    variableValue,
                    1.0 / frequencyHz,
                    magnitudeSum,
                    bin,
                    variableSpectrum[bin].Magnitude));
            }
        }

        return bins;
    }

    /// <summary>A weaker channel deeper than this is roll-off tail or residue: under the in-band combined peak for the overlap,
    /// under the stronger side in its own bin for the delay-evidence gate.</summary>
    private const double OverlapReliabilityGateDb = 30;

    /// <summary>Octaves of the pair band shared at comparable level (gated ∫ 2·min/sum over log f). Level balance, not SNR;
    /// a read-out, not a search weight. See docs/tech/virtual-dsp-analysis.md#effective-overlap.</summary>
    public static double EffectiveOverlapOctaves(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        ValidSampleRange variableValidRange = default,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges = null)
    {
        // Delay bounds only feed argument validation.
        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs: -1,
            maxDelayMs: 1,
            levelMatch: false,
            gateAnchorSample: null,
            variableValidRange,
            fixedValidRanges);
        if (bins.Count < 2)
        {
            return 0.0;
        }

        double signalPeak = 0;
        foreach (AlignmentBin bin in bins)
        {
            signalPeak = Math.Max(signalPeak, bin.MagnitudeSum);
        }
        double reliabilityFloor =
            signalPeak * Math.Pow(10.0, -OverlapReliabilityGateDb / 20.0);

        double octaves = 0;
        for (int i = 0; i < bins.Count - 1; i++)
        {
            AlignmentBin bin = bins[i];
            double fixedMag = bin.FixedSum.Magnitude;
            double variableMag = bin.Variable.Magnitude;
            double weaker = Math.Min(fixedMag, variableMag);
            double sum = fixedMag + variableMag;
            double overlap = sum > 0 && weaker >= reliabilityFloor
                ? 2.0 * weaker / sum
                : 0.0;
            double frequency = 1.0 / bin.LogWeight;
            double nextFrequency = 1.0 / bins[i + 1].LogWeight;
            octaves += overlap * Math.Log2(nextFrequency / frequency);
        }

        return octaves;
    }

    // Cross spectrum conj(F)·V for FindBestDelayMs; without polarity freedom the half-period impostor cannot win.
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

    // Coarse step below the shortest period so refinement cannot jump lobes; with invert, |corr| scores and its sign decides.
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

        // Transposed loops: one complex multiply per delay instead of Complex.Exp.
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

    // Per-bin loss floor −60 dB: one cancelled bin must not dominate the unsmoothed average.
    private const double MinBinAmplitudeRatio = 1e-3;

    // Gentle: genuine candidates differ by tenths of a dB, so it only breaks near-ties.
    private const double PriorPenaltyDbAtSigma = 0.25;

    // Each polarity seeds its own optima; the cap leaves room for three lobes.
    private const double CandidateGapDb = 1.5;
    private const int MaxAlignmentCandidates = 6;

    /// <summary>Penalty per dB of dip below the candidate's own average: weight × (DipDb − LossDb).
    /// Same weight as in <see cref="CrossoverAutoSetup"/>.</summary>
    public const double DipExcessPenaltyWeight = 0.5;

    // Loss-scored coarse grid + refinement; each polarity seeds its own optima.
    // See docs/tech/virtual-dsp-analysis.md#alignment-search-objective.
    private static List<AlignmentCandidate> SearchAlignmentCandidatesByLoss(
        List<AlignmentBin> bins,
        double minDelayMs,
        double maxDelayMs,
        double maxFrequencyHz,
        double? priorDelayMs,
        double priorSigmaMs,
        bool? forcedPolarity,
        out IReadOnlyList<AlignmentCandidate> allOptima) =>
        SearchAlignmentCandidatesByLoss(
            [bins],
            minDelayMs,
            maxDelayMs,
            maxFrequencyHz,
            priorDelayMs,
            priorSigmaMs,
            forcedPolarity,
            out allOptima);

    /// <summary>One shift for several sides, scored on the mean of their objectives; one side gives the search above.</summary>
    private static List<AlignmentCandidate> SearchAlignmentCandidatesByLoss(
        IReadOnlyList<List<AlignmentBin>> sides,
        double minDelayMs,
        double maxDelayMs,
        double maxFrequencyHz,
        double? priorDelayMs,
        double priorSigmaMs,
        bool? forcedPolarity,
        out IReadOnlyList<AlignmentCandidate> allOptima)
    {
        var weightSums = new double[sides.Count];
        for (int side = 0; side < sides.Count; side++)
        {
            foreach (AlignmentBin bin in sides[side])
            {
                weightSums[side] += bin.LogWeight;
            }
        }

        double EvaluatePolarity(double delayMs, bool invert)
        {
            double mean = 0;
            for (int side = 0; side < sides.Count; side++)
            {
                double loss = 0;
                foreach (AlignmentBin bin in sides[side])
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

                mean += loss * 20.0 / weightSums[side];
            }

            return mean / sides.Count;
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

        double coarseStep = Math.Min(0.02, 250.0 / maxFrequencyHz / 4.0);
        int gridCount = Math.Max(
            1,
            (int)Math.Floor((maxDelayMs - minDelayMs) / coarseStep + 1e-9) + 1);
        var normalDb = new double[gridCount];
        var invertedDb = new double[gridCount];
        for (int side = 0; side < sides.Count; side++)
        {
            var sideNormal = new double[gridCount];
            var sideInverted = new double[gridCount];
            foreach (AlignmentBin bin in sides[side])
            {
                Complex rotated = bin.Variable * Complex.Exp(
                    new Complex(0, -bin.OmegaMs * minDelayMs));
                Complex stepPhasor = Complex.Exp(new Complex(0, -bin.OmegaMs * coarseStep));
                for (int i = 0; i < gridCount; i++)
                {
                    sideNormal[i] += bin.LogWeight * Math.Log10(Math.Max(
                        (bin.FixedSum + rotated).Magnitude / bin.MagnitudeSum,
                        MinBinAmplitudeRatio));
                    sideInverted[i] += bin.LogWeight * Math.Log10(Math.Max(
                        (bin.FixedSum - rotated).Magnitude / bin.MagnitudeSum,
                        MinBinAmplitudeRatio));
                    rotated *= stepPhasor;
                }
            }

            for (int i = 0; i < gridCount; i++)
            {
                normalDb[i] += sideNormal[i] * 20.0 / weightSums[side];
                invertedDb[i] += sideInverted[i] * 20.0 / weightSums[side];
            }
        }

        for (int i = 0; i < gridCount; i++)
        {
            normalDb[i] /= sides.Count;
            invertedDb[i] /= sides.Count;
        }

        // A forced polarity seeds only its own grid, so every candidate is evaluated for the final sign.
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
                scores[i] = accumulated[i]
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
                // Clamped to the window; the polarity stays the seed's.
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

        // Dip excess folded in before ranking, so a notched optimum cannot tie a smooth one.
        for (int i = 0; i < refined.Count; i++)
        {
            double lossDb = 0;
            double dipDb = 0;
            for (int side = 0; side < sides.Count; side++)
            {
                (double sideLoss, double sideDip) = DetailedLoss(
                    sides[side], weightSums[side], refined[i].DelayMs, refined[i].InvertPolarity);
                lossDb += sideLoss;
                dipDb += sideDip;
            }

            lossDb /= sides.Count;
            dipDb /= sides.Count;
            refined[i] = refined[i] with
            {
                ScoreDb = refined[i].ScoreDb
                    + DipExcessPenaltyWeight * (dipDb - lossDb),
                LossDb = lossDb,
                DipDb = dipDb,
            };
        }

        // The uncapped set goes out separately: rival margins must see every optimum.
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

        // 1/6-octave moving average: a single-bin modal notch cannot pose as the dip.
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

    /// <summary>Crops IRs to one shared direct-sound window (one offset, timing intact); alignment results match the full
    /// capture at a fraction of the FFT cost.</summary>
    public static Complex[][] CropSharedDirectSoundWindow(
        IReadOnlyList<Complex[]> impulseResponses,
        int cropLength,
        int prePeakSamples) =>
        CropSharedDirectSoundWindow(
            impulseResponses, cropLength, prePeakSamples, out _);

    /// <summary>Also reports the removed offset, so original-frame metadata (<see cref="ValidSampleRange"/>) can be shifted.</summary>
    public static Complex[][] CropSharedDirectSoundWindow(
        IReadOnlyList<Complex[]> impulseResponses,
        int cropLength,
        int prePeakSamples,
        out int startSample)
    {
        ArgumentNullException.ThrowIfNull(impulseResponses);
        if (cropLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cropLength));
        }

        int earliestPeak = impulseResponses.Min(FindPeakIndex);
        int start = Math.Max(0, earliestPeak - Math.Max(0, prePeakSamples));
        startSample = start;
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

    /// <summary>Gated average loss and dip at the responses' current timing, no search. Null when the band has no usable bins.</summary>
    public static (double LossDb, double DipDb)? MeasureSumLoss(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        bool levelMatch = false,
        bool requireDelayEvidence = false,
        int? gateAnchorSample = null,
        ValidSampleRange variableValidRange = default,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges = null)
    {
        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs: -1,
            maxDelayMs: 1,
            levelMatch,
            gateAnchorSample,
            variableValidRange,
            fixedValidRanges);
        // Comparing callers must not read a verdict where the delay is unobservable (near-flat loss).
        if (requireDelayEvidence &&
            (bins.Count == 0 || !HoldsDelayEvidence(bins)))
        {
            return null;
        }
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

    /// <summary>Loss, dip and ripple of the sum (dB RMS about its mean): drivers summing into a 6 dB hump lose nothing
    /// but are not flat. Null when the band has no usable bins.</summary>
    public static JunctionSpectrumReading? MeasureJunctionSpectrum(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        ValidSampleRange variableValidRange = default,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges = null) =>
        MeasureJunctionSpectrum(
            variableImpulseResponse, fixedImpulseResponses, sampleRate, minFrequencyHz, maxFrequencyHz,
            variableValidRange, fixedValidRanges, levels: null);

    /// <summary>The same read, also handing back each bin's two levels.</summary>
    public static JunctionSpectrumReading? MeasureJunctionSpectrum(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        ValidSampleRange variableValidRange,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges,
        out IReadOnlyList<JunctionLevelBin> levelBins)
    {
        var collected = new List<JunctionLevelBin>();
        JunctionSpectrumReading? reading = MeasureJunctionSpectrum(
            variableImpulseResponse, fixedImpulseResponses, sampleRate, minFrequencyHz, maxFrequencyHz,
            variableValidRange, fixedValidRanges, collected);
        levelBins = collected;
        return reading;
    }

    private static JunctionSpectrumReading? MeasureJunctionSpectrum(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        ValidSampleRange variableValidRange,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges,
        List<JunctionLevelBin>? levels)
    {
        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs: -1,
            maxDelayMs: 1,
            levelMatch: false,
            gateAnchorSample: null,
            variableValidRange,
            fixedValidRanges);
        if (bins.Count == 0)
        {
            return null;
        }

        if (levels != null)
        {
            foreach (AlignmentBin bin in bins)
            {
                levels.Add(new JunctionLevelBin(
                    bin.OmegaMs * 1_000.0 / Math.Tau,
                    bin.LogWeight,
                    20 * Math.Log10(Math.Max(bin.FixedSum.Magnitude, 1e-12)),
                    20 * Math.Log10(Math.Max(bin.Variable.Magnitude, 1e-12))));
            }
        }

        return ReadAt(bins, delayMs: 0, invert: false);
    }

    /// <summary>The junction read at the variable side's best timing within +/- <paramref name="halfWindowMs"/>, chosen
    /// as the wizard's post-check chooses it. Null without usable bins or delay evidence.</summary>
    public static (JunctionSpectrumReading Reading, AlignmentCandidate Alignment)? MeasureAlignedJunctionSpectrum(
        Complex[] variableImpulseResponse,
        IReadOnlyList<Complex[]> fixedImpulseResponses,
        int sampleRate,
        double minFrequencyHz,
        double maxFrequencyHz,
        double halfWindowMs,
        ValidSampleRange variableValidRange = default,
        IReadOnlyList<ValidSampleRange>? fixedValidRanges = null)
    {
        List<AlignmentBin> bins = BuildAlignmentBins(
            variableImpulseResponse,
            fixedImpulseResponses,
            sampleRate,
            minFrequencyHz,
            maxFrequencyHz,
            minDelayMs: -halfWindowMs,
            maxDelayMs: halfWindowMs,
            levelMatch: false,
            gateAnchorSample: null,
            variableValidRange,
            fixedValidRanges);
        if (bins.Count == 0 || !HoldsDelayEvidence(bins))
        {
            return null;
        }

        IReadOnlyList<AlignmentCandidate> found = SearchAlignmentCandidatesByLoss(
            bins,
            -halfWindowMs,
            halfWindowMs,
            maxFrequencyHz,
            priorDelayMs: 0,
            priorSigmaMs: halfWindowMs / 2.0,
            forcedPolarity: null,
            out _);
        if (found.Count == 0)
        {
            return null;
        }

        AlignmentCandidate chosen = AlignmentSelection.Select(found, 0);
        return (ReadAt(bins, chosen.DelayMs, chosen.InvertPolarity), chosen);
    }

    /// <summary>Every side read at one timing of the variable side, chosen on the mean of the sides' objectives, as a
    /// mono block's single delay requires. Only sides with delay evidence vote; null where none does.</summary>
    public static (IReadOnlyList<JunctionSpectrumReading?> Readings, AlignmentCandidate Alignment)?
        MeasureJointlyAlignedJunctionSpectra(
            IReadOnlyList<JunctionAlignmentSide> sides,
            double minFrequencyHz,
            double maxFrequencyHz,
            double halfWindowMs)
    {
        ArgumentNullException.ThrowIfNull(sides);
        var bins = new List<List<AlignmentBin>>(sides.Count);
        foreach (JunctionAlignmentSide side in sides)
        {
            bins.Add(BuildAlignmentBins(
                side.VariableImpulseResponse,
                [side.FixedImpulseResponse],
                side.SampleRate,
                minFrequencyHz,
                maxFrequencyHz,
                minDelayMs: -halfWindowMs,
                maxDelayMs: halfWindowMs,
                levelMatch: false,
                gateAnchorSample: null,
                side.VariableValidRange,
                [side.FixedValidRange]));
        }

        List<List<AlignmentBin>> voters = bins
            .Where(side => side.Count > 0 && HoldsDelayEvidence(side))
            .ToList();
        if (voters.Count == 0)
        {
            return null;
        }

        IReadOnlyList<AlignmentCandidate> found = SearchAlignmentCandidatesByLoss(
            voters,
            -halfWindowMs,
            halfWindowMs,
            maxFrequencyHz,
            priorDelayMs: 0,
            priorSigmaMs: halfWindowMs / 2.0,
            forcedPolarity: null,
            out _);
        if (found.Count == 0)
        {
            return null;
        }

        AlignmentCandidate chosen = AlignmentSelection.Select(found, 0);
        return (
            bins.Select(side => side.Count == 0
                    ? null
                    : ReadAt(side, chosen.DelayMs, chosen.InvertPolarity))
                .ToList(),
            chosen);
    }


    private static JunctionSpectrumReading ReadAt(List<AlignmentBin> bins, double delayMs, bool invert)
    {
        double weightSum = 0;
        double levelSum = 0;
        var sumLevels = new double[bins.Count];
        for (int i = 0; i < bins.Count; i++)
        {
            AlignmentBin bin = bins[i];
            weightSum += bin.LogWeight;
            Complex variable = delayMs == 0
                ? bin.Variable
                : bin.Variable * Complex.Exp(new Complex(0, -bin.OmegaMs * delayMs));
            double magnitude = (invert ? bin.FixedSum - variable : bin.FixedSum + variable).Magnitude;
            sumLevels[i] = 20 * Math.Log10(Math.Max(magnitude, 1e-12));
            levelSum += bin.LogWeight * sumLevels[i];
        }

        double mean = levelSum / weightSum;
        double variance = 0;
        for (int i = 0; i < bins.Count; i++)
        {
            double deviation = sumLevels[i] - mean;
            variance += bins[i].LogWeight * deviation * deviation;
        }

        (double lossDb, double dipDb) = DetailedLoss(bins, weightSum, delayMs, invert);
        return new JunctionSpectrumReading(lossDb, dipDb, Math.Sqrt(variance / weightSum));
    }

    /// <summary>Gated band level (dB): 1/f-weighted mean of bin POWERS, so cabin nulls barely move it. Arbitrary reference:
    /// compare responses over the same band.</summary>
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
        int gateSamples = AlignmentGateSamples(sampleRate);
        int fadeSamples = AlignmentGateFadeSamples(sampleRate);
        int leftFade = Math.Min(fadeSamples, anchor);
        double[] gate = Windowing.TukeyWindow(
            gateSamples,
            2.0 * leftFade / gateSamples,
            2.0 * fadeSamples / gateSamples);
        int length = AlignmentFftLength(sampleRate);
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

        // 1e-24 power floor = 1e-12 magnitude floor.
        return weightSum > 0
            ? 10.0 * Math.Log10(Math.Max(total / weightSum, 1e-24))
            : null;
    }

    /// <summary>Junction-loss probe that rotates the variable spectrum by e^{-jωΔ} instead of re-running chains;
    /// <c>Evaluate(0)</c> reproduces <see cref="MeasureSumLoss"/>.</summary>
    public sealed class SumLossEvaluator
    {
        private readonly List<AlignmentBin> bins;
        private readonly double weightSum;

        private SumLossEvaluator(List<AlignmentBin> bins, double weightSum)
        {
            this.bins = bins;
            this.weightSum = weightSum;
        }

        public static SumLossEvaluator? Create(
            Complex[] variableImpulseResponse,
            IReadOnlyList<Complex[]> fixedImpulseResponses,
            int sampleRate,
            double minFrequencyHz,
            double maxFrequencyHz,
            bool levelMatch = false,
            bool requireDelayEvidence = false,
            int? gateAnchorSample = null,
            ValidSampleRange variableValidRange = default,
            IReadOnlyList<ValidSampleRange>? fixedValidRanges = null)
        {
            List<AlignmentBin> bins = BuildAlignmentBins(
                variableImpulseResponse,
                fixedImpulseResponses,
                sampleRate,
                minFrequencyHz,
                maxFrequencyHz,
                minDelayMs: -1,
                maxDelayMs: 1,
                levelMatch,
                gateAnchorSample,
                variableValidRange,
                fixedValidRanges);
            if (bins.Count == 0 ||
                (requireDelayEvidence && !HoldsDelayEvidence(bins)))
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

        public (double LossDb, double DipDb) Evaluate(
            double extraDelayMs, bool invertVariable = false) =>
            DetailedLoss(bins, weightSum, extraDelayMs, invertVariable);
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

    /// <summary>Points more than this below the local combined peak read NaN: there the loss is noise-floor phase arithmetic.
    /// See docs/tech/virtual-dsp-analysis.md#sum-loss-curve-and-level-gate.</summary>
    public const double SumLossLevelGateDb = 25;

    public const double SumLossLevelGateReferenceOctaves = 1.0;

    /// <summary>Points where every channel sits more than this below its own peak read NaN: all of them are in their stop
    /// bands, where a FIR's floor pairs with its partner's at comparable level. Same depth as the Group Delay mode's gate.
    /// See docs/tech/virtual-dsp-analysis.md#sum-loss-curve-and-level-gate.</summary>
    public const double SumLossChannelPresenceDb = 40;

    /// <summary>Per-point sum loss (dB, ≤ 0), the single definition behind the drawn curve and read-outs. Operands must be
    /// UNSMOOTHED; smoothing applies to the ratio (smoothing first invents a dip at every steep corner).
    /// See docs/tech/virtual-dsp-analysis.md#sum-loss-curve-and-level-gate.</summary>
    public static List<SignalPoint> SumLossCurve(
        IReadOnlyList<SignalPoint> sumCurve,
        IReadOnlyList<IReadOnlyList<SignalPoint>> channelCurves,
        double smoothingInverseOctaves = 0)
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
                // A NaN channel (measured nothing here) is skipped rather than poisoning the sum.
                if (double.IsFinite(curve[i].Y))
                {
                    magnitudeSum += DataHelper.DecibelsToAmplitude(curve[i].Y);
                }
            }

            magnitudeSums[i] = magnitudeSum;
        }

        var presenceFloorsDb = new double[channelCurves.Count];
        for (int channel = 0; channel < channelCurves.Count; channel++)
        {
            double peakDb = double.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (double.IsFinite(channelCurves[channel][i].Y))
                {
                    peakDb = Math.Max(peakDb, channelCurves[channel][i].Y);
                }
            }

            presenceFloorsDb[channel] = peakDb - SumLossChannelPresenceDb;
        }

        bool SomeChannelPlays(int i)
        {
            for (int channel = 0; channel < channelCurves.Count; channel++)
            {
                double levelDb = channelCurves[channel][i].Y;
                if (double.IsFinite(levelDb) && levelDb >= presenceFloorsDb[channel])
                {
                    return true;
                }
            }

            return false;
        }

        double[] localPeaks = LocalMagnitudePeaks(sumCurve, magnitudeSums, count);
        double gate = DataHelper.DecibelsToAmplitude(-SumLossLevelGateDb);
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            double gateFloor = localPeaks[i] * gate;
            bool measurable = localPeaks[i] > 0 && magnitudeSums[i] >= gateFloor &&
                SomeChannelPlays(i);
            points.Add(new SignalPoint(
                sumCurve[i].X,
                measurable
                    ? sumCurve[i].Y - DataHelper.AmplitudeToDecibels(magnitudeSums[i])
                    : double.NaN));
        }

        return smoothingInverseOctaves != 0
            ? DataHelper.SmoothRatioLevels(
                points,
                SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves),
                SpectrumSmoothing.IsPsychoacoustic(smoothingInverseOctaves))
            : points;
    }

    /// <summary>Loudest combined magnitude within the reference octaves of each point; O(n) via a monotonic deque.</summary>
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
        var deque = new int[count];
        int head = 0;
        int tail = 0;
        int start = 0;
        int end = 0;
        for (int i = 0; i < count; i++)
        {
            double lower = curve[i].X / ratio;
            double upper = curve[i].X * ratio;

            while (end < count && curve[end].X <= upper)
            {
                while (tail > head && reference[deque[tail - 1]] <= reference[end])
                {
                    tail--;
                }

                deque[tail++] = end;
                end++;
            }

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

    /// <summary>Predicted average sum loss from spectra (1/24-octave grid, ±1/6-octave power smoothing), for Auto delay
    /// before/after quotes without UI state.</summary>
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

    /// <summary>Average of a <see cref="SumLossCurve"/> in the window, so read-out and curve agree by construction.</summary>
    public static double? AverageSumLossDb(
        IReadOnlyList<SignalPoint> lossCurve,
        double minFrequencyHz,
        double maxFrequencyHz)
    {
        ArgumentNullException.ThrowIfNull(lossCurve);

        double total = 0;
        int samples = 0;
        foreach (SignalPoint point in lossCurve)
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

    /// <summary>Deepest point of a <see cref="SumLossCurve"/> in the window: the audible notch the average hides.</summary>
    public static double? MinimumSumLossDb(
        IReadOnlyList<SignalPoint> lossCurve,
        double minFrequencyHz,
        double maxFrequencyHz)
    {
        ArgumentNullException.ThrowIfNull(lossCurve);

        double? minimum = null;
        foreach (SignalPoint point in lossCurve)
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

    /// <summary>Polarity from the first lobe reaching a quarter of the peak: the global extremum misreads ringing drivers,
    /// while pre-ringing stays near a tenth.</summary>
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
