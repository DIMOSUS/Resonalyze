namespace Resonalyze.Dsp;

/// <summary>
/// An <see cref="IProgress{T}"/> that invokes its handler synchronously on the
/// reporting thread.
/// <para>
/// <see cref="Progress{T}"/> posts through the SynchronizationContext captured
/// at construction; created on a worker thread that context is the thread pool,
/// which means "some pool thread, later". Chain two of those and reports
/// reorder freely — one stage's updates land after the next stage's, a progress
/// bar walks backwards, a "running" status overwrites "finished". Every
/// COMPOSITION layer must therefore relay inline with this type; only the one
/// outermost progress, created on the UI thread, may hop threads.
/// </para>
/// </summary>
public sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Action<T> handler =
        handler ?? throw new ArgumentNullException(nameof(handler));

    public void Report(T value) => handler(value);
}
