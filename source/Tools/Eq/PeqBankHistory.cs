namespace Resonalyze;

/// <summary>Undo/redo over whole-bank snapshots: cheap, and correct by construction for Auto Tune and import.</summary>
internal sealed class PeqBankHistory
{
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

    /// <summary>Records the state a change moved away from; drops the redo trail.</summary>
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
