namespace Resonalyze;

/// <summary>
/// The analyzer's open measurement: every analysis mode, Save, Send to REW and Time Alignment read it, and every
/// input (a run, a file, history, REW, a recorded sweep) replaces it. Written on the UI thread.
/// </summary>
/// <remarks>
/// Every input lands through a <see cref="Request"/> taken when it starts, and lands only while no newer one has been
/// taken since: whichever input started last wins, however long each takes. <see cref="Clear"/> counts as newer, so
/// nothing started before New session lands after it.
/// </remarks>
internal sealed class AnalyzerDocument
{
    private volatile MeasurementResult? result;
    private volatile bool busy;
    private long latest;

    /// <summary>Raised on the UI thread whenever the result or its name changes.</summary>
    public event Action? Changed;

    public MeasurementResult? Result => result;

    public bool HasResult => result != null;

    /// <summary>The file the result came from or was saved to, or a title for one with no file; null for a run not saved.</summary>
    public string? SourceName { get; private set; }

    /// <summary>A run or an import is producing the next result; nothing draws or saves the current one meanwhile.</summary>
    public bool IsBusy => busy;

    /// <summary>For a load, a history entry or a REW read; null while a run or an import holds the document.</summary>
    public Request? TryBegin() => busy ? null : new Request(this, ++latest, holds: false);

    /// <summary>
    /// For a run or an import: held until it installs or is disposed, so the record button, history, drops and other
    /// imports refuse meanwhile. Null while another one holds the document.
    /// </summary>
    public Request? TryAcquire()
    {
        if (busy)
        {
            return null;
        }

        busy = true;
        return new Request(this, ++latest, holds: true);
    }

    /// <summary>After a save the result is the file's.</summary>
    public void Rename(string? sourceName)
    {
        SourceName = sourceName;
        Changed?.Invoke();
    }

    /// <summary>Empties the document; every request taken before, a run's or an import's included, will not land.</summary>
    public void Clear()
    {
        ++latest;
        Replace(null, null);
    }

    private void Replace(MeasurementResult? measurement, string? sourceName)
    {
        result = measurement?.Validated();
        SourceName = sourceName;
        Changed?.Invoke();
    }

    internal sealed class Request : IDisposable
    {
        private readonly AnalyzerDocument owner;
        private readonly long id;
        private bool holding;

        public Request(AnalyzerDocument owner, long id, bool holds)
        {
            this.owner = owner;
            this.id = id;
            holding = holds;
        }

        /// <summary>No request has been taken, and the document not cleared, since this one.</summary>
        public bool IsCurrent => owner.latest == id;

        /// <summary>Releases the hold, then makes the result the open measurement unless a newer request was taken.</summary>
        /// <returns>False when superseded: the result is dropped, and the caller shows nothing of it.</returns>
        public bool Install(MeasurementResult measurement, string? sourceName)
        {
            Dispose();
            if (!IsCurrent)
            {
                return false;
            }

            owner.Replace(measurement, sourceName);
            return true;
        }

        public void Dispose()
        {
            if (holding)
            {
                holding = false;
                owner.busy = false;
            }
        }
    }
}
