namespace Resonalyze.Dsp;

/// <summary>Relays inline: <see cref="Progress{T}"/> created on a worker posts to the pool and chained layers reorder reports.
/// Only the outermost UI-thread progress may hop threads.</summary>
public sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Action<T> handler =
        handler ?? throw new ArgumentNullException(nameof(handler));

    public void Report(T value) => handler(value);
}
