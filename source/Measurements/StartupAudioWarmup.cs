namespace Resonalyze;

/// <summary>
/// Owns the one-shot, best-effort audio warm-up started when the shell is
/// first shown. The cancellation source and task used to live as raw fields
/// on <c>Form1</c>, cancelled from three different close paths; the pair is
/// managed in one place now. The warm-up body itself is injected so this
/// class stays free of device and UI concerns.
/// </summary>
internal sealed class StartupAudioWarmup : IDisposable
{
    private readonly Func<CancellationToken, Task> warmUp;
    private CancellationTokenSource? cancellation;
    private Task? task;

    public StartupAudioWarmup(Func<CancellationToken, Task> warmUp)
    {
        this.warmUp = warmUp;
    }

    /// <summary>Starts the warm-up once; later calls are no-ops.</summary>
    public void Start()
    {
        if (task != null)
        {
            return;
        }

        cancellation = new CancellationTokenSource();
        task = warmUp(cancellation.Token);
    }

    /// <summary>
    /// Completes when the warm-up has finished (immediately when it never
    /// started). Warm-up failures are intentionally non-fatal, so faults are
    /// swallowed here.
    /// </summary>
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
            // Warm-up failures are intentionally non-fatal.
        }
    }

    /// <summary>
    /// Requests cancellation without disposing — the fast OS-shutdown close
    /// path cancels and lets the process exit.
    /// </summary>
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
