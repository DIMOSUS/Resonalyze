using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One group of the wizard's session as the fit takes it. Impulse responses are there only for a ranked run.</summary>
internal sealed record AutoSetupGroupPlan(
    VirtualCrossoverAlignmentStage Group,
    IReadOnlyList<int> InitIndices,
    IReadOnlyList<AutoSetupSource> Sources,
    IReadOnlyList<Complex[]>? ImpulseResponses,
    bool IsPrimary);

/// <summary>What the crossover wizard's session asks the search for: each group's sources and options, and the window
/// each junction resolves to. See docs/tech/crossover-auto-setup.md#per-junction-windows.</summary>
internal static class AutoSetupWizardPlan
{
    /// <summary>Slopes the window fields offer. 6 dB/oct protects nothing and is excluded from the search anyway.</summary>
    public static readonly IReadOnlyList<int> SelectableSlopes = CrossoverFilter
        .SupportedSlopes(CrossoverFilterFamily.Butterworth)
        .Where(slope => slope >= 12)
        .ToArray();

    public const decimal FieldMinimumHz = 20m;

    public const decimal FieldMaximumHz = 20_000m;

    public static AutoSetupSource SourceOf(AutoSetupWizardRow row) =>
        new(row.Source.MagnitudeDb, row.Type, row.Source.Coherence, row.Source.Distortion);

    /// <param name="withImpulseResponses">A group of two or more whose members all carry one is ranked on them.</param>
    public static List<AutoSetupGroupPlan> Groups(AutoSetupWizardSession session, bool withImpulseResponses)
    {
        VirtualCrossoverAlignmentStage primary = session.PrimaryGroup();
        var plan = new List<AutoSetupGroupPlan>();
        foreach (VirtualCrossoverAlignmentStage group in session.GroupsInOrder())
        {
            List<AutoSetupWizardRow> members = session.MembersOf(group);
            bool ranked = withImpulseResponses &&
                members.Count > 1 &&
                members.All(row => row.Source.ImpulseResponse is { Length: > 0 });
            plan.Add(new AutoSetupGroupPlan(
                group,
                members.Select(row => row.InitIndex).ToList(),
                members.Select(SourceOf).ToList(),
                ranked ? members.Select(row => row.Source.ImpulseResponse!).ToList() : null,
                group == primary));
        }

        return plan;
    }

    // Sub elevation applies to the primary group only; others keep their balance and are levelled as a whole.
    public static CrossoverAutoSetupOptions OptionsFor(AutoSetupWizardSession session, AutoSetupGroupPlan group) =>
        new(
            session.SelectedFamilies(),
            (double)session.MinCrossoverHz,
            (double)session.MaxCrossoverHz,
            session.IndependentSlopes,
            session.SampleRateHz,
            session.ProcessorSampleRateHz,
            group.IsPrimary && session.SubElevationInitialized ? (double)session.SubElevationDb : null,
            WindowsFor(session, group.Group));

    /// <summary>Each group's options as they stand now, for a run that must not read the session again.</summary>
    public static Dictionary<VirtualCrossoverAlignmentStage, CrossoverAutoSetupOptions> Snapshot(
        AutoSetupWizardSession session, IReadOnlyList<AutoSetupGroupPlan> plan) =>
        plan.ToDictionary(group => group.Group, group => OptionsFor(session, group));

    public static IReadOnlyList<JunctionSearchWindow> WindowsFor(
        AutoSetupWizardSession session, VirtualCrossoverAlignmentStage group) =>
        session.Junctions()
            .Where(junction => junction.Group == group)
            .OrderBy(junction => junction.IndexInGroup)
            .Select(junction => session.EditsOf(junction).ToWindow())
            .ToList();

    /// <summary>The window the search runs each junction on, with the reason for any bound that moved; a junction
    /// whose drivers cannot be crossed at all is left out.</summary>
    public static List<(AutoSetupWizardJunction Junction, JunctionWindowResolution Window)> ResolvedWindows(
        AutoSetupWizardSession session)
    {
        var resolved = new List<(AutoSetupWizardJunction, JunctionWindowResolution)>();
        foreach (VirtualCrossoverAlignmentStage group in session.GroupsInOrder())
        {
            List<AutoSetupWizardRow> members = session.MembersOf(group);
            if (members.Count < 2)
            {
                continue;
            }

            var sources = members.Select(SourceOf).ToList();
            CrossoverAutoSetupOptions options = OptionsFor(session, new AutoSetupGroupPlan(
                group, [], sources, null, group == session.PrimaryGroup()));
            List<AutoSetupWizardJunction> junctions = session.Junctions()
                .Where(junction => junction.Group == group)
                .OrderBy(junction => junction.IndexInGroup)
                .ToList();
            for (int j = 0; j < junctions.Count; j++)
            {
                try
                {
                    resolved.Add((junctions[j], CrossoverAutoSetup.ResolveJunctionWindow(sources, j, options)));
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }
        }

        return resolved;
    }

    /// <summary>A resolved frequency as its field shows it.</summary>
    public static decimal FieldHz(double valueHz) =>
        Math.Clamp((decimal)Math.Round(valueHz), FieldMinimumHz, FieldMaximumHz);

    public static int NearestSlope(int slopeDbPerOctave) =>
        SelectableSlopes.MinBy(slope => Math.Abs(slope - slopeDbPerOctave));
}
