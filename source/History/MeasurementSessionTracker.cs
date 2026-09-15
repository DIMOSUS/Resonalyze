namespace Resonalyze.History;

/// <summary>Owns which history entry the loaded IR belongs to; every transition goes through here. UI-thread only.</summary>
internal sealed class MeasurementSessionTracker
{
    private readonly MeasurementHistoryService history;
    private readonly Func<MeasurementSessionSnapshot> captureSession;

    public MeasurementSessionTracker(
        MeasurementHistoryService history,
        Func<MeasurementSessionSnapshot> captureSession)
    {
        this.history = history;
        this.captureSession = captureSession;
    }

    public Guid? CurrentEntryId { get; private set; }

    public bool HasImpulseResponse { get; private set; }

    public void Reset()
    {
        CurrentEntryId = null;
        HasImpulseResponse = false;
    }

    public void SetImpulseResponseAvailable(bool available)
    {
        HasImpulseResponse = available;
    }

    public void MarkMeasurementCompleted(ExpSweepMeasurement measurement)
    {
        HasImpulseResponse = true;
        CurrentEntryId = history.AddMeasurement(measurement, captureSession());
    }

    public void MarkLoadedFile(string filePath, ImpulseResponseFile file)
    {
        HasImpulseResponse = true;
        CurrentEntryId = history.AddOrUpdateLoadedFile(filePath, file, captureSession());
    }

    /// <summary>Creates a file-backed entry when nothing was current (e.g. the entry was deleted).</summary>
    public void MarkSavedFile(string filePath, ImpulseResponseFile file)
    {
        if (CurrentEntryId.HasValue)
        {
            history.MarkSaved(
                CurrentEntryId.Value,
                filePath,
                file,
                captureSession());
        }
        else
        {
            CurrentEntryId = history.AddOrUpdateLoadedFile(
                filePath,
                file,
                captureSession());
        }
    }

    public void MarkRestored(Guid entryId)
    {
        HasImpulseResponse = true;
        CurrentEntryId = entryId;
    }

    /// <summary>The loaded impulse response stays usable.</summary>
    public void ForgetEntry(Guid entryId)
    {
        if (CurrentEntryId == entryId)
        {
            CurrentEntryId = null;
        }
    }

    public void PersistCurrentSessionState()
    {
        if (!CurrentEntryId.HasValue || !HasImpulseResponse)
        {
            return;
        }

        history.UpdateSession(CurrentEntryId.Value, captureSession());
    }
}
