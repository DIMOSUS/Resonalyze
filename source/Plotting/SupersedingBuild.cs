namespace Resonalyze;

/// <summary>Builds on the thread pool, newest first: starting a build cancels the one in flight, and a superseded build
/// lands nothing, whatever it returned or threw.</summary>
internal sealed class SupersedingBuild
{
    // A source is cancelled and disposed only under this lock, so the two never overlap.
    private readonly object sync = new();
    private CancellationTokenSource? current;

    public void Cancel()
    {
        lock (sync)
        {
            current?.Cancel();
            current = null;
        }
    }

    /// <summary>Hands the result to <paramref name="land"/> on the caller's context unless a newer build or
    /// <see cref="Cancel"/> came first; faults when the build fails while still current.</summary>
    public async Task RunAsync<T>(Func<CancellationToken, T> build, Action<T> land)
    {
        var cancellation = new CancellationTokenSource();
        lock (sync)
        {
            current?.Cancel();
            current = cancellation;
        }

        Task<T> running = Task.Run(() => build(cancellation.Token), cancellation.Token);
        // Waits without throwing: a superseded build's failure is dropped with it.
        await Task.WhenAny(running);

        bool superseded;
        lock (sync)
        {
            // Superseded, the source was cancelled already; still current, it leaves, so nothing can cancel it now.
            superseded = current != cancellation;
            if (!superseded)
            {
                current = null;
            }

            cancellation.Dispose();
        }

        if (!superseded)
        {
            land(await running);
        }
    }
}
