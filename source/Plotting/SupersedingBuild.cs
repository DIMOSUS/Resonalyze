namespace Resonalyze;

/// <summary>Builds on the thread pool, newest first: starting a build cancels the one in flight, and a superseded build
/// lands nothing, whatever it returned or threw.</summary>
internal sealed class SupersedingBuild
{
    private CancellationTokenSource? current;

    public void Cancel() => Interlocked.Exchange(ref current, null)?.Cancel();

    /// <summary>Hands the result to <paramref name="land"/> on the caller's context unless a newer build or
    /// <see cref="Cancel"/> came first; faults when the build fails while still current.</summary>
    public async Task RunAsync<T>(Func<CancellationToken, T> build, Action<T> land)
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref current, cancellation)?.Cancel();
        Task<T> running = Task.Run(() => build(cancellation.Token), cancellation.Token);
        // Waits without throwing: a superseded build's failure is dropped with it.
        await Task.WhenAny(running);

        // Only a build still current takes its source back; whoever superseded it took the source to cancel it.
        if (Interlocked.CompareExchange(ref current, null, cancellation) != cancellation)
        {
            return;
        }

        cancellation.Dispose();
        land(await running);
    }
}
