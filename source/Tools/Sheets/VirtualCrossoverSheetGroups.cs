namespace Resonalyze;

/// <summary>
/// How the tuning sheet groups a project's channels: one section run per zone,
/// in the order a tune is typed into a DSP. Pure rules with no rendering, so
/// the text and PDF sheets cannot come to group the same project differently,
/// and a test can read the grouping without building either.
/// </summary>
internal static class VirtualCrossoverSheetGroups
{
    /// <summary>
    /// The zone order the sheet's sections follow — the order a tune is entered:
    /// the bass foundation first, then the front stage it hands to, then the
    /// groups placed against that stage. Deliberately NOT
    /// <see cref="VirtualCrossoverZones.All"/>, which is the zone SELECTOR's
    /// order (up the spectrum, then outward from the front stage).
    /// </summary>
    public static readonly IReadOnlyList<VirtualCrossoverZone> SectionOrder =
    [
        VirtualCrossoverZone.Sub,
        VirtualCrossoverZone.Front,
        VirtualCrossoverZone.Rear,
        VirtualCrossoverZone.Center
    ];

    /// <summary>
    /// The participating pairs, grouped by zone and ordered for the sheet.
    /// Inside a group the pairs keep their panel order, so the block letters
    /// still read A before B. A pair participates when any side it would print
    /// has a source — the same rule the sheets apply per channel — and a zone
    /// with no participants gets no group. A single-zone project comes back as
    /// its one group, and the sheets print it without any group scaffolding:
    /// the sheet such a project always had.
    /// </summary>
    public static IReadOnlyList<(VirtualCrossoverZone Zone, IReadOnlyList<int> PairIndices)>
        Sections(VirtualCrossoverProjectFile project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var byZone = new Dictionary<VirtualCrossoverZone, List<int>>();
        for (int i = 0; i < project.Pairs.Count; i++)
        {
            if (VirtualCrossoverSheet.SideSections(project.Pairs[i])
                .Any(section => section.Settings.HasSource))
            {
                VirtualCrossoverZone zone = project.Pairs[i].Zone;
                if (!byZone.TryGetValue(zone, out List<int>? indices))
                {
                    indices = [];
                    byZone[zone] = indices;
                }

                indices.Add(i);
            }
        }

        return SectionOrder
            .Where(byZone.ContainsKey)
            .Select(zone =>
                (zone, (IReadOnlyList<int>)byZone[zone]))
            .ToList();
    }
}
