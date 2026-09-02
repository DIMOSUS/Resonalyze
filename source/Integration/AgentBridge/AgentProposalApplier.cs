using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>One channel's settings as they were before an import wrote to them.</summary>
internal sealed record AgentUndoEntry(
    VirtualCrossoverChannelSettings Target,
    VirtualCrossoverChannelSettings Before);

/// <summary>
/// The commit half of an import: judges the ticked rows once more against the
/// session as it is NOW (the dialog may have been open a while), applies them to
/// the live settings as one set, and hands back what to put back for Undo. No
/// control and no redraw in here — the panel does those once, after.
/// </summary>
internal static class AgentProposalApplier
{
    public const string PeqSourceName = "AI proposal";

    /// <summary>
    /// Re-reviews the proposal against a fresh snapshot and keeps the ticked rows.
    /// Null when every one of them is still applicable and admissible together;
    /// otherwise the first problem, and nothing may be applied.
    /// </summary>
    public static string? Prepare(
        AgentProposal proposal,
        IReadOnlySet<string> selectedIds,
        AgentSessionSnapshot session,
        out List<AgentOperationVerdict> toApply)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(session);

        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);
        // Rows the parser refused carry no operation and can never have been
        // ticked; they are skipped here by that, not by id — a refused object may
        // carry the same id as a row the user did tick.
        toApply = review.Verdicts
            .Where(verdict => verdict.Operation != null && selectedIds.Contains(verdict.Id))
            .ToList();
        if (toApply.Count == 0)
        {
            return "No applicable change was selected.";
        }

        AgentOperationVerdict? stale = toApply.FirstOrDefault(verdict => !verdict.Applicable);
        if (stale != null)
        {
            toApply.Clear();
            return $"The session changed while the review was open ({stale.Id}: {stale.Message}). " +
                "Import the reply again to review it against the current settings.";
        }

        string? problem = AgentProposalValidator.CheckSelection(toApply);
        if (problem != null)
        {
            toApply.Clear();
        }

        return problem;
    }

    /// <summary>
    /// Writes the rows into their channels' live settings. Every channel is
    /// snapshotted before its first write; should a write throw, what was written
    /// is put back and the exception surfaces — the settings never hold half a set.
    /// </summary>
    public static List<AgentUndoEntry> Apply(IReadOnlyList<AgentOperationVerdict> toApply)
    {
        ArgumentNullException.ThrowIfNull(toApply);

        var undo = new List<AgentUndoEntry>();
        try
        {
            foreach (IGrouping<VirtualCrossoverChannelSettings, AgentOperationVerdict> group in toApply
                .Where(verdict => verdict.Applicable)
                .GroupBy(verdict => verdict.Channel!.Settings))
            {
                VirtualCrossoverChannelSettings settings = group.Key;
                undo.Add(new AgentUndoEntry(settings, AgentOperations.CloneEditable(settings)));
                foreach (AgentOperationVerdict verdict in group)
                {
                    AgentOperations.Apply(verdict.Operation!, settings);
                    if (verdict.Operation is ReplacePeqBankOperation)
                    {
                        // The block's PEQ read-out names where a bank came from; this
                        // one came from the assistant, not from a file or the wizard.
                        settings.PeqSourceName = PeqSourceName;
                    }
                }
            }
        }
        catch
        {
            Restore(undo);
            throw;
        }

        return undo;
    }

    /// <summary>Puts every channel back exactly as it was before <see cref="Apply"/>.</summary>
    public static void Restore(IReadOnlyList<AgentUndoEntry> undo)
    {
        ArgumentNullException.ThrowIfNull(undo);

        foreach (AgentUndoEntry entry in undo)
        {
            CopyEditable(entry.Before, entry.Target);
        }
    }

    /// <summary>The editable chain — and only it — from one settings object into another.</summary>
    public static void CopyEditable(VirtualCrossoverChannelSettings from, VirtualCrossoverChannelSettings to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        to.GainDb = from.GainDb;
        to.DelayMs = from.DelayMs;
        to.InvertPolarity = from.InvertPolarity;
        to.CrossoverKind = from.CrossoverKind;
        to.LowPassEdge = from.LowPassEdge;
        to.HighPassEdge = from.HighPassEdge;
        to.PeqPreampDb = from.PeqPreampDb;
        to.PeqBands = new List<PeqBand>(from.PeqBands);
        to.PeqSourceName = from.PeqSourceName;
    }
}
