namespace Resonalyze;

/// <summary>Best-effort audio warm-ups that hold the driver while they run: the one-shot startup one, body injected to keep
/// device and UI concerns out, and the ones Record Settings starts later. A run or a device probe waits for both.</summary>
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

    /// <summary>A warm-up after startup, which <see cref="WaitAsync"/> waits for too; its fault reaches the caller.</summary>
    public Task RunAsync(Func<Task> laterWarmUp)
    {
        Task started = laterWarmUp();
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

    private Task? Running() =>
        task is { IsCompleted: false } ? task : later is { IsCompleted: false } ? later : null;
}
