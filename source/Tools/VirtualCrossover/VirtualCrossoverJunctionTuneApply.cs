using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The session before the Apply, the project it was applied in, and the session just after it.</summary>
internal sealed record JunctionTuneUndo(
    AgentImportUndo Channels, long Generation, string Junction, string FingerprintAfter);

/// <summary>Tune junction's write-back and its one step of undo. See docs/tech/crossover-auto-setup.md#junction-tuner.</summary>
internal sealed class VirtualCrossoverJunctionTuneApply(VirtualCrossoverSession session, AgentSessionReader reader)
{
    /// <summary>The last search's result while its dialog is open; what Apply writes.</summary>
    public JunctionTuneResult? Landed { get; set; }

    public JunctionTuneUndo? Undo { get; private set; }

    /// <summary>The junction the last Apply was for, while it can still be undone in this project.</summary>
    public string? Undoable(long generation) =>
        Undo is { } undo && undo.Generation == generation ? undo.Junction : null;

    /// <summary>Writes the found crossover whenever it differs, won or not (the keep margin is advice), and the goal
    /// as asked.</summary>
    public void Apply(
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        JunctionTuneResult landed,
        JunctionAcousticTarget? goal,
        AgentViewInputs view,
        long generation)
    {
        AgentImportUndo before = AgentImportUndo.Capture(session, reader, view);
        AgentJunctionTune.Write(landed, lower, upper, goal, applyCrossover: landed.Moves);
        Undo = new JunctionTuneUndo(before, generation, $"{lower.Name}/{upper.Name}", reader.Fingerprint(view));
    }

    /// <summary>The undo for this project, or null; an undo left from another project is dropped.</summary>
    public JunctionTuneUndo? UndoFor(long generation)
    {
        if (Undo is { } undo && undo.Generation == generation)
        {
            return undo;
        }

        Undo = null;
        return null;
    }

    /// <summary>Whether the session is still exactly as the Apply left it: otherwise Undo takes later changes too.</summary>
    public bool Unchanged(string fingerprintNow) =>
        Undo is { } undo && string.Equals(undo.FingerprintAfter, fingerprintNow, StringComparison.Ordinal);

    public AgentImportUndo TakeUndo()
    {
        AgentImportUndo channels = Undo?.Channels ?? throw new InvalidOperationException("Nothing to undo.");
        Undo = null;
        return channels;
    }
}
