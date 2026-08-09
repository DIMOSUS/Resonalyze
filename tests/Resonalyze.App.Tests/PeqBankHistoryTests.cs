using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class PeqBankHistoryTests
{
    private static PeqBankState Bank(params double[] frequenciesHz) =>
        new(frequenciesHz.Select(hz => new PeqBand(hz, 5, -3)), 0);

    [Fact]
    public void UndoAndRedo_WalkTheRecordedStatesBothWays()
    {
        var history = new PeqBankHistory();
        PeqBankState first = Bank(100);
        PeqBankState second = Bank(100, 200);
        PeqBankState third = Bank(100, 200, 400);

        history.Push(first);
        history.Push(second);

        Assert.True(history.TryUndo(third, out PeqBankState back));
        Assert.Equal(second, back);
        Assert.True(history.TryUndo(back, out back));
        Assert.Equal(first, back);
        Assert.False(history.CanUndo);

        Assert.True(history.TryRedo(back, out PeqBankState forward));
        Assert.Equal(second, forward);
        Assert.True(history.TryRedo(forward, out forward));
        Assert.Equal(third, forward);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void TryUndo_OnAnEmptyHistoryLeavesTheStateAlone()
    {
        var history = new PeqBankHistory();
        PeqBankState current = Bank(1000);

        Assert.False(history.TryUndo(current, out PeqBankState result));
        Assert.Equal(current, result);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_DropsTheRedoTrail()
    {
        // Undoing and then editing again takes a new branch: the future that was
        // undone is no longer reachable, so redo must not resurrect it.
        var history = new PeqBankHistory();
        history.Push(Bank(100));
        Assert.True(history.TryUndo(Bank(100, 200), out PeqBankState _));
        Assert.True(history.CanRedo);

        history.Push(Bank(100));

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_KeepsOnlyTheMostRecentStatesOnceFull()
    {
        var history = new PeqBankHistory();
        for (int index = 0; index <= PeqBankHistory.Capacity; index++)
        {
            history.Push(Bank(100 + index));
        }

        // Capacity + 1 states were pushed, so the oldest is gone; walking all the
        // way back must land on the second one, never on the dropped first.
        PeqBankState state = Bank(9000);
        int steps = 0;
        while (history.TryUndo(state, out state))
        {
            steps++;
        }

        Assert.Equal(PeqBankHistory.Capacity, steps);
        Assert.Equal(Bank(101), state);
    }

    [Fact]
    public void States_WithTheSameBandsInADifferentOrderAreNotEqual()
    {
        // Order is what an exported profile numbers its filters by, so a reorder
        // is a real change and has to be recordable as one.
        Assert.NotEqual(Bank(100, 200), Bank(200, 100));
        Assert.Equal(Bank(100, 200), Bank(100, 200));
    }

    [Fact]
    public void States_DifferOnPreampAlone()
    {
        var quiet = new PeqBankState(new[] { new PeqBand(100, 5, -3) }, -6);
        var loud = new PeqBankState(new[] { new PeqBand(100, 5, -3) }, 0);

        Assert.NotEqual(quiet, loud);
    }
}
