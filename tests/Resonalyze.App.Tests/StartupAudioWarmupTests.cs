namespace Resonalyze.App.Tests;

public sealed class StartupAudioWarmupTests
{
    [Fact]
    public async Task Start_RunsTheWarmUpOnlyOnce()
    {
        int starts = 0;
        using var warmup = new StartupAudioWarmup(_ =>
        {
            starts++;
            return Task.CompletedTask;
        });

        warmup.Start();
        warmup.Start();
        await warmup.WaitAsync();

        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task WaitAsync_CompletesImmediatelyWhenNeverStarted()
    {
        using var warmup = new StartupAudioWarmup(_ => Task.CompletedTask);

        await warmup.WaitAsync();
    }

    [Fact]
    public async Task WaitAsync_SwallowsWarmUpFailures()
    {
        using var warmup = new StartupAudioWarmup(
            async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("driver failed");
            });

        warmup.Start();
        await warmup.WaitAsync();
    }

    // Record Settings' Apply warms up on any backend, so also where no startup warm-up ran.
    [Fact]
    public async Task WaitAsync_WaitsForALaterWarmUp()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var warmup = new StartupAudioWarmup(_ => Task.CompletedTask);
        Task later = warmup.RunAsync(() => release.Task);

        Task waiting = warmup.WaitAsync();
        Assert.False(waiting.IsCompleted);

        release.SetResult();
        await waiting;
        await later;
    }

    // A short second warm-up ending first hid the first one, still holding the driver, from a run.
    [Fact]
    public async Task ALaterWarmUp_BeginsAfterTheOneInFlight_AndWaitAsyncCoversBoth()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool secondBegan = false;
        using var warmup = new StartupAudioWarmup(_ => Task.CompletedTask);
        Task a = warmup.RunAsync(() => first.Task);
        Task b = warmup.RunAsync(() =>
        {
            secondBegan = true;
            return Task.CompletedTask;
        });

        Task waiting = warmup.WaitAsync();
        Assert.False(secondBegan);
        Assert.False(waiting.IsCompleted);

        first.SetResult();
        await waiting;
        Assert.True(secondBegan);
        await a;
        await b;
    }

    [Fact]
    public async Task Cancel_SignalsTheWarmUpToken()
    {
        var cancelled = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var warmup = new StartupAudioWarmup(async token =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult(true);
            }
        });

        warmup.Start();
        warmup.Cancel();
        await warmup.WaitAsync();

        Assert.True(await cancelled.Task);
    }
}
