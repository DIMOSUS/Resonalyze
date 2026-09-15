namespace Resonalyze;

/// <summary>Shared zone grouping, so the text and PDF sheets cannot group a project differently.</summary>
internal static class VirtualCrossoverSheetGroups
{
    /// <summary>Tune-entry order; deliberately not <see cref="VirtualCrossoverZones.All"/> (the selector's order).</summary>
    public static readonly IReadOnlyList<VirtualCrossoverZone> SectionOrder =
    [
        VirtualCrossoverZone.Sub,
        VirtualCrossoverZone.Front,
        VirtualCrossoverZone.Rear,
        VirtualCrossoverZone.Center
    ];

    /// <summary>Pairs keep panel order within a zone; a pair participates when any printed side has a source.</summary>
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
