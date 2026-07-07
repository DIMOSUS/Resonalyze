namespace Resonalyze;

/// <summary>
/// Marshals values from a high-rate producer to a consumer with latest-wins
/// coalescing: at most one dispatch is in flight at a time, and it always
/// delivers the most recent value. Audio callbacks can fire a thousand times
/// per second at small buffer sizes; posting every snapshot to the UI message
/// queue would flood the pump with stale updates.
/// </summary>
internal sealed class CoalescingDispatcher<T>
{
    private readonly object sync = new();
    private readonly Func<Action, bool> tryPost;
    private readonly Action<T> apply;
    private T pendingValue = default!;
    private bool dispatchQueued;

    /// <param name="tryPost">
    /// Schedules the drain callback on the consumer thread; returns false when
    /// dispatch is impossible (e.g. the target handle is gone) so the queued
    /// flag is released and a later offer can try again.
    /// </param>
    /// <param name="apply">Receives the newest value on the consumer thread.</param>
    public CoalescingDispatcher(Func<Action, bool> tryPost, Action<T> apply)
    {
        this.tryPost = tryPost;
        this.apply = apply;
    }

    public void Offer(T value)
    {
        lock (sync)
        {
            pendingValue = value;
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
