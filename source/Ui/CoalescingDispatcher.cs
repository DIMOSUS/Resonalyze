namespace Resonalyze;

/// <summary>Latest-wins marshalling from a high-rate producer (audio callbacks) with at most one dispatch in flight.</summary>
/// <remarks>Pass <paramref name="merge"/> when a superseded value carries something the newest lacks (e.g. a peak).</remarks>
internal sealed class CoalescingDispatcher<T>
{
    private readonly object sync = new();
    private readonly Func<Action, bool> tryPost;
    private readonly Action<T> apply;
    private readonly Func<T, T, T>? merge;
    private T pendingValue = default!;
    private bool dispatchQueued;

    /// <param name="tryPost">False when dispatch is impossible, releasing the queued flag for a later offer.</param>
    /// <param name="merge">Folds the undelivered value (first) into its successor (second).</param>
    public CoalescingDispatcher(
        Func<Action, bool> tryPost,
        Action<T> apply,
        Func<T, T, T>? merge = null)
    {
        this.tryPost = tryPost;
        this.apply = apply;
        this.merge = merge;
    }

    public void Offer(T value)
    {
        lock (sync)
        {
            pendingValue = dispatchQueued && merge != null
                ? merge(pendingValue, value)
                : value;
            if (dispatchQueued)
            {
                return;
            }

            dispatchQueued = true;
        }

        if (!tryPost(Drain))
        {
            lock (sync)
            {
                dispatchQueued = false;
            }
        }
    }

    private void Drain()
    {
        T value;
        lock (sync)
        {
            value = pendingValue;
            dispatchQueued = false;
        }

        apply(value);
    }
}
