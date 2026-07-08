namespace Resonalyze.History;

/// <summary>
/// Owns the "current measurement" identity that used to live as two raw
/// fields on <c>Form1</c>: which history entry the loaded impulse response
/// belongs to, and whether an impulse response is loaded at all. Every
/// transition that pairs those flags with a history-service call (finished
/// sweep, loaded/saved file, restored or deleted entry, fresh session) goes
/// through here, so the invariants live in one place. UI-thread only.
/// </summary>
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

    /// <summary>A sweep starts or a fresh session begins: nothing current.</summary>
    public void Reset()
    {
        CurrentEntryId = null;
        HasImpulseResponse = false;
    }

    public void SetImpulseResponseAvailable(bool available)
    {
        HasImpulseResponse = available;
    }

    /// <summary>
    /// A finished sweep becomes a new in-memory history entry and the
    /// current one.
    /// </summary>
    public void MarkMeasurementCompleted(ExpSweepMeasurement measurement)
    {
        HasImpulseResponse = true;
        CurrentEntryId = history.AddMeasurement(measurement, captureSession());
    }

    /// <summary>A file was loaded: its file-backed entry becomes current.</summary>
    public void MarkLoadedFile(string filePath, ImpulseResponseFile file)
    {
        HasImpulseResponse = true;
        CurrentEntryId = history.AddOrUpdateLoadedFile(filePath, file, captureSession());
    }

    /// <summary>
    /// The current measurement was saved to a file: the current entry is
    /// marked saved, or a file-backed entry is created and becomes current
    /// when nothing was current (an unsaved measurement loaded before the
    /// history service existed, or a deleted entry).
    /// </summary>
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

    /// <summary>A history entry was restored into the app and becomes current.</summary>
    public void MarkRestored(Guid entryId)
    {
        HasImpulseResponse = true;
        CurrentEntryId = entryId;
    }

    /// <summary>
    /// An entry was deleted: the current pointer is dropped but the loaded
    /// impulse response stays usable.
    /// </summary>
    public void ForgetEntry(Guid entryId)
    {
        if (CurrentEntryId == entryId)
        {
            CurrentEntryId = null;
        }
    }

    /// <summary>
    /// Writes the live working state (mode + per-mode settings + active
    /// overlays) back into the current entry. Safe to call when nothing is
    /// selected or no measurement is loaded.
    /// </summary>
    public void PersistCurrentSessionState()
    {
        if (!CurrentEntryId.HasValue || !HasImpulseResponse)
        {
            return;
        }

        history.UpdateSession(CurrentEntryId.Value, captureSession());
    }
}
