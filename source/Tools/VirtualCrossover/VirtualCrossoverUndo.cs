namespace Resonalyze;

/// <summary>The session before a write and just after it, the project it was made in, what it wrote, and when.</summary>
internal sealed record VirtualCrossoverUndoStep(
    AgentImportUndo Before, AgentImportUndo After, long Generation, string What, long Written);

/// <summary>The order every undo step was written in, the AI import's included. Taking a step drops the steps written
/// after it: they describe a session the restore has left, and would bring back what it took away.</summary>
internal sealed class VirtualCrossoverUndoHistory
{
    private readonly List<(long After, long At)> drops = [];
    private long clock;

    public long Write() => ++clock;

    public void DropAfter(long written) => drops.Add((written, ++clock));

    public bool Stands(long written) => !drops.Exists(drop => drop.After < written && written < drop.At);
}

/// <summary>One command's one step of undo. A step from another project is dropped; one the session moved on from still
/// restores, after a question. See docs/tech/virtual-dsp-panel.md#undo.</summary>
internal sealed class VirtualCrossoverUndo
{
    private readonly VirtualCrossoverUndoHistory history;
    private readonly Func<string, string> written;
    private readonly string write;
    private VirtualCrossoverUndoStep? step;

    private VirtualCrossoverUndo(
        VirtualCrossoverUndoHistory history, string command, Func<string, string> written, string write = "Apply")
    {
        this.history = history;
        Command = command;
        this.written = written;
        this.write = write;
    }

    public string Command { get; }

    public VirtualCrossoverUndoStep? Step => step is { } kept && history.Stands(kept.Written) ? kept : null;

    public static VirtualCrossoverUndo TuneJunction(VirtualCrossoverUndoHistory history) =>
        new(history, "Tune junction", junction => $"the tune of {junction} was applied");

    public static VirtualCrossoverUndo AutoCrossover(VirtualCrossoverUndoHistory history) =>
        new(history, "Auto crossover", blocks => $"Auto crossover was applied to {blocks}");

    public static VirtualCrossoverUndo AutoDelay(VirtualCrossoverUndoHistory history) =>
        new(history, "Auto delay", sides => $"Auto delay aligned {sides}");

    public static VirtualCrossoverUndo CopySide(VirtualCrossoverUndoHistory history) =>
        new(history, "Copy between sides", copy => $"the copy {copy}", "copy");

    public static string Blocks(IEnumerable<VirtualCrossoverChannel> channels) =>
        string.Join(", ", channels.Select(channel => channel.Name));

    public static string Aligned(bool stereo, bool rightSide) =>
        stereo ? "both sides" : rightSide ? "the right side" : "the left side";

    public static string Copied(bool fromRight, IEnumerable<VirtualCrossoverChannel> channels) =>
        $"{(fromRight ? "R → L" : "L → R")} of {Blocks(channels)}";

    /// <summary>What the last write was, while it can still be undone in this project.</summary>
    public string? Undoable(long generation) =>
        Step is { } kept && kept.Generation == generation ? kept.What : null;

    /// <summary>A write that changed nothing leaves the step before it standing.</summary>
    /// <param name="after">The session read once the cards show the write: showing it can still move a field.</param>
    public void Remember(AgentImportUndo before, long generation, string what, AgentImportUndo after)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (!before.SameAs(after))
        {
            step = new VirtualCrossoverUndoStep(before, after, generation, what, history.Write());
        }
    }

    public VirtualCrossoverUndoStep? For(long generation)
    {
        if (Step is { } kept && kept.Generation == generation)
        {
            return kept;
        }

        step = null;
        return null;
    }

    /// <summary>Whether the session is still as the write left it, as far as Undo reaches: otherwise Undo takes the later
    /// changes too.</summary>
    public bool Unchanged(AgentImportUndo now) => Step is { } kept && kept.After.SameAs(now);

    public AgentImportUndo Take()
    {
        VirtualCrossoverUndoStep taken = Step ?? throw new InvalidOperationException("Nothing to undo.");
        step = null;
        history.DropAfter(taken.Written);
        return taken.Before;
    }

    public string ChangedSince(VirtualCrossoverUndoStep changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        return $"The session has changed since {written(changed.What)}. Undo puts every channel back exactly as it was " +
            $"before that {write}, so the later changes go as well." + Environment.NewLine + Environment.NewLine +
            "Undo anyway?";
    }

    /// <summary>A refusal of the command offering its undo instead, since the refusal keeps the dialog holding Undo shut;
    /// null with nothing to undo in this project.</summary>
    public string? InsteadOf(string refusal, long generation) =>
        Undoable(generation) == null
            ? null
            : refusal + Environment.NewLine + Environment.NewLine + $"Undo the last {Command} instead?";
}
