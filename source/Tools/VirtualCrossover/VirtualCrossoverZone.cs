using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Where a channel block sits in the installation — the answer to "what part of
/// the system is this driver", which the crossover corners alone cannot give.
/// </summary>
/// <remarks>
/// A complex car install is not one chain along the spectrum. Front, rear and
/// centre drivers routinely play the SAME band from different places: a rear
/// pair high-passed at 290 Hz overlaps the front midrange and tweeter entirely,
/// and has no crossover junction with either. Ordering such a system by band
/// centre — which is all the tool could do while every block was just "a
/// channel" — invents a handover between a midrange and a rear fill that no
/// filter creates.
/// <para>
/// The zone is deliberately NOT the <see cref="VirtualCrossoverChannelPairSettings.Mono"/>
/// flag in disguise. Mono is a ROUTING fact (one measurement feeds both sides),
/// and the two are independent in the field: a subwoofer pair can be stereo, a
/// two-way centre is two mono blocks, and one install can carry two mono subs in
/// different bands. Merging them into a single list would force a re-typing
/// whenever a mono sub becomes a stereo pair. <see cref="Center"/> is the one
/// zone that implies mono, and the panel enforces that rather than encoding it
/// here.
/// </para>
/// <para>
/// <see cref="Sub"/> is meant as a zone for DISPLAY and for the tuning sheet:
/// where alignment and the crossover wizard are concerned the subwoofers are the
/// bottom members of the front chain, which is how they are tuned in practice —
/// the two subs of the reference install (below 50 Hz and 50–110 Hz) form real
/// junctions with each other and with the midbass.
/// </para>
/// <para>
/// Nothing reads the zone yet. It is persisted, shown and migrated here so the
/// fact exists; the grouped views, the staged Auto delay and the per-group
/// crossover wizard that consume it land in their own changes. Until then the
/// sum, the metric and the alignment still order every channel by band centre.
/// </para>
/// </remarks>
public enum VirtualCrossoverZone
{
    Front,
    Rear,
    Center,
    Sub
}

/// <summary>
/// Naming and classification rules for <see cref="VirtualCrossoverZone"/>. Pure
/// and UI-free so the schema migration and the tests can read them without a
/// panel.
/// </summary>
public static class VirtualCrossoverZones
{
    /// <summary>
    /// The zones in the order the UI lists them and the tuning sheet sections
    /// follow — up the spectrum, then outward from the front stage.
    /// </summary>
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

    /// <summary>
    /// Whether the zone is one physical driver serving both sides by nature. Only
    /// a centre is: it plays a signal derived from L and R, so it has no side.
    /// A subwoofer is USUALLY mono but legitimately stereo, which is why this
    /// asks about the zone rather than deciding it.
    /// </summary>
    public static bool RequiresMono(VirtualCrossoverZone zone) =>
        zone == VirtualCrossoverZone.Center;

    /// <summary>
    /// The zone a pre-v9 project's block most likely occupied, read from the two
    /// facts such a file recorded: whether the block was mono, and what it played.
    /// </summary>
    /// <remarks>
    /// Before zones existed, "mono" carried the whole meaning — the panel's own
    /// documentation called a mono pair "a shared subwoofer", because that is what
    /// users had. So a stereo pair becomes <see cref="VirtualCrossoverZone.Front"/>
    /// (a rear pair reads identically in a v8 file and has to be re-pointed by
    /// hand), and a mono block splits on its filter: a HIGH-PASS mono block plays
    /// up the spectrum, which no subwoofer does — that is a centre. Everything else
    /// mono (low-pass, band-pass, or unfiltered) keeps the historical reading.
    /// <para>
    /// The guess costs nothing when it is wrong: the zone is additive, so a
    /// mis-guessed block still opens with every delay, filter, PEQ band and gain it
    /// was saved with, and the user re-points one combo box.
    /// </para>
    /// </remarks>
    public static VirtualCrossoverZone GuessForLegacyPair(
        bool mono,
        CrossoverKind monoSideKind) =>
        !mono
            ? VirtualCrossoverZone.Front
            : monoSideKind == CrossoverKind.HighPass
                ? VirtualCrossoverZone.Center
                : VirtualCrossoverZone.Sub;
}
