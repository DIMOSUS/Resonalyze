namespace Resonalyze.Dsp;

/// <summary>What a post-check that predicts Auto delay searches at one junction whose chains render with their saved signs:
/// the upper channel's flip that gives the relation Auto delay forces, or null where its search decides.
/// See docs/tech/auto-alignment.md#expected-polarity.</summary>
public static class PostCheckPolarity
{
    public static bool? ForcedFlip(DspChannelChain lower, DspChannelChain upper, int processorSampleRate)
    {
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(upper);
        return AutoAlignmentEngine.SettledRelativeInversion(
                lower.LowPassEdge, upper.HighPassEdge, processorSampleRate) is bool settled
            ? settled ^ lower.InvertPolarity ^ upper.InvertPolarity
            : null;
    }

    /// <summary>One flip for several sides read at one shift: their common flip, or none where they differ.</summary>
    public static bool? Shared(IReadOnlyList<bool?> flips)
    {
        ArgumentNullException.ThrowIfNull(flips);
        return flips.Count > 0 && flips.All(flip => flip == flips[0]) ? flips[0] : null;
    }
}
