using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A block's acoustic goal button: the goal as stated for the edges the channel runs; a goal for an edge it
/// does not run is not shown as stated, and the tooltip names it as kept.</summary>
internal sealed record VirtualCrossoverChannelGoalReadout(string Text, Color Color, string Tooltip)
{
    public static VirtualCrossoverChannelGoalReadout Read(
        JunctionAcousticTarget? highPass,
        JunctionAcousticTarget? lowPass,
        bool highPassRuns,
        bool lowPassRuns)
    {
        string? high = highPassRuns ? Describe(highPass) : null;
        string? low = lowPassRuns ? Describe(lowPass) : null;
        string text = (high, low) switch
        {
            (null, null) => "—",
            (not null, null) => high!,
            (null, not null) => low!,
            _ => high == low ? high! : $"{high}/{low}"
        };
        return new VirtualCrossoverChannelGoalReadout(
            text,
            high == null && low == null ? UiPalette.TextDisabled : UiPalette.TextPrimary,
            TooltipText(
                highPassRuns ? highPass : null,
                lowPassRuns ? lowPass : null,
                highPassRuns ? null : highPass,
                lowPassRuns ? null : lowPass));

        static string? Describe(JunctionAcousticTarget? goal) => goal is { } asked
            ? FamilyShort(asked.Family) + asked.SlopeDbPerOctave
            : null;
    }

    private static string FamilyShort(CrossoverFilterFamily family) => family switch
    {
        CrossoverFilterFamily.LinkwitzRiley => "LR",
        CrossoverFilterFamily.Butterworth => "BW",
        CrossoverFilterFamily.Bessel => "BE",
        _ => "CH"
    };

    private static string TooltipText(
        JunctionAcousticTarget? highPass,
        JunctionAcousticTarget? lowPass,
        JunctionAcousticTarget? keptHighPass,
        JunctionAcousticTarget? keptLowPass)
    {
        string stated = highPass == null && lowPass == null
            ? "Nothing stated: the EQ target follows the electrical filter."
            : "Stated" +
              (highPass is { } high ? $", HP {FamilyShort(high.Family)}{high.SlopeDbPerOctave}" : string.Empty) +
              (lowPass is { } low ? $", LP {FamilyShort(low.Family)}{low.SlopeDbPerOctave}" : string.Empty) +
              ": Auto Tune aims at THIS instead of the filter.";
        string last = keptHighPass == null && keptLowPass == null
            ? "Click to read or edit it; the corner always follows the filter's."
            : "Kept for an edge this channel does not run:" +
              (keptHighPass is { } keptHigh
                  ? $" HP {FamilyShort(keptHigh.Family)}{keptHigh.SlopeDbPerOctave}"
                  : string.Empty) +
              (keptLowPass is { } keptLow
                  ? $" LP {FamilyShort(keptLow.Family)}{keptLow.SlopeDbPerOctave}"
                  : string.Empty) +
              ", unused until it does.";
        return "The ACOUSTIC crossover you want here — driver and filter together," + "\r\n" +
            "which is steeper than the filter alone by the driver's own fall." + "\r\n" +
            stated + "\r\n" +
            last;
    }
}
