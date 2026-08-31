namespace Resonalyze.App.Tests;

/// <summary>
/// The Time Alignment panel's read scheduler. Its whole job is that what is on
/// screen answers the controls as they stand NOW: a read is slow enough that
/// the user moves on twice while it runs, and a finished read that no longer
/// describes the controls must not be drawn at all — a panel stating an
/// alignment for a band nobody is looking at is worse than a panel that has not
/// caught up yet.
/// </summary>
/// <remarks>
/// Driven with plain integers because the state machine is the subject: through
/// the panel these paths need a real message loop (with none, the read runs
/// inline and nothing is ever in flight), which is exactly why they went
/// untested — and one of them unnoticed — while the scheduling lived in four
/// fields of the controller.
/// </remarks>
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
        Assert.True(schedule.Complete(1, version));

        // The refresh a mode switch fires behind the one that drew this.
        Assert.Null(schedule.Submit(1));
    }

    [Fact]
    public void ARequestAlreadyRunningDoesNotStartTwice()
    {
        schedule.Submit(1);

        Assert.Null(schedule.Submit(1));
    }

    // The rule, stated on its own: a read the controls have moved off is not
    // drawn when it lands — not "drawn, then corrected by the next one".
    [Fact]
    public void ASupersededReadIsNotDrawnAndTheWantedOneRunsInstead()
    {
        int drawn = schedule.Submit(1)!.Value;
        schedule.Complete(1, drawn);
        int superseded = schedule.Submit(2)!.Value;

        Assert.Null(schedule.Submit(3));

        Assert.False(schedule.Complete(2, superseded));
        int next = schedule.TakeDesired(out int wanted)!.Value;
        Assert.Equal(3, wanted);
        Assert.True(schedule.Complete(3, next));
    }

    [Fact]
    public void OnlyTheLastOfAHeldSpinnerEverRuns()
    {
        int first = schedule.Submit(1)!.Value;

        // A request per click while the first read runs.
        Assert.Null(schedule.Submit(2));
        Assert.Null(schedule.Submit(3));
        Assert.Null(schedule.Submit(4));

        Assert.False(schedule.Complete(1, first));
        Assert.NotNull(schedule.TakeDesired(out int wanted));
        Assert.Equal(4, wanted);
        Assert.Null(schedule.TakeDesired(out _));
    }

    // Regression, first order: render A, start B, return to A before B lands.
    // B must not be drawn, and nothing else may run — A is already the answer.
    [Fact]
    public void ReturningToWhatIsDrawnRetiresTheReadInFlight()
    {
        int drawn = schedule.Submit(1)!.Value;
        schedule.Complete(1, drawn);
        int abandoned = schedule.Submit(2)!.Value;

        Assert.Null(schedule.Submit(1));

        Assert.False(schedule.Complete(2, abandoned));
        Assert.Null(schedule.TakeDesired(out _));
    }

    // Regression, second order: start A, want B, return to A. A is drawn when
    // it lands and B never runs — it describes a state left twice over.
    [Fact]
    public void ReturningToWhatIsRunningDropsWhatWasWanted()
    {
        int version = schedule.Submit(1)!.Value;
        schedule.Submit(2);

        Assert.Null(schedule.Submit(1));

        Assert.True(schedule.Complete(1, version));
        Assert.Null(schedule.TakeDesired(out _));
    }

    // The read the controls come back to is revived, not recomputed: it answers
    // exactly what is being asked for again, and one read runs at a time, so no
    // other version is live to collide with its own.
    [Fact]
    public void ReturningToARetiredReadInFlightRevivesIt()
    {
        int version = schedule.Submit(1)!.Value;
        schedule.Submit(2);
        schedule.Submit(1);

        Assert.True(schedule.Complete(1, version));
        Assert.Null(schedule.TakeDesired(out _));
    }

    // What waited runs when the pool frees, and is then the drawn read like any
    // other — the refresh that follows it draws nothing again.
    [Fact]
    public void AWantedReadRunsAndThenCountsAsDrawn()
    {
        int first = schedule.Submit(1)!.Value;
        schedule.Submit(2);
        Assert.False(schedule.Complete(1, first));
        int version = schedule.TakeDesired(out int wanted)!.Value;
        Assert.Equal(2, wanted);

        Assert.True(schedule.Complete(2, version));
        Assert.Null(schedule.Submit(2));
    }

    // Losing the record: nothing is drawn any more, and the read that was
    // running for it cannot be drawn either — the panel is showing the
    // "no data" message instead.
    [Fact]
    public void ClearingRetiresTheRunningReadAndForgetsTheDrawnOne()
    {
        int drawn = schedule.Submit(1)!.Value;
        schedule.Complete(1, drawn);
        int abandoned = schedule.Submit(2)!.Value;

        schedule.Clear();

        Assert.False(schedule.Complete(2, abandoned));
        Assert.Null(schedule.TakeDesired(out _));
        // The same request is new again: what it used to answer is gone.
        Assert.NotNull(schedule.Submit(1));
    }
}
