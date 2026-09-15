namespace Resonalyze;

/// <summary>Auto delay stage order; a later stage may only slide an earlier one rigidly. See docs/tech/junction-phase-and-group-placement.md#zones-and-alignment-stages.</summary>
public enum VirtualCrossoverAlignmentStage
{
    FrontChain,

    Rear,

    Center
}

public static class VirtualCrossoverAlignmentStages
{
    public static readonly IReadOnlyList<VirtualCrossoverAlignmentStage> InOrder =
    [
        VirtualCrossoverAlignmentStage.FrontChain,
        VirtualCrossoverAlignmentStage.Rear,
        VirtualCrossoverAlignmentStage.Center
    ];

    public static string DisplayName(VirtualCrossoverAlignmentStage stage) =>
        stage switch
        {
            VirtualCrossoverAlignmentStage.Rear => "Rear fill",
            VirtualCrossoverAlignmentStage.Center => "Center",
            _ => "Front stage and subs"
        };

    /// <summary>Subwoofers join the FRONT chain: that is where their junctions are.</summary>
    public static VirtualCrossoverAlignmentStage StageOf(VirtualCrossoverZone zone) =>
        zone switch
        {
            VirtualCrossoverZone.Rear => VirtualCrossoverAlignmentStage.Rear,
            VirtualCrossoverZone.Center => VirtualCrossoverAlignmentStage.Center,
            _ => VirtualCrossoverAlignmentStage.FrontChain
        };

    public static bool SearchesJunctions(VirtualCrossoverAlignmentStage stage) =>
        stage == VirtualCrossoverAlignmentStage.FrontChain;

    /// <summary>False for front-only projects, which take the old single-stage path unchanged by construction.</summary>
    public static bool NeedsStaging(IEnumerable<VirtualCrossoverZone> zones) =>
        zones.Any(zone => StageOf(zone) != VirtualCrossoverAlignmentStage.FrontChain);
}
