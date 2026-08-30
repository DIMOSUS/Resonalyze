namespace Resonalyze;

/// <summary>
/// Which part of a complex installation the main plot is about. A car with a
/// rear fill and a centre has more drivers playing one band than a single set
/// of curves can say anything about: the front three-way, the rear pair and the
/// centre all cover 290 Hz upward from different places, and drawn together
/// they are seven overlapping traces whose sum describes no listening position.
/// The view picks a subset that IS a coherent question.
/// </summary>
public enum VirtualCrossoverGroupView
{
    /// <summary>
    /// The front stage with the subwoofers under it — the crossover chain, and
    /// what every project was before zones existed. The default, and the only
    /// view a front-only install ever needs.
    /// </summary>
    FrontAndSub,

    /// <summary>The rear fill with the subwoofers under it.</summary>
    RearAndSub,

    /// <summary>
    /// The front stage beside the centre. Drawn together and compared, never
    /// summed — see <see cref="VirtualCrossoverGroupViews.ParticipatesInSum"/>.
    /// </summary>
    FrontAndCenter,

    /// <summary>
    /// Each group as one line — the front sum, the rear sum, the centre — with
    /// no per-driver curves. The view for setting the rear fill's offset and
    /// level against the front, which is a relation between groups rather than
    /// anything inside one.
    /// </summary>
    GroupsCompared,

    /// <summary>
    /// Every driver, and the sum of everything that legitimately sums. The
    /// honest "what arrives at the seat" picture, ugly by nature.
    /// </summary>
    Everything
}

/// <summary>
/// Which zones each view draws and sums. Pure and UI-free: the rules decide
/// what the plot, the sum, the loss curve and the metric read-out all describe,
/// so they are worth stating once and testing rather than spelling out at each
/// of those four places.
/// </summary>
public static class VirtualCrossoverGroupViews
{
    /// <summary>The views in the order the selector lists them.</summary>
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

    /// <summary>
    /// Whether a block in this zone is DRAWN in this view. Drawn is not the same
    /// as summed: the centre appears beside the front stage so the two can be
    /// compared, while contributing to no sum.
    /// </summary>
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

    /// <summary>
    /// Whether the view draws one curve per DRIVER. False only for
    /// <see cref="VirtualCrossoverGroupView.GroupsCompared"/>, which is about the
    /// relation between groups and would bury it under a dozen traces.
    /// </summary>
    public static bool DrawsChannelCurves(VirtualCrossoverGroupView view) =>
        view != VirtualCrossoverGroupView.GroupsCompared;

    /// <summary>
    /// Whether the view draws one summed line per GROUP instead of a single total.
    /// </summary>
    public static bool DrawsGroupSums(VirtualCrossoverGroupView view) =>
        view == VirtualCrossoverGroupView.GroupsCompared;

    /// <summary>
    /// Whether a shown block enters the view's single total sum. False throughout
    /// <see cref="VirtualCrossoverGroupView.GroupsCompared"/>, which has no total
    /// — it sums each group separately.
    /// </summary>
    /// <remarks>
    /// A centre never enters a sum, in any view. It plays a signal synthesised
    /// from L and R — the correlated content pulled out of both — so how much of
    /// the music reaches it is a property of the programme, not of the tune. Its
    /// measured response is an honest acoustic path, but adding that path to the
    /// front stage's would state a division of signal between them that no
    /// measurement can know and that changes from track to track. The centre is
    /// drawn to be compared, and judged by its arrival and its level.
    /// </remarks>
    public static bool ParticipatesInTotalSum(
        VirtualCrossoverGroupView view,
        VirtualCrossoverZone zone) =>
        IsShown(view, zone) &&
        zone != VirtualCrossoverZone.Center &&
        !DrawsGroupSums(view);

    /// <summary>
    /// The zone whose crossover chain the view's summation-loss read-out
    /// describes, or null when the view spans more than one and the read-out has
    /// to stay silent about loss. The subwoofers belong to whichever stage is on
    /// screen with them — they are the bottom of that chain, and the junction
    /// between them is a real one.
    /// </summary>
    /// <remarks>
    /// Summation loss measures cancellation at a CROSSOVER: two drivers handing
    /// one band to each other, where the dip is real and the delay that removes
    /// it is the tune. Front against rear is not that. They play the same band
    /// from opposite ends of the cabin with no filter between them, so their
    /// complex sum combs however well either is tuned — quoting that comb as a
    /// loss would report several decibels of damage that nothing can repair, on a
    /// system that is correct. What those views report instead is the pair of
    /// numbers a tuner actually sets between groups: the arrival difference and
    /// the level difference.
    /// </remarks>
    public static VirtualCrossoverZone? LossChainZone(VirtualCrossoverGroupView view) =>
        view switch
        {
            VirtualCrossoverGroupView.FrontAndSub => VirtualCrossoverZone.Front,
            VirtualCrossoverGroupView.RearAndSub => VirtualCrossoverZone.Rear,
            // The centre is not in the sum, so the loss still describes the front
            // chain alone — the same number this view's Front + Sub sibling shows.
            VirtualCrossoverGroupView.FrontAndCenter => VirtualCrossoverZone.Front,
            _ => null
        };

    /// <summary>
    /// The zones this view compares ACROSS, for which it reports an arrival and a
    /// level difference against the front instead of a summation loss. Empty when
    /// the view describes one group.
    /// </summary>
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
