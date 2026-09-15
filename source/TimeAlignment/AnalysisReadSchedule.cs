namespace Resonalyze;

/// <summary>Tracks the drawn, in-flight and desired reads of a slow panel analysis. Equal requests share one answer; the newest request is authoritative
/// (a superseded read fails <see cref="Complete"/> and is never drawn). One read at a time: call <see cref="Complete"/> then <see cref="TakeDesired"/> for every finished read.</summary>
internal sealed class AnalysisReadSchedule<TRequest>
    where TRequest : struct
{
    private TRequest? drawn;
    private TRequest? flight;
    private TRequest? desired;
    private int flightVersion;
    private int version;

    /// <summary>Version to run under, or null when already drawn, already in flight, or waiting for the pool.</summary>
    public int? Submit(TRequest request)
    {
        if (drawn is { } current && Equals(current, request))
        {
            desired = null;
            Retire();
            return null;
        }

        // Revive a just-retired in-flight read rather than recompute; no other version is live.
        if (flight is { } inFlight && Equals(inFlight, request))
        {
            version = flightVersion;
            desired = null;
            return null;
        }

        // Busy pool: retire the running read and wait for the slot (a held spinner fires per click).
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

    /// <summary>Call after every <see cref="Complete"/>, even a refused one: that is when the pool frees.</summary>
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
