using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A field of a channel block, as the block names the one the user changed.</summary>
internal enum VirtualCrossoverChannelField
{
    Gain,
    Delay,
    Polarity,
    Mono,
    Zone,
    Mute,
    Bypass,
    ShowRaw,
    ShowProcessed,
    CrossoverKind,
    HighPassCorner,
    HighPassFilter,
    HighPassRipple,
    LowPassCorner,
    LowPassFilter,
    LowPassRipple,
    PhaseRotation
}

/// <summary>What a channel block shows, each value as its field holds it.</summary>
internal sealed record VirtualCrossoverChannelShown(
    double GainDb,
    double DelayMs,
    bool Inverted,
    bool Mono,
    VirtualCrossoverZone Zone,
    bool Muted,
    bool Bypass,
    bool ShowRaw,
    bool ShowProcessed,
    CrossoverKind CrossoverKind,
    CrossoverEdge HighPass,
    CrossoverEdge LowPass,
    double PhaseRotationDegrees);

/// <summary>Writes a block edit into the pair and the side the block shows, the changed field only: a field shows a
/// stored value rounded or clamped, so writing the others back would move what nobody touched.
/// See docs/tech/virtual-dsp-session-file.md#channel-field-ranges.</summary>
internal static class VirtualCrossoverChannelEdit
{
    public static void Write(
        VirtualCrossoverChannelField field,
        VirtualCrossoverChannelShown shown,
        VirtualCrossoverChannelPairSettings pair,
        VirtualCrossoverChannelSettings side)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(side);

        switch (field)
        {
            case VirtualCrossoverChannelField.Gain:
                side.GainDb = shown.GainDb;
                break;
            case VirtualCrossoverChannelField.Delay:
                side.DelayMs = shown.DelayMs;
                break;
            case VirtualCrossoverChannelField.Polarity:
                side.InvertPolarity = shown.Inverted;
                break;
            case VirtualCrossoverChannelField.Mono:
                pair.Mono = shown.Mono;
                break;
            case VirtualCrossoverChannelField.Zone:
                pair.Zone = shown.Zone;
                break;
            case VirtualCrossoverChannelField.Mute:
                pair.Enabled = !shown.Muted;
                break;
            case VirtualCrossoverChannelField.Bypass:
                pair.Bypass = shown.Bypass;
                break;
            case VirtualCrossoverChannelField.ShowRaw:
                pair.ShowRawCurve = shown.ShowRaw;
                break;
            case VirtualCrossoverChannelField.ShowProcessed:
                pair.ShowProcessedCurve = shown.ShowProcessed;
                break;
            case VirtualCrossoverChannelField.CrossoverKind:
                side.CrossoverKind = shown.CrossoverKind;
                break;
            case VirtualCrossoverChannelField.HighPassCorner:
                side.HighPassEdge = side.HighPassEdge with { FrequencyHz = shown.HighPass.FrequencyHz };
                break;
            case VirtualCrossoverChannelField.HighPassFilter:
                side.HighPassEdge = Refilter(side.HighPassEdge, shown.HighPass);
                break;
            case VirtualCrossoverChannelField.HighPassRipple:
                side.HighPassEdge = side.HighPassEdge with { RippleDb = shown.HighPass.RippleDb };
                break;
            case VirtualCrossoverChannelField.LowPassCorner:
                side.LowPassEdge = side.LowPassEdge with { FrequencyHz = shown.LowPass.FrequencyHz };
                break;
            case VirtualCrossoverChannelField.LowPassFilter:
                side.LowPassEdge = Refilter(side.LowPassEdge, shown.LowPass);
                break;
            case VirtualCrossoverChannelField.LowPassRipple:
                side.LowPassEdge = side.LowPassEdge with { RippleDb = shown.LowPass.RippleDb };
                break;
            case VirtualCrossoverChannelField.PhaseRotation:
                side.PhaseRotationDegrees = shown.PhaseRotationDegrees;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    // Family and slope are one choice, the slope list following the family. The stored ripple stays unless the new
    // family cannot build it; the shown ripple always can.
    private static CrossoverEdge Refilter(CrossoverEdge stored, CrossoverEdge shown)
    {
        CrossoverEdge edge = stored with { Family = shown.Family, SlopeDbPerOctave = shown.SlopeDbPerOctave };
        return VirtualCrossoverChannelSettings.HasBuildableRipple(edge) ? edge : edge with { RippleDb = shown.RippleDb };
    }
}
