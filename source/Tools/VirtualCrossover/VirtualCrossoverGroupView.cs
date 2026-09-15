namespace Resonalyze;

/// <summary>Which coherent subset of a complex installation the main plot describes. See docs/tech/junction-phase-and-group-placement.md#group-views.</summary>
public enum VirtualCrossoverGroupView
{
    FrontAndSub,

    RearAndSub,

    /// <summary>Front beside the centre: compared, never summed.</summary>
    FrontAndCenter,

    GroupsCompared,

    Everything
}

public static class VirtualCrossoverGroupViews
{
    public static readonly IReadOnlyList<VirtualCrossoverGroupView> All =
    [
        VirtualCrossoverGroupView.FrontAndSub,
        VirtualCrossoverGroupView.RearAndSub,
        VirtualCrossoverGroupView.FrontAndCenter,
        VirtualCrossoverGroupView.GroupsCompared,
        VirtualCrossoverGroupView.Everything
    ];

    public static string DisplayName(VirtualCrossoverGroupView view) => view switch
    {
        VirtualCrossoverGroupView.RearAndSub => "Rear + Sub",
        VirtualCrossoverGroupView.FrontAndCenter => "Front + Center",
        VirtualCrossoverGroupView.GroupsCompared => "Groups",
        VirtualCrossoverGroupView.Everything => "Everything",
        _ => "Front + Sub"
    };

    /// <summary>Drawn is not summed: the centre is drawn beside the front but sums with nothing.</summary>
    public static bool IsShown(VirtualCrossoverGroupView view, VirtualCrossoverZone zone) =>
        view switch
        {
            VirtualCrossoverGroupView.FrontAndSub =>
                zone is VirtualCrossoverZone.Front or VirtualCrossoverZone.Sub,
            VirtualCrossoverGroupView.RearAndSub =>
                zone is VirtualCrossoverZone.Rear or VirtualCrossoverZone.Sub,
            VirtualCrossoverGroupView.FrontAndCenter =>
                zone is VirtualCrossoverZone.Front or VirtualCrossoverZone.Center,
            _ => true
        };

    public static bool DrawsChannelCurves(VirtualCrossoverGroupView view) =>
        view != VirtualCrossoverGroupView.GroupsCompared;

    public static bool DrawsGroupSums(VirtualCrossoverGroupView view) =>
        view == VirtualCrossoverGroupView.GroupsCompared;

    /// <summary>A centre never enters a sum: its share of the programme is unknowable.</summary>
    public static bool ParticipatesInTotalSum(
        VirtualCrossoverGroupView view,
        VirtualCrossoverZone zone) =>
        IsShown(view, zone) &&
        zone != VirtualCrossoverZone.Center &&
        !DrawsGroupSums(view);

    /// <summary>Zone whose crossover chain the loss read-out describes; null when the view spans groups (front vs rear combs, no crossover to repair).</summary>
    public static VirtualCrossoverZone? LossChainZone(VirtualCrossoverGroupView view) =>
        view switch
        {
            VirtualCrossoverGroupView.FrontAndSub => VirtualCrossoverZone.Front,
            VirtualCrossoverGroupView.RearAndSub => VirtualCrossoverZone.Rear,
            // Front chain without the subs: this view does not draw them.
            VirtualCrossoverGroupView.FrontAndCenter => VirtualCrossoverZone.Front,
            _ => null
        };

    public static IReadOnlyList<VirtualCrossoverZone> ComparedAgainstFront(
        VirtualCrossoverGroupView view) =>
        view switch
        {
            VirtualCrossoverGroupView.FrontAndCenter => [VirtualCrossoverZone.Center],
            VirtualCrossoverGroupView.GroupsCompared or VirtualCrossoverGroupView.Everything =>
                [VirtualCrossoverZone.Rear, VirtualCrossoverZone.Center],
            _ => []
        };
}
