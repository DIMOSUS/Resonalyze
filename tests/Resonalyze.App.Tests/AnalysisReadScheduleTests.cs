namespace Resonalyze.App.Tests;

/// <summary>A finished read that no longer describes the controls must not be drawn. Driven with integers:
/// through the panel these paths need a real message loop.</summary>
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

        Assert.Null(schedule.Submit(1));
    }

    [Fact]
    public void ARequestAlreadyRunningDoesNotStartTwice()
    {
        schedule.Submit(1);

        Assert.Null(schedule.Submit(1));
    }

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

        Assert.Null(schedule.Submit(2));
        Assert.Null(schedule.Submit(3));
        Assert.Null(schedule.Submit(4));

        Assert.False(schedule.Complete(1, first));
        Assert.NotNull(schedule.TakeDesired(out int wanted));
        Assert.Equal(4, wanted);
        Assert.Null(schedule.TakeDesired(out _));
    }

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

    [Fact]
    public void ReturningToWhatIsRunningDropsWhatWasWanted()
    {
        int version = schedule.Submit(1)!.Value;
        schedule.Submit(2);

        Assert.Null(schedule.Submit(1));

        Assert.True(schedule.Complete(1, version));
        Assert.Null(schedule.TakeDesired(out _));
    }

    [Fact]
    public void ReturningToARetiredReadInFlightRevivesIt()
    {
        int version = schedule.Submit(1)!.Value;
        schedule.Submit(2);
        schedule.Submit(1);

        Assert.True(schedule.Complete(1, version));
        Assert.Null(schedule.TakeDesired(out _));
    }

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

    [Fact]
    public void ClearingRetiresTheRunningReadAndForgetsTheDrawnOne()
    {
        int drawn = schedule.Submit(1)!.Value;
        schedule.Complete(1, drawn);
        int abandoned = schedule.Submit(2)!.Value;

        schedule.Clear();

        Assert.False(schedule.Complete(2, abandoned));
        Assert.Null(schedule.TakeDesired(out _));
        Assert.NotNull(schedule.Submit(1));
    }
}
