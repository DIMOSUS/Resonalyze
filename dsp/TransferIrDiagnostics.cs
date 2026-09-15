using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>Contiguous region of the 1/6-oct smoothed spectrum within <see cref="TransferIrDiagnostics.DominantBandThresholdDb"/> of its peak.</summary>
public readonly record struct DominantBand(double LowHz, double HighHz, double PeakHz);

/// <summary>Pre-arrival crosstalk click and the head gate removing it (zero before <see cref="GateEndSample"/>, short fade after).</summary>
public readonly record struct CrosstalkHeadGate(
    int GateEndSample,
    double BurstTimeMs,
    double BurstPeakDbReMax);

/// <summary>Start of a measured IR's acoustic content. See docs/tech/sweep-measurement.md#ir-start-estimate.</summary>
public readonly record struct IrStartEstimate(
    double StartMs,
    double EarlyMs,
    double LateMs,
    double BandLowHz,
    double BandHighHz,
    bool DominantBandLimited);

/// <summary>Time-compactness of a transfer IR. See docs/tech/sweep-measurement.md#compactness.</summary>
public readonly record struct TransferIrCompactness(
    double InsideOutsideDb,
    double PeakDelayMs);

/// <summary>Transfer IR record-hygiene diagnostics. See docs/tech/sweep-measurement.md#transfer-ir-diagnostics.</summary>
public static class TransferIrDiagnostics
{
    /// <summary>Credible-IR floor: field genuine 28.8-48.6 dB, bleed loopback 11.2-18.7. See docs/tech/sweep-measurement.md#compactness.</summary>
    public const double MinimumCompactnessDb = 22.0;

    /// <summary>Spans a wrong-excitation smear; short enough that room decay contributes little.</summary>
    public const double ArrivalWindowSeconds = 0.002;

    /// <summary>Same 100 ms the compactness window reserves for kernel pre-ringing.</summary>
    public const double PreArrivalStartSeconds = 0.100;

    public const double PreArrivalEndSeconds = 0.600;

    /// <summary>Above this the record is reported, never refused. See docs/tech/sweep-measurement.md#pre-arrival.</summary>
    public const double SuspectPreArrivalDb = -22.0;

    public const double MinimumJudgeableGuardOctaves = 0.30;

    /// <summary>Floor below which the IR is not a measurement of the analysed sweep. See docs/tech/sweep-measurement.md#arrival-sharpness.</summary>
    public const double MinimumArrivalSharpnessDb = 8.0;

    // Circular: the zero-phase gate's kernel pre-rings at the buffer's far end. See docs/tech/sweep-measurement.md#compactness.
    private const double CompactnessPreSeconds = 0.100;
    private const double CompactnessPostSeconds = 0.500;
    private const int CompactnessMinimumSamples = 256;

    /// <summary>Peak above the RMS of its own +-<see cref="ArrivalWindowSeconds"/> neighbourhood, circular; null when unmeasurable.</summary>
    public static double? MeasureArrivalSharpnessDb(
        IReadOnlyList<Complex> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        int length = impulseResponse.Count;
        if (length == 0 || sampleRate <= 0)
        {
            return null;
        }

        double total = 0;
        double peak = 0;
        int peakIndex = 0;
        for (int i = 0; i < length; i++)
        {
            double sample = impulseResponse[i].Real;
            total += sample * sample;
            if (Math.Abs(sample) > peak)
            {
                peak = Math.Abs(sample);
                peakIndex = i;
            }
        }
        if (!double.IsFinite(total) || total <= 0)
        {
            return null;
        }

        int half = Math.Min((int)(ArrivalWindowSeconds * sampleRate), length / 2);
        double near = 0;
        for (int k = -half; k <= half; k++)
        {
            double sample = impulseResponse[((peakIndex + k) % length + length) % length].Real;
            near += sample * sample;
        }

        double rms = Math.Sqrt(near / (2 * half + 1));
        return rms > 0 ? 20.0 * Math.Log10(peak / rms) : null;
    }

    /// <summary>Energy geometry only, no peak-position rule, so electrical chain measurements (peak ~0 ms) pass. Null when unjudgeable.</summary>
    internal static TransferIrCompactness? MeasureCompactness(
        IReadOnlyList<double> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        int length = impulseResponse.Count;
        if (length < CompactnessMinimumSamples || sampleRate <= 0)
        {
            return null;
        }

        double total = 0;
        double peak = 0;
        int peakIndex = 0;
        for (int i = 0; i < length; i++)
        {
            double sample = impulseResponse[i];
            total += sample * sample;
            if (Math.Abs(sample) > peak)
            {
                peak = Math.Abs(sample);
                peakIndex = i;
            }
        }
        // Null also for NaN/Infinity: callers treat an unmeasurable shape as a refusal.
        if (!double.IsFinite(total) || total <= 0)
        {
            return null;
        }

        int pre = Math.Min((int)(CompactnessPreSeconds * sampleRate), length / 16);
        int post = Math.Min((int)(CompactnessPostSeconds * sampleRate), length / 4);
        double windowed = 0;
        for (int k = -pre; k <= post; k++)
        {
            int index = ((peakIndex + k) % length + length) % length;
            double sample = impulseResponse[index];
            windowed += sample * sample;
        }

        int windowLength = pre + post + 1;
        int outsideLength = length - windowLength;
        double insidePerSample = windowed / windowLength;
        double outsidePerSample = Math.Max(
            insidePerSample * 1e-12, (total - windowed) / outsideLength);
        double peakDelayMs = peakIndex <= length / 2
            ? peakIndex * 1000.0 / sampleRate
            : (peakIndex - length) * 1000.0 / sampleRate;
        return new TransferIrCompactness(
            10 * Math.Log10(insidePerSample / outsidePerSample),
            peakDelayMs);
    }

    public static TransferIrCompactness? MeasureCompactness(
        IReadOnlyList<Complex> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        // View, not copy: a real-part array would be 34 MB at 96 kHz / 20 s.
        return MeasureCompactness(new RealPartsView(impulseResponse), sampleRate);
    }

    /// <summary>Energy well before the arrival vs the arrival neighbourhood (circular). See docs/tech/sweep-measurement.md#pre-arrival.</summary>
    public static double? MeasurePreArrivalDb(
        IReadOnlyList<Complex> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        return MeasurePreArrivalDb(new RealPartsView(impulseResponse), sampleRate);
    }

    internal static double? MeasurePreArrivalDb(
        IReadOnlyList<double> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        int length = impulseResponse.Count;
        if (length < CompactnessMinimumSamples || sampleRate <= 0)
        {
            return null;
        }

        int start = (int)(PreArrivalStartSeconds * sampleRate);
        // Shrinks on short records so the two windows cannot meet around the circle.
        int end = Math.Min((int)(PreArrivalEndSeconds * sampleRate), length / 4);
        if (start <= 0 || end < 2 * start)
        {
            return null;
        }

        double peak = 0;
        int peakIndex = 0;
        for (int i = 0; i < length; i++)
        {
            double sample = impulseResponse[i];
            if (Math.Abs(sample) > peak)
            {
                peak = Math.Abs(sample);
                peakIndex = i;
            }
        }

        double arrival = 0;
        for (int k = -start; k <= start; k++)
        {
            double sample = impulseResponse[((peakIndex + k) % length + length) % length];
            arrival += sample * sample;
        }

        double before = 0;
        for (int k = -end; k < -start; k++)
        {
            double sample = impulseResponse[((peakIndex + k) % length + length) % length];
            before += sample * sample;
        }

        if (!double.IsFinite(arrival) || !double.IsFinite(before) || arrival <= 0)
        {
            return null;
        }

        double arrivalPerSample = arrival / (2 * start + 1);
        double beforePerSample = before / (end - start);
        return 10 * Math.Log10(
            Math.Max(beforePerSample, arrivalPerSample * 1e-12) / arrivalPerSample);
    }

    /// <summary>Whether the low guard band is wide enough that the gate's own kernel cannot mimic the fault. See docs/tech/sweep-measurement.md#pre-arrival.</summary>
    public static bool CanJudgePreArrival(ExcitationBandGate gate)
    {
        if (gate.LowFullNyquistFraction <= 0)
        {
            return true;
        }
        if (gate.LowZeroNyquistFraction <= 0)
        {
            return true;
        }

        double guardOctaves = Math.Log2(
            gate.LowFullNyquistFraction / gate.LowZeroNyquistFraction);
        return double.IsFinite(guardOctaves) &&
            guardOctaves >= MinimumJudgeableGuardOctaves;
    }

    private sealed class RealPartsView(IReadOnlyList<Complex> source)
        : IReadOnlyList<double>
    {
        public int Count => source.Count;

        public double this[int index] => source[index].Real;

        public IEnumerator<double> GetEnumerator()
        {
            for (int i = 0; i < source.Count; i++)
            {
                yield return source[i].Real;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    /// <summary>15 dB: driver-shaped bands; 20 swallows cabin shelves, 12 modal-latches midbass. See docs/tech/sweep-measurement.md#dominant-band.</summary>
    public const double DominantBandThresholdDb = 15.0;

    // Resolves 1/6 octave at 20 Hz; the head artifact sits in the first milliseconds anyway.
    private const int MaxAnalysisSamples = 65_536;

    // Crosstalk detection constants: see docs/tech/sweep-measurement.md#crosstalk-head-gate.
    private const double ComplementGapOctaves = 0.5;
    private const double ComplementMinimumOctaves = 1.0;

    private const double IslandEndBelowPeakDb = 12.0;
    private const double IslandEndHoldSeconds = 0.00025;

    private const double PreFrontGuardSeconds = 0.002;

    private const double IslandCapSeconds = 0.010;

    private const double InBandQuietBeforeGateDb = 15.0;

    private const double FirstArrivalJumpMs = 2.0;

    private const double FadeSeconds = 0.0004;

    public static DominantBand DetectDominantBand(
        IReadOnlyList<double> impulseResponse,
        int sampleRate,
        double thresholdDb = DominantBandThresholdDb,
        IReadOnlyList<double>? coherence = null)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Count == 0)
        {
            throw new ArgumentException(
                "Impulse response must not be empty.", nameof(impulseResponse));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int length = Math.Min(impulseResponse.Count, MaxAnalysisSamples);
        var spectrum = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            spectrum[i] = impulseResponse[i];
        }
        Fourier.Forward(spectrum, FourierOptions.Matlab);

        int half = length / 2;
        double topHz = Math.Min(20_000, sampleRate * 0.45);

        double CoherenceAt(double hz)
        {
            if (coherence == null || coherence.Count < 2)
            {
                return 1.0;
            }

            double position = hz * 2.0 * (coherence.Count - 1) / sampleRate;
            if (position <= 0.0)
            {
                return coherence[0];
            }
            if (position >= coherence.Count - 1)
            {
                return coherence[^1];
            }

            int lower = (int)position;
            double fraction = position - lower;
            return coherence[lower] * (1.0 - fraction) +
                coherence[lower + 1] * fraction;
        }

        double SmoothedDb(double hz)
        {
            double lo = hz / Math.Pow(2.0, 1.0 / 12);
            double hi = hz * Math.Pow(2.0, 1.0 / 12);
            int i1 = Math.Max(1, (int)(lo * length / sampleRate));
            int i2 = Math.Min(half - 1, (int)(hi * length / sampleRate));
            if (i2 < i1)
            {
                i2 = i1;
            }
            double sum = 0;
            int trustedCount = 0;
            int binCount = i2 - i1 + 1;
            for (int i = i1; i <= i2; i++)
            {
                double binCoherence = CoherenceAt((double)i * sampleRate / length);
                if (!double.IsFinite(binCoherence) || binCoherence < 0.5)
                {
                    continue;
                }

                sum += spectrum[i].Magnitude * spectrum[i].Magnitude;
                trustedCount++;
            }
            // At least half the window must be coherent, so a lone bin cannot stand for it.
            return trustedCount * 2 >= binCount
                ? 10 * Math.Log10(Math.Max(1e-24, sum / trustedCount))
                : double.NegativeInfinity;
        }

        var gridHz = new List<double>();
        var gridDb = new List<double>();
        for (double hz = 20; hz <= topHz; hz *= Math.Pow(2.0, 1.0 / 24))
        {
            gridHz.Add(hz);
            gridDb.Add(SmoothedDb(hz));
        }

        if (coherence != null && gridDb.All(double.IsNegativeInfinity))
        {
            throw new InvalidOperationException(
                "No reliable signal remains above the coherence threshold.");
        }

        int peakIndex = 0;
        for (int i = 1; i < gridDb.Count; i++)
        {
            if (gridDb[i] > gridDb[peakIndex])
            {
                peakIndex = i;
            }
        }

        // Bridge narrow dips (cabin notches), stop at wide silence. See docs/tech/sweep-measurement.md#dominant-band.
        double floorDb = gridDb[peakIndex] - thresholdDb;
        int maxGapSteps = (int)Math.Round(MaxBridgedGapOctaves * 24);
        int solidSteps = (int)Math.Round(MinSolidLandingOctaves * 24);
        // A bridge must land on a solid stretch; chaining through ripple spikes tripled the field midbass band.
        bool SolidAt(int start, int step)
        {
            for (int i = 0; i < solidSteps; i++)
            {
                int index = start + step * i;
                if (index < 0 || index >= gridDb.Count || gridDb[index] < floorDb)
                {
                    return false;
                }
            }
            return true;
        }
        int Expand(int from, int step)
        {
            int edge = from;
            while (true)
            {
                int next = edge + step;
                if (next < 0 || next >= gridDb.Count)
                {
                    return edge;
                }
                if (gridDb[next] >= floorDb)
                {
                    edge = next;
                    continue;
                }
                int across = -1;
                for (int k = 2; k <= maxGapSteps; k++)
                {
                    int candidate = edge + step * k;
                    if (candidate < 0 || candidate >= gridDb.Count)
                    {
                        break;
                    }
                    if (SolidAt(candidate, step))
                    {
                        across = candidate;
                        break;
                    }
                }
                if (across < 0)
                {
                    return edge;
                }
                edge = across;
            }
        }
        int low = Expand(peakIndex, -1);
        int high = Expand(peakIndex, +1);

        return new DominantBand(gridHz[low], gridHz[high], gridHz[peakIndex]);
    }

    /// <summary>
    /// Rising-front crossings of the first credible arrival, read inside the dominant band; falls back to full band, null
    /// when no credible arrival. See docs/tech/sweep-measurement.md#ir-start-estimate.
    /// </summary>
    public static IrStartEstimate? EstimateIrStart(
        IReadOnlyList<double> impulseResponse,
        int sampleRate,
        ValidSampleRange validRange = default)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (sampleRate <= 0)
        {
            return null;
        }

        // Analyse only the valid range but report full-record times: a silent chain-delay prefix sinks the noise floor and fakes SNR.
        int rangeStart = 0;
        if (validRange.IsKnown)
        {
            rangeStart = Math.Clamp(
                validRange.StartSample, 0, impulseResponse.Count);
            int rangeEnd = Math.Clamp(
                validRange.EndSample, rangeStart, impulseResponse.Count);
            var window = new double[rangeEnd - rangeStart];
            for (int i = 0; i < window.Length; i++)
            {
                window[i] = impulseResponse[rangeStart + i];
            }
            impulseResponse = window;
        }
        if (impulseResponse.Count < IrStartMinimumSamples)
        {
            return null;
        }

        if (impulseResponse.Count > MaxAnalysisSamples)
        {
            var head = new double[MaxAnalysisSamples];
            for (int i = 0; i < head.Length; i++)
            {
                head[i] = impulseResponse[i];
            }
            impulseResponse = head;
        }

        DominantBand band = DetectDominantBand(
            impulseResponse, sampleRate, IrStartBandThresholdDb);
        TimeAlignmentAnalysisResult inBand = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(band.LowHz * band.HighHz),
                BandpassPassOctaves = Math.Log2(band.HighHz / band.LowHz),
                BandpassFadeOctaves = 0.25
            });
        double offsetMs = rangeStart * 1_000.0 / sampleRate;
        if (IsCredible(inBand))
        {
            return Offset(
                CrossingsOf(inBand, band, sampleRate, dominantBandLimited: true),
                offsetMs);
        }

        TimeAlignmentAnalysisResult fullBand = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, new TimeAlignmentAnalysisOptions());
        return IsCredible(fullBand)
            ? Offset(
                CrossingsOf(fullBand, band, sampleRate, dominantBandLimited: false),
                offsetMs)
            : null;
    }

    private static IrStartEstimate Offset(IrStartEstimate estimate, double offsetMs) =>
        offsetMs == 0
            ? estimate
            : estimate with
            {
                StartMs = estimate.StartMs + offsetMs,
                EarlyMs = estimate.EarlyMs + offsetMs,
                LateMs = estimate.LateMs + offsetMs
            };

    /// Wider than DominantBandThresholdDb: time resolves only to ~1/bandwidth. See docs/tech/sweep-measurement.md#ir-start-estimate.
    private const double IrStartBandThresholdDb = 25.0;

    private const int IrStartMinimumSamples = 256;

    // Same floor as AutoAlignmentEngine.OnsetLockMinimumSnrDb; clean cabin records grade 50+ dB, pure noise ~10-13.
    private const double IrStartMinimumSnrDb = 20;

    private static bool IsCredible(TimeAlignmentAnalysisResult result) =>
        result.IsValid && result.SignalToNoiseDecibels >= IrStartMinimumSnrDb;

    public static IrStartEstimate? EstimateIrStart(
        IReadOnlyList<Complex> impulseResponse,
        int sampleRate,
        ValidSampleRange validRange = default)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        // Head cap counts from the range start, or a late-content record is truncated before its front.
        int rangeStart = validRange.IsKnown
            ? Math.Clamp(validRange.StartSample, 0, impulseResponse.Count)
            : 0;
        int length = Math.Min(
            impulseResponse.Count, rangeStart + MaxAnalysisSamples);
        var samples = new double[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = impulseResponse[i].Real;
        }
        return EstimateIrStart(samples, sampleRate, validRange);
    }

    private static IrStartEstimate CrossingsOf(
        TimeAlignmentAnalysisResult result,
        DominantBand band,
        int sampleRate,
        bool dominantBandLimited)
    {
        double[] envelope = result.EnvelopeSamples;
        int peakIndex = result.EnvelopePeakIndex;
        double peak = envelope[peakIndex];
        return new IrStartEstimate(
            RisingFrontCrossingMs(envelope, peakIndex, 0.25 * peak, sampleRate),
            RisingFrontCrossingMs(envelope, peakIndex, 0.10 * peak, sampleRate),
            RisingFrontCrossingMs(envelope, peakIndex, 0.50 * peak, sampleRate),
            band.LowHz,
            band.HighHz,
            dominantBandLimited);
    }

    // Pinned to the arrival peak's own front: an earlier disjoint event past a dip never captures the crossing.
    private static double RisingFrontCrossingMs(
        IReadOnlyList<double> envelope,
        int peakIndex,
        double threshold,
        int sampleRate)
    {
        int i = peakIndex;
        while (i > 0 && envelope[i] > threshold)
        {
            i--;
        }
        if (i == peakIndex)
        {
            return peakIndex * 1_000.0 / sampleRate;
        }

        double below = envelope[i];
        double above = envelope[i + 1];
        // Floored at the record start: a front running off the head would extrapolate unboundedly (field: -1.5 ms).
        double fraction = above > below
            ? Math.Max(0.0, (threshold - below) / (above - below))
            : 0.0;
        return (i + fraction) * 1_000.0 / sampleRate;
    }

    private const double MaxBridgedGapOctaves = 0.5;

    private const double MinSolidLandingOctaves = 1.0 / 6.0;

    /// <summary>
    /// Playback-crosstalk click in the head of a BAND-LIMITED record, convicted by trial removal; full-range records
    /// always return null. See docs/tech/sweep-measurement.md#crosstalk-head-gate.
    /// </summary>
    public static CrosstalkHeadGate? DetectCrosstalkHead(
        IReadOnlyList<double> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Count == 0 || sampleRate <= 0)
        {
            return null;
        }

        // Capped so the verdict is identical between the panel, the engine and the probes.
        if (impulseResponse.Count > MaxAnalysisSamples)
        {
            var head = new double[MaxAnalysisSamples];
            for (int i = 0; i < head.Length; i++)
            {
                head[i] = impulseResponse[i];
            }
            impulseResponse = head;
        }

        DominantBand band = DetectDominantBand(impulseResponse, sampleRate);
        double complementLow = band.HighHz * Math.Pow(2.0, ComplementGapOctaves);
        double complementHigh = Math.Min(20_000, sampleRate * 0.45);
        if (complementHigh < complementLow * Math.Pow(2.0, ComplementMinimumOctaves))
        {
            return null;
        }

        TimeAlignmentAnalysisResult complement = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(complementLow * complementHigh),
                BandpassPassOctaves = Math.Log2(complementHigh / complementLow),
                BandpassFadeOctaves = 0.25
            });
        if (!complement.IsValid)
        {
            return null;
        }

        TimeAlignmentAnalysisResult inBand = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(band.LowHz * band.HighHz),
                BandpassPassOctaves = Math.Log2(band.HighHz / band.LowHz),
                BandpassFadeOctaves = 0.25
            });
        if (!inBand.IsValid)
        {
            return null;
        }

        int clickIndex = complement.EnvelopePeakIndex;
        double[] envelope = complement.EnvelopeSamples;
        double clickPeak = envelope[clickIndex];
        double islandFloor = clickPeak * Math.Pow(10, -IslandEndBelowPeakDb / 20);
        int hold = Math.Max(1, (int)(sampleRate * IslandEndHoldSeconds));
        int islandCap = Math.Min(
            envelope.Length, clickIndex + (int)(sampleRate * IslandCapSeconds));
        int islandEnd = -1;
        int below = 0;
        for (int i = clickIndex; i < islandCap; i++)
        {
            if (envelope[i] < islandFloor)
            {
                below++;
                if (below >= hold)
                {
                    islandEnd = i - hold + 1;
                    break;
                }
            }
            else
            {
                below = 0;
            }
        }
        if (islandEnd < 0)
        {
            return null;
        }
        int fade = Math.Max(1, (int)(sampleRate * FadeSeconds));
        int gateEnd = islandEnd + fade;

        // Proportionality guard, not an onset-threshold walk (poisoned by the click's in-band shadow and window pre-ring).
        int guard = (int)(sampleRate * PreFrontGuardSeconds);
        if (clickIndex + guard >= inBand.EnvelopePeakIndex ||
            gateEnd > clickIndex + (inBand.EnvelopePeakIndex - clickIndex) / 2)
        {
            return null;
        }

        // In-band envelope must stay quiet over the gated stretch, which refuses co-onset genuine out-of-band bursts.
        double[] inBandEnvelope = inBand.EnvelopeSamples;
        double inBandFirstPeak = inBandEnvelope[inBand.EnvelopePeakIndex];
        double quietCeiling =
            inBandFirstPeak * Math.Pow(10, -InBandQuietBeforeGateDb / 20);
        for (int i = 0; i < gateEnd && i < inBandEnvelope.Length; i++)
        {
            if (inBandEnvelope[i] > quietCeiling)
            {
                return null;
            }
        }

        // Trial-remove the island and re-read the full-band first arrival; only a large jump convicts.
        var fullBandOptions = new TimeAlignmentAnalysisOptions();
        TimeAlignmentAnalysisResult rawFull = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, fullBandOptions);
        if (!rawFull.IsValid)
        {
            return null;
        }
        var gate = new CrosstalkHeadGate(gateEnd, 0, 0);
        double[] trialCleaned = CleanCrosstalkHead(
            impulseResponse is double[] array ? array : [.. impulseResponse],
            sampleRate,
            gate);
        TimeAlignmentAnalysisResult cleanedFull = TimeAlignmentAnalysis.Analyze(
            trialCleaned, sampleRate, fullBandOptions);
        if (!cleanedFull.IsValid ||
            cleanedFull.FirstArrivalDelayMilliseconds -
            rawFull.FirstArrivalDelayMilliseconds < FirstArrivalJumpMs)
        {
            return null;
        }

        double recordMax = 0;
        int length = Math.Min(impulseResponse.Count, MaxAnalysisSamples);
        for (int i = 0; i < length; i++)
        {
            recordMax = Math.Max(recordMax, Math.Abs(impulseResponse[i]));
        }
        return new CrosstalkHeadGate(
            gateEnd,
            clickIndex * 1000.0 / sampleRate,
            recordMax > 0
                ? 20 * Math.Log10(Math.Max(1e-12, clickPeak / recordMax))
                : 0.0);
    }

    /// <summary>Zeros [0, GateEndSample) and raised-cosine fades the next <see cref="FadeSeconds"/> of a copy.</summary>
    public static Complex[] CleanCrosstalkHead(
        Complex[] impulseResponse,
        int sampleRate,
        CrosstalkHeadGate gate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        var clean = (Complex[])impulseResponse.Clone();
        int end = Math.Min(gate.GateEndSample, clean.Length);
        for (int i = 0; i < end; i++)
        {
            clean[i] = Complex.Zero;
        }
        int fade = Math.Max(1, (int)(sampleRate * FadeSeconds));
        for (int i = 0; i < fade && end + i < clean.Length; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(Math.PI * i / fade);
            clean[end + i] *= w;
        }
        return clean;
    }

    public static double[] CleanCrosstalkHead(
        double[] impulseResponse,
        int sampleRate,
        CrosstalkHeadGate gate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        var clean = (double[])impulseResponse.Clone();
        int end = Math.Min(gate.GateEndSample, clean.Length);
        Array.Clear(clean, 0, end);
        int fade = Math.Max(1, (int)(sampleRate * FadeSeconds));
        for (int i = 0; i < fade && end + i < clean.Length; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(Math.PI * i / fade);
            clean[end + i] *= w;
        }
        return clean;
    }

}
