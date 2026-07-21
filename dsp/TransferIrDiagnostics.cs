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

        double floorDb = gridDb[peakIndex] - thresholdDb;
        int low = peakIndex;
        int high = peakIndex;
        while (low > 0 && gridDb[low - 1] >= floorDb)
        {
            low--;
        }
        while (high < gridDb.Count - 1 && gridDb[high + 1] >= floorDb)
        {
            high++;
        }

        return new DominantBand(gridHz[low], gridHz[high], gridHz[peakIndex]);
    }

    /// <summary>
    /// Looks for a playback-crosstalk click in the record's head, defined by
    /// physics rather than thresholds on raw samples: in the COMPLEMENT band
    /// (half an octave above the dominant band's top, where the driver has
    /// nothing to say) the first arrival must be a valley-separated island
    /// (<see cref="TimeAlignmentAnalysisResult.StrongestPeakIsSeparateArrival"/> —
    /// the same disjoint-event test the panel uses, sidelobe-rejected so
    /// window pre-ring cannot masquerade as it) that PRECEDES the in-band
    /// front. A genuine early arrival carries its out-of-band content along
    /// with the in-band wavefront, so it reads the same time in both bands
    /// and never trips this. Returns null when the record is full-range (no
    /// complement to test — measured harmless on the v3 field data: engine
    /// proposals move ≤ 0.01 ms), when the head is clean, or when the island
    /// cannot be bounded safely away from the real sound.
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
        if (!complement.IsValid || !complement.StrongestPeakIsSeparateArrival)
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

        int guard = (int)(sampleRate * PreFrontGuardSeconds);
        int clickIndex = complement.EnvelopePeakIndex;
        if (clickIndex + guard >= inBand.EnvelopePeakIndex ||
            clickIndex + guard >= complement.StrongestEnvelopePeakIndex)
        {
            return null;
        }

        // The island's end: where the complement envelope falls well below
        // the click's peak and stays there.
        double[] envelope = complement.EnvelopeSamples;
        double clickPeak = envelope[clickIndex];
        double islandFloor = clickPeak * Math.Pow(10, -IslandEndBelowPeakDb / 20);
        int hold = Math.Max(1, (int)(sampleRate * IslandEndHoldSeconds));
        int islandEnd = -1;
        int below = 0;
        for (int i = clickIndex; i < complement.StrongestEnvelopePeakIndex; i++)
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
        if (gateEnd + guard >= inBand.EnvelopePeakIndex ||
            gateEnd + guard >= complement.StrongestEnvelopePeakIndex)
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

}
