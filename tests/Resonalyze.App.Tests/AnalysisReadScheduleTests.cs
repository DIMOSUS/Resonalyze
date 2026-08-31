namespace Resonalyze.App.Tests;

/// <summary>
/// The Time Alignment panel's read scheduler. Its whole job is that what is on
/// screen answers the controls as they stand now: a read is slow enough that
/// the user can move on twice while it runs, and every one of those moves has
/// to leave a finished read either drawable or retired.
/// </summary>
public sealed class AnalysisReadScheduleTests
{
    private readonly AnalysisReadSchedule<int> schedule = new();

    [Fact]
    public void AFreshRequestRuns()
    {
        Assert.NotNull(schedule.Submit(1));
    }

    [Fact]
    public void ARequestAlreadyDrawnDoesNotRunAgain()
    {
        int version = schedule.Submit(1)!.Value;
        Assert.True(schedule.Accept(1, version));

        // The refresh a mode switch fires behind the one that drew this.
        Assert.Null(schedule.Submit(1));
    }

    [Fact]
    public void ARequestAlreadyRunningDoesNotStartTwice()
    {
        schedule.Submit(1);

        Assert.Null(schedule.Submit(1));
    }

    [Fact]
    public void OnlyOneReadRunsAndOnlyTheLastOneWaits()
    {
        int version = schedule.Submit(1)!.Value;

        // A held-down spinner: a request per click while the first read runs.
        Assert.Null(schedule.Submit(2));
        Assert.Null(schedule.Submit(3));
        Assert.Null(schedule.Submit(4));

        Assert.True(schedule.Accept(1, version));
        Assert.NotNull(schedule.TakeQueued(out int queued));
        Assert.Equal(4, queued);
        Assert.Null(schedule.TakeQueued(out _));
    }

    // The failure this scheduler exists for. Selecting a Compare record and
    // clearing it again before the read finishes leaves the panel asking for
    // exactly what it already shows — and the read for the record that is no
    // longer selected must not land on top of it with its Compare deltas.
    [Fact]
    public void AReadIsRetiredWhenTheControlsReturnToWhatIsDrawn()
    {
        int drawn = schedule.Submit(1)!.Value;
        schedule.Accept(1, drawn);
        int abandoned = schedule.Submit(2)!.Value;

        Assert.Null(schedule.Submit(1));

        Assert.False(schedule.Accept(2, abandoned));
    }

    [Fact]
    public void ReturningToWhatIsDrawnAlsoDropsWhatWasWaiting()
    {
        int drawn = schedule.Submit(1)!.Value;
        schedule.Accept(1, drawn);
        int abandoned = schedule.Submit(2)!.Value;
        schedule.Submit(3);

        Assert.Null(schedule.Submit(1));

        Assert.False(schedule.Accept(2, abandoned));
        Assert.Null(schedule.TakeQueued(out _));
    }

    // The same trap one step in: the queued read is older than the request that
    // returns to the one already running, so it describes a state that has been
    // left twice over.
    [Fact]
    public void ReturningToWhatIsRunningDropsWhatWasWaiting()
    {
        int version = schedule.Submit(1)!.Value;
        schedule.Submit(2);

        Assert.Null(schedule.Submit(1));

        Assert.True(schedule.Accept(1, version));
        Assert.Null(schedule.TakeQueued(out _));
    }

    // What waited runs when the read ahead of it lands, and is then the drawn
    // read like any other — the refresh that follows it draws nothing again.
    [Fact]
    public void AQueuedReadRunsAndThenCountsAsDrawn()
    {
        int first = schedule.Submit(1)!.Value;
        schedule.Submit(2);
        Assert.True(schedule.Accept(1, first));
        int queuedVersion = schedule.TakeQueued(out int queued)!.Value;
        Assert.Equal(2, queued);

        Assert.True(schedule.Accept(2, queuedVersion));
        Assert.Null(schedule.Submit(2));
    }

    // Losing the record: nothing is drawn any more, and the read that was
    // running for it cannot be drawn either — the panel is showing the
    // "no data" message instead.
    [Fact]
    public void ClearingRetiresTheRunningReadAndForgetsTheDrawnOne()
    {
        int drawn = schedule.Submit(1)!.Value;
        schedule.Accept(1, drawn);
        int abandoned = schedule.Submit(2)!.Value;

        schedule.Clear();

        Assert.False(schedule.Accept(2, abandoned));
        // The same request is new again: what it used to answer is gone.
        Assert.NotNull(schedule.Submit(1));
    }
}
