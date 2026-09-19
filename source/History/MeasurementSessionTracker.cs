namespace Resonalyze.History;

/// <summary>Owns which history entry the open measurement belongs to; every transition goes through here. UI-thread only.</summary>
internal sealed class MeasurementSessionTracker
{
    private readonly MeasurementHistoryService history;
    private readonly AnalyzerDocument document;
    private readonly Func<MeasurementSessionSnapshot> captureSession;

    public MeasurementSessionTracker(
        MeasurementHistoryService history,
        AnalyzerDocument document,
        Func<MeasurementSessionSnapshot> captureSession)
    {
        this.history = history;
        this.document = document;
        this.captureSession = captureSession;
    }

    public Guid? CurrentEntryId { get; private set; }

    public void Reset()
    {
        CurrentEntryId = null;
    }

    public void MarkMeasurementCompleted(MeasurementResult result)
    {
        CurrentEntryId = history.AddMeasurement(result, captureSession());
    }

    public void MarkLoadedFile(string filePath, ImpulseResponseFile file, MeasurementResult result)
    {
        CurrentEntryId = history.AddOrUpdateLoadedFile(filePath, file, result, captureSession());
    }

    /// <summary>Creates a file-backed entry when nothing was current (e.g. the entry was deleted).</summary>
    public void MarkSavedFile(string filePath, ImpulseResponseFile file, MeasurementResult result)
    {
        if (CurrentEntryId.HasValue)
        {
            history.MarkSaved(
                CurrentEntryId.Value,
                filePath,
                file,
                result,
                captureSession());
        }
        else
        {
            CurrentEntryId = history.AddOrUpdateLoadedFile(
                filePath,
                file,
                result,
                captureSession());
        }
    }

    public void MarkRestored(Guid entryId)
    {
        CurrentEntryId = entryId;
    }

    /// <summary>The open measurement stays usable.</summary>
    public void ForgetEntry(Guid entryId)
    {
        if (CurrentEntryId == entryId)
        {
            CurrentEntryId = null;
        }
    }

    public void PersistCurrentSessionState()
    {
        if (!CurrentEntryId.HasValue || !document.HasResult)
        {
            return;
        }

        history.UpdateSession(CurrentEntryId.Value, captureSession());
    }
}
