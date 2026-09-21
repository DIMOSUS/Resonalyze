using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The band a read is taken in: Full, the Manual numbers, or the Auto band detected on the records.</summary>
internal static class TimeAlignmentBand
{
    public const double AutoBandFadeOctaves = 0.5;

    public static TimeAlignmentAnalysisOptions CreateAnalysisOptions(
        TimeAlignmentRequest request,
        TimeAlignmentAnalysisSource source,
        TimeAlignmentAnalysisSource? compareSource,
        out DominantBand? autoBand,
        out bool autoBandShared)
    {
        double centerHz = request.BandpassCenterHz;
        double passOctaves = request.BandpassPassOctaves;
        double fadeOctaves = request.BandpassFadeOctaves;
        autoBand = null;
        autoBandShared = false;
        if (request.BandMode == TimeAlignmentBandMode.AutoBand)
        {
            DominantBand band = DetectDominantBand(source);
            if (compareSource is { } compare &&
                TryDetectDominantBand(compare, out DominantBand compareBand))
            {
                (band, autoBandShared) = SharedBand(band, compareBand);
            }

            autoBand = band;
            centerHz = Math.Sqrt(band.LowHz * band.HighHz);
            passOctaves = Math.Log2(band.HighHz / band.LowHz);
            fadeOctaves = AutoBandFadeOctaves;
        }

        return new TimeAlignmentAnalysisOptions
        {
            UseBandpassWindow = request.BandMode != TimeAlignmentBandMode.FullBand,
            BandpassCenterHz = centerHz,
            BandpassPassOctaves = passOctaves,
            BandpassFadeOctaves = fadeOctaves,
            FirstPeakThresholdBelowMaxDb = request.FirstPeakThresholdBelowMaxDb,
            FirstPeakMinimumSnrDb = request.FirstPeakMinimumSnrDb,
            PeakSearchWindowMilliseconds = request.PeakSearchWindowMilliseconds,
            // Positions are delays against another arrival: a peak past halfway is a negative lead.
            WrapPeakPositions = true
        };
    }

    private static DominantBand DetectDominantBand(TimeAlignmentAnalysisSource source) =>
        TransferIrDiagnostics.DetectDominantBand(
            source.TransferImpulseResponse,
            source.SampleRate,
            coherence: source.TransferCoherence);

    // Compare's detection failure stays Compare's: the band falls back to Main's (label drops "shared"). Main's failure propagates.
    public static bool TryDetectDominantBand(
        TimeAlignmentAnalysisSource source,
        out DominantBand band)
    {
        try
        {
            band = DetectDominantBand(source);
            return true;
        }
        catch (InvalidOperationException)
        {
            band = default;
            return false;
        }
    }

    // Overlap of both dominant bands (symmetric, so swapping Main/Compare gives the same delta); too little overlap keeps Main's band.
    public static (DominantBand Band, bool Shared) SharedBand(
        DominantBand main, DominantBand compare)
    {
        double low = Math.Max(main.LowHz, compare.LowHz);
        double high = Math.Min(main.HighHz, compare.HighHz);
        return high < low * VirtualCrossoverAnalysis.MinimumArrivalBandRatio
            ? (main, false)
            : (new DominantBand(low, high, Math.Clamp(main.PeakHz, low, high)), true);
    }

    /// <summary>The Auto caption names the last finished read's band, not one the controls are waiting for.</summary>
    public static string AutoBandCaption(TimeAlignmentSession session) =>
        session.Options.BandMode != TimeAlignmentBandMode.AutoBand
            ? "-"
            : session.AutoBand is { } band
                ? $"detected: {band.LowHz:0}-{band.HighHz:0} Hz" +
                    (session.AutoBandShared ? " (shared with Compare)" : string.Empty)
                : "detected: waiting for a record";
}
