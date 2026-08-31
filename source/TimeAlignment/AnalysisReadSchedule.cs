namespace Resonalyze;

/// <summary>
/// Which read is on screen, which is on the pool, and which one the controls
/// are actually asking for, for a panel whose analysis is far too slow to run
/// inside the click that asks for it.
/// <para>
/// A request describes a whole read — the records and every setting it is taken
/// under — so two equal requests have one answer. That is what lets the panel
/// recognize the read already on screen (it is refreshed from more places than
/// there are changes to draw) and fold a held-down spinner into one read
/// instead of a queue of them.
/// </para>
/// <para>
/// The rule the rest of it follows: <b>the newest request is authoritative</b>.
/// A read whose request is no longer what the controls ask for is retired the
/// moment that becomes true — the version moves, so the read fails its own
/// <see cref="Complete"/> and is never drawn. Not merely "eventually corrected
/// by the next read": a result that does not answer the controls in front of
/// the user is a panel lying about its own state, and the window in which it
/// could be read is exactly the window this whole background pass opened.
/// </para>
/// <para>
/// One read runs at a time. A retired read still holds the pool until it
/// finishes — it cannot be cancelled — so the desired one starts when it lands,
/// which is why <see cref="Complete"/> must be called for every finished read
/// and <see cref="TakeDesired"/> after it, drawn or not.
/// </para>
/// </summary>
internal sealed class AnalysisReadSchedule<TRequest>
    where TRequest : struct
{
    private TRequest? drawn;
    private TRequest? flight;
    private TRequest? desired;
    private int flightVersion;
    private int version;

    /// <summary>
    /// The version to run <paramref name="request"/> under, or null when it is
    /// not this call's job to run it: it is already drawn, it is already the
    /// read in flight, or it has to wait for the pool. Every read this request
    /// supersedes is retired here.
    /// </summary>
    public int? Submit(TRequest request)
    {
        // Already on screen: whatever else is happening was asked for a state
        // of the controls that has since been left.
        if (drawn is { } current && Equals(current, request))
        {
            desired = null;
            Retire();
            return null;
        }

        // Back to the read already on the pool. If it was retired a moment ago
        // it is revived rather than recomputed — it answers exactly what is
        // being asked for again, and one read runs at a time, so no other
        // version is live to collide with its own.
        if (flight is { } inFlight && Equals(inFlight, request))
        {
            version = flightVersion;
            desired = null;
            return null;
        }

        // Something else holds the pool. It cannot be cancelled, but it can be
        // retired, and this request waits for the slot rather than doubling up
        // on it — a held spinner fires a request per click.
        if (flight != null)
        {
            desired = request;
            Retire();
            return null;
        }

        desired = null;
        version++;
        flight = request;
        flightVersion = version;
        return version;
    }

    /// <summary>
    /// Hands the pool slot back and says whether the finished read may be
    /// drawn — only a read still running under the current version may. A read
    /// that may be drawn becomes the drawn one.
    /// </summary>
    public bool Complete(TRequest request, int readVersion)
    {
        flight = null;
        if (readVersion != version)
        {
            return false;
        }

        drawn = request;
        desired = null;
        return true;
    }

    /// <summary>
    /// The read the controls are still waiting for, and the version to run it
    /// under — or null when what is drawn is what they ask for. Call after
    /// every <see cref="Complete"/>, including one that refused to draw: that
    /// is the moment the pool frees.
    /// </summary>
    public int? TakeDesired(out TRequest request)
    {
        if (desired is not { } next)
        {
            request = default;
            return null;
        }

        request = next;
        return Submit(next);
    }

    /// <summary>
    /// Forgets everything: nothing is drawn, nothing is wanted, and no read in
    /// flight may be drawn. For the panel losing its record, where the answer
    /// to every request there was stops existing.
    /// </summary>
    public void Clear()
    {
        desired = null;
        drawn = null;
        Retire();
    }

    private void Retire()
    {
        if (flight != null && flightVersion == version)
        {
            version++;
        }
    }

    private static bool Equals(TRequest left, TRequest right) =>
        EqualityComparer<TRequest>.Default.Equals(left, right);
}
