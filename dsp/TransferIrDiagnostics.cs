using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// The contiguous frequency region where a transfer IR actually carries the
/// driver's energy: the 1/6-octave-smoothed magnitude spectrum's span within
/// <see cref="TransferIrDiagnostics.DominantBandThresholdDb"/> of its peak.
/// </summary>
public readonly record struct DominantBand(double LowHz, double HighHz, double PeakHz);

/// <summary>
/// A detected pre-arrival crosstalk artifact and the head gate that removes
/// it: zero everything before <see cref="GateEndSample"/> (with a short fade
/// after it). Burst figures are diagnostics for the UI/log.
/// </summary>
public readonly record struct CrosstalkHeadGate(
    int GateEndSample,
    double BurstTimeMs,
    double BurstPeakDbReMax);

/// <summary>
/// Record-hygiene diagnostics shared by the manual Time Alignment mode and
/// the auto-delay launcher: where the driver's energy actually lives in
/// frequency, and whether the record's head carries a playback-crosstalk
/// click (field evidence: a broadband spike at one fixed sample in every
/// record of a session — an electrical copy of the playback that lands
/// before any physically possible acoustic arrival and, on band-limited
/// low-frequency records, sits within the first-arrival detector's
/// threshold).
/// </summary>
public static class TransferIrDiagnostics
{
    /// <summary>
    /// How far below the smoothed spectrum's peak the dominant band extends.
    /// Field calibration (v3 cabin, 7 records): 20 dB yields driver-shaped
    /// bands on every record (sub 20-127, midbass 33-329, mid 71-6089,
    /// tweeter 1016-20k); 25+ dB lets clean full-range records swallow the
    /// whole audible range and the band loses its meaning.
    /// </summary>
    public const double DominantBandThresholdDb = 20.0;

    // Spectral analysis window: long enough to resolve 1/6 octave at 20 Hz,
    // short enough to keep the FFT cheap; the head artifact this feeds sits
    // in the first milliseconds anyway.
    private const int MaxAnalysisSamples = 65_536;

    // The complement band (where the record has no driver energy) starts
    // half an octave above the dominant band's top, and must span at least
    // one octave to have any detection leverage — full-range records have no
    // complement, and their head clicks measurably do not move the engine
    // (v3: proposals shift ≤ 0.01 ms).
    private const double ComplementGapOctaves = 0.5;
    private const double ComplementMinimumOctaves = 1.0;

    // The click island ends where the complement envelope falls this far
    // below the click's own peak and stays there.
    private const double IslandEndBelowPeakDb = 12.0;
    private const double IslandEndHoldSeconds = 0.00025;

    // The artifact must precede the in-band front by at least this much: a
    // GENUINE early arrival carries the driver's out-of-band content along
    // with the in-band front (same wavefront), so the two bands read the
    // same time; only a non-acoustic event can be complement-early.
    private const double PreFrontGuardSeconds = 0.002;

    // The island must close within this long of the candidate — a click is
    // a millisecond-scale event; anything longer is sound.
    private const double IslandCapSeconds = 0.010;

    // How far below the in-band first-arrival peak the in-band envelope must
    // stay over the whole gated stretch (the gate claims that stretch is
    // pre-sound). Field margins: the click's in-band shadow reads ~-25 dB,
    // a co-onset genuine rise ~-10 dB.
    private const double InBandQuietBeforeGateDb = 15.0;

    // How much later the full-band first arrival must move after the trial
    // removal for the burst to be convicted (safely above the band-to-band
    // dispersion of one wavefront in a cabin, ~1-1.5 ms measured).
    private const double FirstArrivalJumpMs = 2.0;

    private const double FadeSeconds = 0.0004;

    public static DominantBand DetectDominantBand(
        IReadOnlyList<double> impulseResponse,
        int sampleRate,
        double thresholdDb = DominantBandThresholdDb)
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
            for (int i = i1; i <= i2; i++)
            {
                sum += spectrum[i].Magnitude * spectrum[i].Magnitude;
            }
            return 10 * Math.Log10(Math.Max(1e-24, sum / (i2 - i1 + 1)));
        }

        // 1/24-octave log grid over the audible range.
        var gridHz = new List<double>();
        var gridDb = new List<double>();
        for (double hz = 20; hz <= topHz; hz *= Math.Pow(2.0, 1.0 / 24))
        {
            gridHz.Add(hz);
            gridDb.Add(SmoothedDb(hz));
        }

        int peakIndex = 0;
        for (int i = 1; i < gridDb.Count; i++)
        {
            if (gridDb[i] > gridDb[peakIndex])
            {
                peakIndex = i;
            }
        }

        // Expand from the peak, bridging dips narrower than
        // MaxBridgedGapOctaves: an in-cabin cancellation notch is deep but
        // narrow and must not cut the driver's working band in half, while
        // a wide stretch of silence is a real band edge.
        double floorDb = gridDb[peakIndex] - thresholdDb;
        int maxGapSteps = (int)Math.Round(MaxBridgedGapOctaves * 24);
        int solidSteps = (int)Math.Round(MinSolidLandingOctaves * 24);
        // A bridge must LAND on a solid stretch of band (≥ solidSteps
        // consecutive points above the floor): real cancellation notches sit
        // between solid regions, while broadband ripple hovering around the
        // threshold offers only isolated spikes — chaining bridges through
        // those would crawl the band arbitrarily far (measured on the field
        // records: the midbass band tripled before this rule).
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

    // The widest spectral dip the dominant-band expansion steps across
    // (interference notches); anything wider counts as the band's real edge.
    private const double MaxBridgedGapOctaves = 0.5;

    // A bridged gap must land on at least this much contiguous above-floor
    // band on the far side.
    private const double MinSolidLandingOctaves = 1.0 / 6.0;

    /// <summary>
    /// Looks for a playback-crosstalk click in the record's head. The
    /// candidate is the COMPLEMENT band's first arrival (half an octave
    /// above the dominant band's top, where the driver has nothing to say;
    /// sidelobe-rejected, so window pre-ring cannot masquerade as it), and
    /// it must be a short ISLAND far ahead of the in-band front. The island
    /// is judged on its own envelope, never against later complement
    /// content: a click hotter than the driver's out-of-band tail — or one
    /// that is the only complement event at all — is the MORE dangerous
    /// artifact and must not detect worse than a faint one. The verdict is
    /// an experiment rather than a threshold: the island is trial-removed
    /// and the record's full-band first arrival re-read — only a jump well
    /// past one wavefront's band-to-band dispersion convicts. A genuine
    /// early arrival never trips this: its complement energy rises into the
    /// room decay (no island), it sits at the front rather than in the head
    /// (proportionality guard), and removing sound that IS the first
    /// arrival of only one band cannot move the full-band read.
    /// CONTRACT: this is a field-calibrated detector for BAND-LIMITED
    /// records, not a universal crosstalk detector — a full-range record
    /// offers no complement band to test and is always returned untouched
    /// (null), even if its head carries a click (measured inert on the v3
    /// field data: engine proposals move ≤ 0.01 ms). Null likewise
    /// whenever any of the guards is not met.
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

        // The click and the front both live in the record's first second;
        // capping the analysis keeps the per-refresh cost flat and the
        // verdict identical between the panel, the engine and the probes.
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

        // The candidate: the complement's first arrival, and its island end —
        // where the complement envelope falls well below the candidate's
        // peak and stays there, within a short cap. A genuine early front's
        // complement energy rises into the room decay instead of dropping,
        // so no end is found.
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

        // Proportionality guard: the candidate must sit far ahead of the
        // in-band front, and the gate may reach at most half-way from the
        // candidate to it (midpoint, so an artifact that does not sit near
        // sample zero is not penalized). This is what protects genuine
        // co-onset out-of-band content (its island sits AT the front, not in
        // the head) — and it is deliberately not an onset-threshold walk,
        // which the click's own in-band shadow and the window pre-ring ramp
        // both poison (two field-tested failures).
        int guard = (int)(sampleRate * PreFrontGuardSeconds);
        if (clickIndex + guard >= inBand.EnvelopePeakIndex ||
            gateEnd > clickIndex + (inBand.EnvelopePeakIndex - clickIndex) / 2)
        {
            return null;
        }

        // The gate's own claim is "everything before it is pre-sound": the
        // IN-BAND envelope must stay far below its first-arrival peak over
        // the whole gated stretch. This is what refuses a co-onset genuine
        // event (a driver's out-of-band burst travelling WITH a front whose
        // envelope peaks much later) — there the in-band envelope is already
        // rising where the island ends. The click's own in-band shadow sits
        // ~25 dB down on the field records and clears the ceiling.
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

        // The verdict is an experiment, not a threshold: trial-remove the
        // island and re-read the FULL-BAND first arrival. If it jumps later
        // by more than the dispersion tolerance, the record's first arrival
        // WAS the head burst — a disjoint event ahead of all sound, i.e.
        // crosstalk. If the read barely moves, the burst was not driving
        // anything (measured inert on the field data) and the record is left
        // alone.
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

    /// <summary>
    /// Applies a head gate to a copy of the IR: zeros [0, GateEndSample) and
    /// raised-cosine-fades the next <see cref="FadeSeconds"/> worth of
    /// samples. The artifact is removed from the record before any linear
    /// processing, so every downstream band-limited read is clean.
    /// </summary>
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

    /// <summary>
    /// The real-valued twin of the gate above, for callers holding the
    /// transfer IR as samples (the Time Alignment panel).
    /// </summary>
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
