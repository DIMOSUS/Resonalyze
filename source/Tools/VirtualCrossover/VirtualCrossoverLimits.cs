using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Ranges of the Virtual DSP fields that a restored file or an AI reply can state too: the controls and the
/// checks read them here, so a value the review passed is one the field shows unchanged.</summary>
internal static class VirtualCrossoverLimits
{
    public static readonly NumericFieldRange TargetLevel = new(-120m, 60m, 1);

    // A channel block's fields. See docs/tech/virtual-dsp-session-file.md#channel-field-ranges.
    public static readonly NumericFieldRange ChannelGain = new(-60m, 20m, 1);
    public static readonly NumericFieldRange ChannelDelay = new(0m, 100m, 2);
    public static readonly NumericFieldRange CrossoverCorner = new(10m, 24_000m, 0);
    public static readonly NumericFieldRange ChebyshevRipple =
        new(0.1m, (decimal)CrossoverFilter.MaximumChebyshevRippleDb, 1);
    public static readonly NumericFieldRange PhaseRotation = new(0m, (decimal)PhaseRotationControl.MaximumDegrees, 3);

    // The Auto delay dialog's fields.
    public static readonly NumericFieldRange SceneOffset = new(0m, 5m, 2);
    public static readonly NumericFieldRange NearSideCut = new(0m, 6m, 1);
    public static readonly NumericFieldRange RearFillOffset = new(0m, 30m, 1);

    /// <summary>Precedence-effect start: 10-20 ms is where the rear stops being localized and reads as room.</summary>
    public const double DefaultRearFillOffsetMs = 15.0;
}
