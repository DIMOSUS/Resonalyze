namespace Resonalyze;

/// <summary>One-shot best-effort audio warm-up; body injected to keep device and UI concerns out.</summary>
internal sealed class StartupAudioWarmup : IDisposable
{
    private readonly Func<CancellationToken, Task> warmUp;
    private CancellationTokenSource? cancellation;
    private Task? task;

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

    /// <summary>Completes immediately when never started; faults are swallowed (non-fatal).</summary>
    public async Task WaitAsync()
    {
        Task? started = task;
        if (started == null || started.IsCompleted)
        {
            return;
        }

        try
        {
            await started;
        }
        catch
        {
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
}
