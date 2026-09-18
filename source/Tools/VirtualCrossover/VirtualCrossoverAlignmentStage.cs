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

    /// <summary>False for front-only projects, which take the single-stage path (not a one-stage staged run), so their results match the unstaged engine by construction.</summary>
    public static bool NeedsStaging(IEnumerable<VirtualCrossoverZone> zones) =>
        zones.Any(zone => StageOf(zone) != VirtualCrossoverAlignmentStage.FrontChain);

    /// <summary>The searched front chain and the groups placed against it. A project without rear fill or centre gets
    /// everything in the chain and takes the unstaged path; a rear-only project is its own chain.</summary>
    internal static (List<VirtualCrossoverChannel> Chain, List<VirtualCrossoverChannel> Later)
        Split(IReadOnlyList<VirtualCrossoverChannel> participants)
    {
        List<VirtualCrossoverChannel> chain = [.. participants.Where(channel =>
            StageOf(channel.Pair.Zone) == VirtualCrossoverAlignmentStage.FrontChain)];
        List<VirtualCrossoverChannel> later = [.. participants.Except(chain)];
        return chain.Count == 0 || later.Count == 0
            ? ([.. participants], [])
            : (chain, later);
    }
}
