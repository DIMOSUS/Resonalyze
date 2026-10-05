namespace Resonalyze;

/// <summary>Best-effort warm-ups that hold the audio driver, at startup and from Record Settings; a run waits for them.</summary>
internal sealed class StartupAudioWarmup : IDisposable
{
    private readonly Func<CancellationToken, Task> warmUp;
    private CancellationTokenSource? cancellation;
    private Task? task;
    private Task? later;

    public StartupAudioWarmup(Func<CancellationToken, Task> warmUp)
    {
        this.warmUp = warmUp;
    }

    public void Start()
    {
        if (task != null)
        {
            return;
        }

        cancellation = new CancellationTokenSource();
        task = warmUp(cancellation.Token);
    }

    /// <summary>A later warm-up, begun once the one in flight ends; WaitAsync covers it, and its fault reaches the caller.</summary>
    public Task RunAsync(Func<Task> laterWarmUp)
    {
        Task started = Running() is { } earlier ? RunAfterAsync(earlier, laterWarmUp) : laterWarmUp();
        later = started;
        return started;
    }

    /// <summary>Completes once none runs, one started meanwhile included; faults are swallowed (non-fatal).</summary>
    public async Task WaitAsync()
    {
        while (Running() is { } running)
        {
            try
            {
                await running;
            }
            catch
            {
            }
        }
    }

    /// <summary>Cancels without disposing, for the fast OS-shutdown close.</summary>
    public void Cancel()
    {
        cancellation?.Cancel();
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    // Two at once would hold one driver twice; the earlier one's fault belongs to its own caller.
    private static async Task RunAfterAsync(Task earlier, Func<Task> laterWarmUp)
    {
        try
        {
            await earlier;
        }
        catch
        {
        }

        await laterWarmUp();
    }

    private Task? Running() =>
        task is { IsCompleted: false } ? task : later is { IsCompleted: false } ? later : null;
}
