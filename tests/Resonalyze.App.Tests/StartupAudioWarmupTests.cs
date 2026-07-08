namespace Resonalyze.App.Tests;

/// <summary>
/// The startup warm-up task/cancellation pair moved off Form1 into
/// <see cref="StartupAudioWarmup"/>; these pin the once-only start, the
/// non-fatal wait and the cancel used by the close paths.
/// </summary>
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
