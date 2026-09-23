namespace Resonalyze;

/// <summary>Ranges of the Virtual DSP fields that a restored file or an AI reply can state too: the controls and the
/// checks read them here, so a value the review passed is one the field shows unchanged.</summary>
internal static class VirtualCrossoverLimits
{
    public static readonly NumericFieldRange TargetLevel = new(-120m, 60m, 0);
}
