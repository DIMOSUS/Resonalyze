namespace Resonalyze;

/// <summary>
/// Marshals values from a high-rate producer to a consumer with latest-wins
/// coalescing: at most one dispatch is in flight at a time, and it always
/// delivers the most recent value. Audio callbacks can fire a thousand times
/// per second at small buffer sizes; posting every snapshot to the UI message
/// queue would flood the pump with stale updates.
/// </summary>
/// <remarks>
/// Latest-wins assumes a superseded value carries nothing the newest one does
/// not. When it does — a peak that only ever existed in the window that got
/// dropped — pass <paramref name="merge"/> to fold it in instead.
/// </remarks>
internal sealed class CoalescingDispatcher<T>
{
    private readonly object sync = new();
    private readonly Func<Action, bool> tryPost;
    private readonly Action<T> apply;
    private readonly Func<T, T, T>? merge;
    private T pendingValue = default!;
    private bool dispatchQueued;

    /// <param name="tryPost">
    /// Schedules the drain callback on the consumer thread; returns false when
    /// dispatch is impossible (e.g. the target handle is gone) so the queued
    /// flag is released and a later offer can try again.
    /// </param>
    /// <param name="apply">Receives the newest value on the consumer thread.</param>
    /// <param name="merge">
    /// Folds a still-undelivered value (first argument) into the one that
    /// supersedes it (second). Omit for plain latest-wins.
    /// </param>
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
            // Only a queued value is still pending delivery; once drained, the
            // field holds an already-applied value that must not be folded in.
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
