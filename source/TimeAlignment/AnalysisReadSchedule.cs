namespace Resonalyze;

/// <summary>
/// Which read is drawn, which is running and which is waiting, for a panel
/// whose analysis is too slow to run inside the click that asks for it.
/// <para>
/// A request describes a whole read — the records and every setting it is taken
/// under — so two equal requests have one answer. That is what lets the panel
/// recognize the read already on screen (it is refreshed from more places than
/// there are changes to draw) and coalesce a held-down spinner into one read
/// instead of a queue of them.
/// </para>
/// <para>
/// The version is the whole point of the type: a read carries the one it was
/// started with, and only a read whose version is still current may be drawn.
/// Anything else describes a state the controls have left — including a read
/// still in flight when the user returns to what is ALREADY on screen, which is
/// otherwise the one path where a stale answer lands on top of a correct one.
/// </para>
/// </summary>
internal sealed class AnalysisReadSchedule<TRequest>
    where TRequest : struct
{
    private TRequest? drawn;
    private TRequest? running;
    private TRequest? queued;
    private int version;

    /// <summary>
    /// The version to run <paramref name="request"/> under, or null when there
    /// is nothing to run: it is already drawn, it is already running, or it has
    /// been queued behind the read in flight. Any read that no longer describes
    /// the request being submitted is retired here — dropped from the queue, or
    /// left to land on a version that has moved on.
    /// </summary>
    public int? Submit(TRequest request)
    {
        // Already on screen. Whatever is in flight or queued was asked for a
        // state of the controls that has since been left, so it must not be
        // allowed to land: retire it and keep the answer already drawn.
        if (drawn is { } current && Equals(current, request))
        {
            Retire();
            return null;
        }

        // Already on its way. Anything queued behind it is older than this
        // request by construction, so it goes.
        if (running is { } inFlight && Equals(inFlight, request))
        {
            queued = null;
            return null;
        }

        // One read at a time: a held spinner fires a request per click, and
        // starting each of them puts analyses on the pool to have all but the
        // last thrown away.
        if (running != null)
        {
            queued = request;
            return null;
        }

        version++;
        running = request;
        return version;
    }

    /// <summary>
    /// Whether a finished read may be drawn, given the version it started
    /// under. A read that may be drawn becomes the drawn one.
    /// </summary>
    public bool Accept(TRequest request, int readVersion)
    {
        if (readVersion != version)
        {
            return false;
        }

        running = null;
        drawn = request;
        return true;
    }

    /// <summary>
    /// The read that was asked for while the last one was running, and the
    /// version to run it under — or null when nothing waits, or when what waits
    /// is what has just been drawn.
    /// </summary>
    public int? TakeQueued(out TRequest request)
    {
        if (queued is not { } next)
        {
            request = default;
            return null;
        }

        queued = null;
        request = next;
        return Submit(next);
    }

    /// <summary>
    /// Forgets everything: nothing is drawn, and no read in flight may be. For
    /// the panel losing its record, where the answer to every request there was
    /// stops existing.
    /// </summary>
    public void Clear()
    {
        Retire();
        drawn = null;
    }

    private void Retire()
    {
        if (running != null)
        {
            // The version moves, so the read in flight fails its own Accept.
            version++;
            running = null;
        }

        queued = null;
    }

    private static bool Equals(TRequest left, TRequest right) =>
        EqualityComparer<TRequest>.Default.Equals(left, right);
}
