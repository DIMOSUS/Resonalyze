using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Tune junction's write-back and its one step of undo. See docs/tech/crossover-auto-setup.md#junction-tuner.</summary>
internal sealed class VirtualCrossoverJunctionTuneApply(
    VirtualCrossoverSession session, AgentSessionReader reader, VirtualCrossoverUndoHistory history)
{
    /// <summary>The last search's result while its dialog is open; what Apply writes.</summary>
    public JunctionTuneResult? Landed { get; set; }

    public VirtualCrossoverUndo Undo { get; } = VirtualCrossoverUndo.TuneJunction(history);

    /// <summary>Writes the found crossover whenever it differs, won or not (the keep margin is advice), and the goal
    /// as asked.</summary>
    /// <returns>The session as it was, for <see cref="Remember"/> once the write is shown.</returns>
    public AgentImportUndo Apply(
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        JunctionTuneResult landed,
        JunctionAcousticTarget? goal,
        AgentViewInputs view)
    {
        AgentImportUndo before = AgentImportUndo.Capture(session, reader, view);
        AgentJunctionTune.Write(landed, lower, upper, goal, applyCrossover: landed.Moves);
        return before;
    }

    public void Remember(
        AgentImportUndo before,
        long generation,
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        AgentImportUndo after) =>
        Undo.Remember(before, generation, $"{lower.Name}/{upper.Name}", after);
}
