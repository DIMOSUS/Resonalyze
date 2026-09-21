using Resonalyze.Dsp;

namespace Resonalyze;

internal enum TimeAlignmentDelayRow
{
    FirstArrival,
    StrongestPeak,
    EnergyOnset
}

/// <summary>Which delay row the report recommends aligning from, if any.</summary>
internal static class TimeAlignmentRecommendation
{
    public static string RowLabel(TimeAlignmentDelayRow row) => row switch
    {
        TimeAlignmentDelayRow.FirstArrival => DelayTableText.FirstArrivalLabel,
        TimeAlignmentDelayRow.StrongestPeak => DelayTableText.StrongestPeakLabel,
        _ => DelayTableText.EnergyOnsetLabel
    };

    // First arrival unless disqualified; never the strongest peak (mode/reflection) nor the energy onset (partly response shape between unrelated drivers).
    // See docs/tech/junction-phase-and-group-placement.md#time-alignment-panel.
    public static TimeAlignmentDelayRow? RecommendedRow(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? honestyProbe,
        TimeAlignmentBandMode bandMode,
        bool crosstalkDetected) =>
        IsArrivalRecommendable(result, honestyProbe, bandMode, crosstalkDetected)
            ? TimeAlignmentDelayRow.FirstArrival
            : null;

    // Gate so "Use First Arrival" never prints beside a verdict that disqualified it (modal latch, near noise, full-band read with crosstalk).
    public static bool IsArrivalRecommendable(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? honestyProbe,
        TimeAlignmentBandMode bandMode,
        bool crosstalkDetected) =>
        result.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb &&
        honestyProbe?.Certificate != AutoAlignmentEngine.ArrivalCertificate.Latched &&
        !(bandMode == TimeAlignmentBandMode.FullBand && crosstalkDetected);
}
