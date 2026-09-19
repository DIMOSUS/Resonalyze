namespace Resonalyze;

/// <summary>
/// Turns change notifications on the UI thread into one refresh after the work in progress, so an input that installs a
/// measurement and then changes the view draws once, with the final state.
/// </summary>
/// <remarks>
/// A refresh the view makes on its own (a mode switch draws) calls <see cref="Refreshed"/>, and the queued one then has
/// nothing left to do. Not for values arriving from other threads: that is <see cref="CoalescingDispatcher{T}"/>.
/// </remarks>
internal sealed class DeferredRefresh
{
    private readonly Control owner;
    private readonly Action refresh;
    private bool changed;
    private bool queued;

    public DeferredRefresh(Control owner, Action refresh)
    {
        this.owner = owner;
        this.refresh = refresh;
    }

    /// <summary>Something the view shows changed; it refreshes once the UI thread is done with the current work.</summary>
    public void Request()
    {
        changed = true;
        if (queued || owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }

        queued = true;
        owner.BeginInvoke(Run);
    }

    /// <summary>The view has just read what it shows; a queued refresh is no longer needed.</summary>
    public void Refreshed() => changed = false;

    private void Run()
    {
        queued = false;
        if (changed && !owner.IsDisposed)
        {
            refresh();
        }
    }
}
