namespace Resonalyze;

/// <summary>Whether a waterfall has enough slices to draw its surface, and what the plot says when it has not.</summary>
internal static class WaterfallSliceVerdict
{
    /// <summary><see cref="WaterfallSeries"/> draws the surface from this many slices up.</summary>
    public const int MinimumSlices = 8;

    /// <summary>Burst decay has one slice per frequency, so its count follows the window and the smoothing.</summary>
    public static string? Explain(WaterfallMode mode, int slices) =>
        slices >= MinimumSlices
            ? null
            : mode == WaterfallMode.BurstDecay
                ? $"Only {slices} frequencies fit this window and smoothing; burst decay draws from " +
                    $"{MinimumSlices}. Lengthen the window or narrow the smoothing."
                : $"Only {slices} slices; the waterfall draws from {MinimumSlices}. Raise Slices.";
}
