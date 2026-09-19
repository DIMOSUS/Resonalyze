namespace Resonalyze;

/// <summary>
/// The analyzer's open measurement: every analysis mode, Save, Send to REW and Time Alignment read it, and every
/// input (a run, a file, history, REW, a recorded sweep) replaces it. Written on the UI thread.
/// </summary>
internal sealed class AnalyzerDocument
{
    private volatile MeasurementResult? result;
    private volatile bool busy;
    private long activation;

    /// <summary>Raised on the UI thread whenever the result or its name changes.</summary>
    public event Action? Changed;

    public MeasurementResult? Result => result;

    public bool HasResult => result != null;

    /// <summary>The file the result came from or was saved to, or a title for one with no file; null for a run not saved.</summary>
    public string? SourceName { get; private set; }

    /// <summary>A run or an import is producing the next result; nothing draws or saves the current one meanwhile.</summary>
    public bool IsBusy => busy;

    /// <summary>Taken by every request that makes a measurement current, and checked across its awaits: the newest wins.</summary>
    public long BeginActivation() => ++activation;

    public bool IsCurrent(long token) => token == activation;

    /// <summary>Holds the document for a run or an import; nothing installs until the holder releases it.</summary>
    public IDisposable Acquire()
    {
        if (busy)
        {
            throw new InvalidOperationException("The measurement is already busy.");
        }

        busy = true;
        return new Acquisition(this);
    }

    public void Install(MeasurementResult measurement, string? sourceName)
    {
        if (busy)
        {
            throw new InvalidOperationException(
                "Cannot load an impulse response while a measurement is running.");
        }

        Replace(measurement, sourceName);
    }

    /// <summary>After a save the result is the file's.</summary>
    public void Rename(string? sourceName)
    {
        SourceName = sourceName;
        Changed?.Invoke();
    }

    public void Clear() => Replace(null, null);

    private void Replace(MeasurementResult? measurement, string? sourceName)
    {
        result = measurement?.Validated();
        SourceName = sourceName;
        Changed?.Invoke();
    }

    private sealed class Acquisition(AnalyzerDocument owner) : IDisposable
    {
        private AnalyzerDocument? owner = owner;

        public void Dispose()
        {
            if (owner != null)
            {
                owner.busy = false;
                owner = null;
            }
        }
    }
}
