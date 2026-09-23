namespace Resonalyze;

/// <summary>Ranges of the Virtual DSP fields that a restored file or an AI reply can state too: the controls and the
/// checks read them here, so a value the review passed is one the field shows unchanged.</summary>
internal static class VirtualCrossoverLimits
{
    public static readonly NumericFieldRange TargetLevel = new(-120m, 60m, 0);

    // The Auto delay dialog's fields.
    public static readonly NumericFieldRange SceneOffset = new(0m, 5m, 2);
    public static readonly NumericFieldRange NearSideCut = new(0m, 6m, 1);
    public static readonly NumericFieldRange RearFillOffset = new(0m, 30m, 1);

    /// <summary>Precedence-effect start: 10-20 ms is where the rear stops being localized and reads as room.</summary>
    public const double DefaultRearFillOffsetMs = 15.0;
}
