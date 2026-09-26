namespace Resonalyze;

/// <summary>The one step of undo each writing command keeps (<see cref="VirtualCrossoverUndo"/>), offered from the command's
/// own dialog, and the restore every undo shares, Undo AI import's included. See docs/tech/virtual-dsp-panel.md#undo.</summary>
public partial class VirtualCrossoverPanel
{
    private readonly VirtualCrossoverUndoHistory undoHistory = new();
    private readonly VirtualCrossoverUndo autoCrossoverUndo;
    private readonly VirtualCrossoverUndo autoDelayUndo;
    private readonly VirtualCrossoverUndo copySideUndo;

    private AgentImportUndo CaptureSession() => AgentImportUndo.Capture(session, agentReader, AgentView());

    /// <summary>Changes made since the write go too, so that is asked first.</summary>
    private void UndoLast(VirtualCrossoverUndo undo)
    {
        if (undo.For(session.ProjectGeneration) is not { } step)
        {
            return;
        }

        if (!undo.Unchanged(CaptureSession()) &&
            ShowMessage(undo.ChangedSince(step), undo.Command, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) !=
                DialogResult.Yes)
        {
            return;
        }

        RestoreChannels(undo.Take());
    }

    // The refusal can be the last write's own doing: moved arrivals can misplace a pinned gate.
    private void Refuse(string message, string details, VirtualCrossoverUndo? undo)
    {
        string? question = undo?.InsteadOf(
            $"{message}{Environment.NewLine}{Environment.NewLine}{details}", session.ProjectGeneration);
        if (question == null)
        {
            ShowError(message, details);
            return;
        }

        if (ShowMessage(question, "Virtual DSP", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            UndoLast(undo!);
        }
    }

    /// <summary>Every channel, the block order and the session-wide settings an engine commits, back as the
    /// snapshot holds them.</summary>
    private void RestoreChannels(AgentImportUndo undo)
    {
        (List<VirtualCrossoverChannel> written, bool reordered) = undo.Restore(session, agentReader);
        foreach (VirtualCrossoverChannel channel in written)
        {
            ShowChannel(channel);
        }

        if (reordered)
        {
            ShowChannelOrder();
        }

        bool suppressed = suppressProjectEvents;
        suppressProjectEvents = true;
        try
        {
            checkBoxHybrid.Checked = undo.HybridTicked;
            ShowTargetLevel();
        }
        finally
        {
            suppressProjectEvents = suppressed;
        }

        foreach (VirtualCrossoverChannel channel in session.Channels)
        {
            RefreshSpatialAverageStatus(channel);
        }

        RefreshHybridAvailability();
        // Remember the restored state as it stands: a difference could carry a side where it never was (L=A,R=B; import wrote L=B; undo restores L=A and would carry A onto R).
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        SaveAndRedraw();
    }
}
