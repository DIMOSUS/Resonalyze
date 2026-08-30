namespace Resonalyze;

/// <summary>
/// The order Auto delay settles a complex installation in. Each stage is
/// finished before the next begins, and no later stage may re-tune an earlier
/// one internally — it may only slide it as a rigid body.
/// </summary>
/// <remarks>
/// The alignment engine walks ONE chain along the spectrum, pairing neighbours
/// and settling each junction against the last. That is the right shape for a
/// crossover chain and the wrong shape for a car: a rear fill and a centre play
/// the same band as the front midrange and tweeter from other places, with no
/// filter handing anything between them. Walked as one chain they produce
/// junctions that do not exist — on the reference installation, a front
/// midrange handing over to a rear fill at its own low-pass corner.
/// <para>
/// Staging is what replaces that. The front stage and the subwoofers under it
/// ARE one chain and keep the engine unchanged. What the later groups need is
/// not a junction search but a single number each: how far behind the front
/// they should arrive. So they are not searched at all — they are COMPUTED
/// against a front stage that is already settled, which is also why the stages
/// cannot fight each other.
/// </para>
/// </remarks>
public enum VirtualCrossoverAlignmentStage
{
    /// <summary>
    /// The front stage with the subwoofers: the crossover chain, settled by the
    /// existing engine exactly as it always was.
    /// </summary>
    FrontChain,

    /// <summary>
    /// The rear fill: its own L/R pair settled between itself, then the group
    /// placed as a whole against the front.
    /// </summary>
    Rear,

    /// <summary>
    /// The centre: one driver, placed between the two front sides.
    /// </summary>
    Center
}

/// <summary>
/// Which zones each alignment stage owns, and what a stage is allowed to do to
/// the ones before it. Pure and UI-free so the rules are testable without an
/// engine run.
/// </summary>
public static class VirtualCrossoverAlignmentStages
{
    /// <summary>The stages in the order they run.</summary>
    public static readonly IReadOnlyList<VirtualCrossoverAlignmentStage> InOrder =
    [
        VirtualCrossoverAlignmentStage.FrontChain,
        VirtualCrossoverAlignmentStage.Rear,
        VirtualCrossoverAlignmentStage.Center
    ];

    /// <summary>What a stage is called wherever one is named to the user.</summary>
    public static string DisplayName(VirtualCrossoverAlignmentStage stage) =>
        stage switch
        {
            VirtualCrossoverAlignmentStage.Rear => "Rear fill",
            VirtualCrossoverAlignmentStage.Center => "Center",
            _ => "Front stage and subs"
        };

    /// <summary>
    /// The stage a block belongs to. The subwoofers join the FRONT chain rather
    /// than forming one of their own: that is where their junctions are — on the
    /// reference car the two subs cross each other and the lower one crosses the
    /// midbass — and it is how they are tuned by hand.
    /// </summary>
    public static VirtualCrossoverAlignmentStage StageOf(VirtualCrossoverZone zone) =>
        zone switch
        {
            VirtualCrossoverZone.Rear => VirtualCrossoverAlignmentStage.Rear,
            VirtualCrossoverZone.Center => VirtualCrossoverAlignmentStage.Center,
            _ => VirtualCrossoverAlignmentStage.FrontChain
        };

    /// <summary>
    /// Whether the stage settles its members by SEARCHING their junctions — the
    /// chain walk the engine has always done — or by computing one offset for
    /// the group against a front stage that is already settled.
    /// </summary>
    /// <remarks>
    /// Only the front chain searches. A rear fill has no junction with the front
    /// to search for, and a centre has no junction with anything: what they need
    /// is a placement, and a placement computed from a settled reference cannot
    /// disagree with the thing it was computed from. That is what makes the
    /// stages one-way — the reason no later stage can pull an earlier one out of
    /// tune, other than by sliding all of it at once.
    /// </remarks>
    public static bool SearchesJunctions(VirtualCrossoverAlignmentStage stage) =>
        stage == VirtualCrossoverAlignmentStage.FrontChain;

    /// <summary>
    /// Whether a project needs staging at all: false when every block is in the
    /// front chain, which is every project written before zones existed and
    /// every front-only car.
    /// </summary>
    /// <remarks>
    /// The distinction earns its place as the compatibility guarantee. Such a
    /// project takes the single-stage path, which IS the old code — not a
    /// staged run that happens to have one stage — so the session battery's
    /// results are unchanged by construction rather than by measurement.
    /// </remarks>
    public static bool NeedsStaging(IEnumerable<VirtualCrossoverZone> zones) =>
        zones.Any(zone => StageOf(zone) != VirtualCrossoverAlignmentStage.FrontChain);
}
