namespace Resonalyze;

/// <summary>Whether each group's chain in the wizard runs the way the measurements say. See
/// docs/tech/crossover-auto-setup.md#chain-order.</summary>
internal static class AutoSetupWizardChainOrder
{
    /// <summary>Adjacent pairs whose order the measurement does not confirm.</summary>
    public static List<(AutoSetupWizardRow Earlier, AutoSetupWizardRow Later, VirtualCrossoverChainOrder Verdict)>
        DoubtfulPairs(AutoSetupWizardSession session)
    {
        var pairs = new List<(AutoSetupWizardRow, AutoSetupWizardRow, VirtualCrossoverChainOrder)>();
        foreach (VirtualCrossoverAlignmentStage group in session.GroupsInOrder())
        {
            List<AutoSetupWizardRow> members = session.MembersOf(group);
            for (int i = 0; i + 1 < members.Count; i++)
            {
                VirtualCrossoverChainOrder verdict = VirtualCrossoverAutoSetupOrder.Judge(
                    CenterOf(members[i]), CenterOf(members[i + 1]));
                if (verdict != VirtualCrossoverChainOrder.AsMeasured)
                {
                    pairs.Add((members[i], members[i + 1], verdict));
                }
            }
        }

        return pairs;
    }

    /// <summary>Every row of a doubtful pair, with the worse verdict where it is in two.</summary>
    public static Dictionary<AutoSetupWizardRow, VirtualCrossoverChainOrder> Marks(AutoSetupWizardSession session)
    {
        var marks = new Dictionary<AutoSetupWizardRow, VirtualCrossoverChainOrder>();
        foreach ((AutoSetupWizardRow earlier, AutoSetupWizardRow later, VirtualCrossoverChainOrder verdict)
                 in DoubtfulPairs(session))
        {
            foreach (AutoSetupWizardRow row in new[] { earlier, later })
            {
                if (!marks.TryGetValue(row, out VirtualCrossoverChainOrder existing) ||
                    existing != VirtualCrossoverChainOrder.Reversed)
                {
                    marks[row] = verdict;
                }
            }
        }

        return marks;
    }

    /// <summary>What Apply asks before crossing a doubtful chain; null when there is nothing to ask. It asks rather
    /// than refuses: the user may know which sub is which.</summary>
    public static string? Question(AutoSetupWizardSession session)
    {
        List<(AutoSetupWizardRow Earlier, AutoSetupWizardRow Later, VirtualCrossoverChainOrder Verdict)>
            doubtful = DoubtfulPairs(session);
        if (doubtful.Count == 0)
        {
            return null;
        }

        var message = new List<string>();
        var reversed = doubtful
            .Where(pair => pair.Verdict == VirtualCrossoverChainOrder.Reversed)
            .ToList();
        if (reversed.Count > 0)
        {
            message.Add(
                "A group's chain runs from the lowest driver to the highest, and " +
                "these are the wrong way round — the second measures LOWER than " +
                "the one above it:");
            message.Add(string.Empty);
            message.AddRange(reversed.Select(pair =>
                $"    {pair.Earlier.Source.Name}  above  {pair.Later.Source.Name}"));
            message.Add(string.Empty);
        }

        var unclear = doubtful
            .Where(pair => pair.Verdict == VirtualCrossoverChainOrder.Unclear)
            .ToList();
        if (unclear.Count > 0)
        {
            message.Add(
                "These measure too much alike for their order to be read off the " +
                "measurement at all:");
            message.Add(string.Empty);
            message.AddRange(unclear.Select(pair =>
                $"    {pair.Earlier.Source.Name}  above  {pair.Later.Source.Name}"));
            message.Add(string.Empty);
        }

        message.Add(
            "The wizard will cross them in the order shown. Use the ▲▼ arrows to " +
            "change it, or set a crossover corner on one of them first — either " +
            "one says which plays lower. Continue anyway?");
        return string.Join(Environment.NewLine, message);
    }

    private static double CenterOf(AutoSetupWizardRow row) =>
        VirtualCrossoverAutoSetupOrder.CenterHz(
            row.Source.Band, row.Source.HighPassHz, row.Source.LowPassHz);
}
