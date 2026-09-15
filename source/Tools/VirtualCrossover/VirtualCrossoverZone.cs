using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Where a block sits in the installation; independent of the Mono routing flag. See docs/tech/junction-phase-and-group-placement.md#zones-and-alignment-stages.</summary>
public enum VirtualCrossoverZone
{
    Front,
    Rear,
    Center,
    Sub
}

public static class VirtualCrossoverZones
{
    public static readonly IReadOnlyList<VirtualCrossoverZone> All =
    [
        VirtualCrossoverZone.Front,
        VirtualCrossoverZone.Rear,
        VirtualCrossoverZone.Center,
        VirtualCrossoverZone.Sub
    ];

    public static string DisplayName(VirtualCrossoverZone zone) => zone switch
    {
        VirtualCrossoverZone.Rear => "Rear",
        VirtualCrossoverZone.Center => "Center",
        VirtualCrossoverZone.Sub => "Sub",
        _ => "Front"
    };

    /// <summary>Only a centre is mono by nature; a subwoofer may legitimately be stereo.</summary>
    public static bool RequiresMono(VirtualCrossoverZone zone) =>
        zone == VirtualCrossoverZone.Center;

    /// <summary>Best guess for a pre-v9 block: stereo = Front, high-pass mono = Center, other mono = Sub. Harmless when wrong.</summary>
    public static VirtualCrossoverZone GuessForLegacyPair(
        bool mono,
        CrossoverKind monoSideKind) =>
        !mono
            ? VirtualCrossoverZone.Front
            : monoSideKind == CrossoverKind.HighPass
                ? VirtualCrossoverZone.Center
                : VirtualCrossoverZone.Sub;
}
