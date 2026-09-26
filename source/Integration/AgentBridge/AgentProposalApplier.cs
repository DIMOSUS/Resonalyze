using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

internal sealed record AgentUndoEntry(
    VirtualCrossoverChannelSettings Target,
    VirtualCrossoverChannelSettings Before);

/// <summary>Commit half of an import: re-judges ticked rows against the session now, writes settings rows as one set and returns undo; engine rows go back to the panel. No UI here.</summary>
internal static class AgentProposalApplier
{
    public const string PeqSourceName = "AI proposal";

    /// <summary>Re-reviews against a fresh snapshot. Null when the ticked rows are still admissible together; otherwise the first problem. <paramref name="unseenWarnings"/> are final-state warnings the review did not show (the panel asks, never refuses).</summary>
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
        // Skipped by missing operation, not id: a refused object may share an id with a ticked row.
        toApply = review.Verdicts
            .Where(verdict => verdict.Operation != null && selectedIds.Contains(verdict.Id))
            .ToList();
        if (toApply.Count == 0)
        {
            return "No applicable change was selected.";
        }

        // Ticked is the review's default, not a gate (stale rows are offered unticked to opt in); what matters is whether the session moved after the dialog showed.
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

        // Only channels whose ticked rows shape the junction zone; notes on others describe the tune as it already is.
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

    /// <summary>Writes settings rows into live settings; on a throw what was written is restored, so settings never hold half a set.</summary>
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

    public static void Restore(IReadOnlyList<AgentUndoEntry> undo)
    {
        ArgumentNullException.ThrowIfNull(undo);

        foreach (AgentUndoEntry entry in undo)
        {
            CopyEditable(entry.Before, entry.Target);
        }
    }

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
        to.AcousticLowPass = from.AcousticLowPass;
        to.AcousticHighPass = from.AcousticHighPass;
        // Also the undo path, and probe variants are built from it: every running filter and goal is carried.
        to.PhaseRotationDegrees = from.PhaseRotationDegrees;
        to.PeqPreampDb = from.PeqPreampDb;
        to.PeqBands = new List<PeqBand>(from.PeqBands);
        to.PeqSourceName = from.PeqSourceName;
        to.Fir = from.Fir;
        to.FirSourceName = from.FirSourceName;
        to.FirDesign = from.FirDesign;
        to.FirRunSampleRateHz = from.FirRunSampleRateHz;
    }

    /// <summary>Whether <see cref="CopyEditable"/> would change nothing; the two must name the same fields.</summary>
    public static bool SameEditable(VirtualCrossoverChannelSettings a, VirtualCrossoverChannelSettings b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return a.GainDb.Equals(b.GainDb) &&
            a.DelayMs.Equals(b.DelayMs) &&
            a.InvertPolarity == b.InvertPolarity &&
            a.CrossoverKind == b.CrossoverKind &&
            a.LowPassEdge == b.LowPassEdge &&
            a.HighPassEdge == b.HighPassEdge &&
            a.AcousticLowPass == b.AcousticLowPass &&
            a.AcousticHighPass == b.AcousticHighPass &&
            a.PhaseRotationDegrees.Equals(b.PhaseRotationDegrees) &&
            a.PeqPreampDb.Equals(b.PeqPreampDb) &&
            a.PeqBands.SequenceEqual(b.PeqBands) &&
            a.PeqSourceName == b.PeqSourceName &&
            ReferenceEquals(a.Fir, b.Fir) &&
            a.FirSourceName == b.FirSourceName &&
            a.FirDesign == b.FirDesign &&
            a.FirRunSampleRateHz == b.FirRunSampleRateHz;
    }
}
