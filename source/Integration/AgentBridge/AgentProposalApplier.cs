using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>One channel's settings as they were before an import wrote to them.</summary>
internal sealed record AgentUndoEntry(
    VirtualCrossoverChannelSettings Target,
    VirtualCrossoverChannelSettings Before);

/// <summary>
/// The commit half of an import: judges the ticked rows once more against the
/// session as it is NOW (the dialog may have been open a while), applies the
/// ones that write settings to the live objects as one set, and hands back what
/// to put back for Undo. The engine requests among the ticked rows are handed
/// straight back for the panel to run in its own fixed order — nothing in here
/// touches a control, opens a dialog or redraws.
/// </summary>
internal static class AgentProposalApplier
{
    public const string PeqSourceName = "AI proposal";

    /// <summary>
    /// Re-reviews the proposal against a fresh snapshot and keeps the ticked rows.
    /// Null when every one of them is still applicable and admissible together;
    /// otherwise the first problem, and nothing may be applied.
    /// </summary>
    /// <param name="unseenWarnings">
    /// Warnings about the channels as the TICKED rows would leave them that the
    /// review did not show — it judged every applicable row together, and an
    /// unticked row can take a compensating change with it. The panel asks before
    /// applying over them; they never refuse on their own.
    /// </param>
    /// <param name="reviewedFingerprint">
    /// The session fingerprint shown in the review dialog. A different current
    /// fingerprint means the session moved while the dialog was open; an already
    /// stale row that the user deliberately ticked is allowed while it stays the
    /// same.
    /// </param>
    public static string? Prepare(
        AgentProposal proposal,
        IReadOnlySet<string> selectedIds,
        string? reviewedFingerprint,
        AgentSessionSnapshot session,
        out List<AgentOperationVerdict> toApply,
        out List<string> unseenWarnings)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(session);

        unseenWarnings = [];
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

        // Ticked is the review's DEFAULT, not an admissibility gate: a stale
        // settings row is deliberately offered unticked for the user to opt in.
        // What matters at commit is whether the session moved AFTER that warning
        // was shown. The dialog's fingerprint is the only state that can answer
        // that; comparing the fresh verdict's Ticked flag would reject the exact
        // manual override the review offers.
        if (!string.Equals(reviewedFingerprint, session.Fingerprint, StringComparison.Ordinal))
        {
            toApply.Clear();
            return "The session changed while the review was open. " +
                "Import the reply again to review it against the current settings.";
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
            return problem;
        }

        // Only channels whose ticked rows shape the junction zone: a gain or delay
        // row leaves the bank and the corners as they are, and a note about them
        // would be about the tune as it already is, not about the import.
        HashSet<AgentChannelSnapshot> shaped = toApply
            .Where(verdict => verdict.Operation is SetCrossoverOperation or ReplacePeqBankOperation)
            .Select(verdict => verdict.Channel!)
            .ToHashSet();
        foreach ((AgentChannelSnapshot channel, List<string> notes) in
            AgentProposalValidator.FinalStateNotes(toApply).Where(entry => shaped.Contains(entry.Channel)))
        {
            foreach (string note in notes)
            {
                bool shown = toApply.Any(verdict =>
                    ReferenceEquals(verdict.Channel, channel) &&
                    verdict.Message.Contains(note, StringComparison.Ordinal));
                if (!shown)
                {
                    unseenWarnings.Add($"{channel.Label}: {note}");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Writes the settings rows into their channels' live settings. Every channel
    /// is snapshotted before its first write; should a write throw, what was
    /// written is put back and the exception surfaces — the settings never hold
    /// half a set. The engine rows are not touched here: they are asked for, not
    /// written, and the panel runs them afterwards.
    /// </summary>
    public static List<AgentUndoEntry> Apply(IReadOnlyList<AgentOperationVerdict> toApply)
    {
        ArgumentNullException.ThrowIfNull(toApply);

        var undo = new List<AgentUndoEntry>();
        try
        {
            foreach (IGrouping<VirtualCrossoverChannelSettings, AgentOperationVerdict> group in toApply
                .Where(verdict => verdict.Applicable && verdict.Operation is AgentSettingsOperation)
                .GroupBy(verdict => verdict.Channel!.Settings))
            {
                VirtualCrossoverChannelSettings settings = group.Key;
                undo.Add(new AgentUndoEntry(settings, AgentOperations.CloneEditable(settings)));
                foreach (AgentOperationVerdict verdict in group)
                {
                    AgentOperations.Apply((AgentSettingsOperation)verdict.Operation!, settings);
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
        // No operation writes it, but this is the UNDO path as well: a phase
        // rotation dialled in after an import would otherwise survive the undo
        // while every setting beside it went back.
        to.PhaseRotationDegrees = from.PhaseRotationDegrees;
        to.PeqPreampDb = from.PeqPreampDb;
        to.PeqBands = new List<PeqBand>(from.PeqBands);
        to.PeqSourceName = from.PeqSourceName;
    }
}
