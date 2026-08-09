namespace Resonalyze;

/// <summary>
/// Undo/redo over whole <see cref="PeqBankState"/> snapshots. A snapshot is a
/// handful of numbers, so keeping the last <see cref="Capacity"/> of them costs
/// less than the bookkeeping an undoable-command model would need — and it is
/// correct for every operation by construction, including Auto Tune and import,
/// which replace the entire bank rather than editing one band.
/// </summary>
internal sealed class PeqBankHistory
{
    /// <summary>How many steps back the bank remembers; the oldest is dropped.</summary>
    public const int Capacity = 100;

    private readonly List<PeqBankState> undo = new();
    private readonly List<PeqBankState> redo = new();

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }

    /// <summary>
    /// Records the state a change moved away from. Recording is what makes a
    /// change a step, so it also drops the redo trail: the future that was
    /// undone is no longer reachable from the branch the user just took.
    /// </summary>
    public void Push(PeqBankState previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        undo.Add(previous);
        if (undo.Count > Capacity)
        {
            undo.RemoveAt(0);
        }

        redo.Clear();
    }

    /// <summary>
    /// Steps back one state, handing <paramref name="current"/> to the redo
    /// stack. False (and an untouched history) when there is nothing to undo.
    /// </summary>
    public bool TryUndo(PeqBankState current, out PeqBankState previous)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (undo.Count == 0)
        {
            previous = current;
            return false;
        }

        previous = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(current);
        return true;
    }

    /// <summary>Steps forward again, handing <paramref name="current"/> back to undo.</summary>
    public bool TryRedo(PeqBankState current, out PeqBankState next)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (redo.Count == 0)
        {
            next = current;
            return false;
        }

        next = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(current);
        return true;
    }
}
