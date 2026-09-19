namespace Resonalyze.History;

internal sealed class MeasurementHistoryService
{
    public const int MaxInMemoryHistoryEntries = 10;
    // The whole list, saved rows included.
    public const int MaxHistoryEntries = 30;

    private readonly MeasurementHistoryPersistence persistence;
    private readonly List<MeasurementHistoryEntry> entries;

    public MeasurementHistoryService(MeasurementHistoryPersistence? persistence = null)
    {
        this.persistence = persistence ?? new MeasurementHistoryPersistence();
        entries = this.persistence.Load().ToList();
        // An older store may arrive over depth; cut now, since a read-only session never saves.
        if (TrimEntries())
        {
            SaveTrimmedEntries();
        }
    }

    public event Action? Changed;

    public IReadOnlyList<MeasurementHistoryEntry> Entries => entries;
    public string? LoadWarning => persistence.LoadWarning;

    public Guid AddMeasurement(
        MeasurementResult result,
        MeasurementSessionSnapshot session)
    {
        MeasurementHistoryEntry entry = CreateEntry(
            DateTimeOffset.Now,
            TimestampDisplayHelper.Format(DateTimeOffset.Now),
            sourceFilePath: null,
            result,
            MeasurementHistoryPreviewBuilder.Build(result),
            session);
        entries.Insert(0, entry);
        // The depth cap can push a saved row off the end, which must reach disk.
        if (TrimEntries())
        {
            SaveTrimmedEntries();
        }

        OnChanged();
        return entry.Id;
    }

    /// <param name="result">The file's result, read once by whoever opened it.</param>
    public Guid AddOrUpdateLoadedFile(
        string filePath,
        ImpulseResponseFile file,
        MeasurementResult result,
        MeasurementSessionSnapshot session)
    {
        MeasurementHistoryPreview preview = PreviewOf(file, result);
        MeasurementHistoryEntry? entry = FindBySourceFilePath(filePath);
        if (entry == null)
        {
            entry = CreateEntry(
                DateTimeOffset.Now,
                Path.GetFileName(filePath),
                filePath,
                result,
                preview,
                session);
            entries.Insert(0, entry);
        }
        else
        {
            entry.DisplayName = Path.GetFileName(filePath);
            entry.Timestamp = DateTimeOffset.Now;
            entry.SourceFilePath = filePath;
            Fill(entry, result, preview, session);
            MoveToStart(entry);
        }

        RetainSingleFileBackedResult(entry);
        TrimEntries();
        persistence.Save(entries);
        OnChanged();
        return entry.Id;
    }

    /// <param name="result">What was written to <paramref name="file"/>.</param>
    public void MarkSaved(
        Guid entryId,
        string filePath,
        ImpulseResponseFile file,
        MeasurementResult result,
        MeasurementSessionSnapshot? sessionOverride = null)
    {
        MeasurementHistoryEntry? existingEntry = FindById(entryId);
        MeasurementSessionSnapshot? session = sessionOverride ?? existingEntry?.Session;
        MeasurementHistoryPreview preview = PreviewOf(file, result);
        MeasurementHistoryEntry? duplicate = FindBySourceFilePath(filePath);
        if (duplicate != null && duplicate.Id != entryId)
        {
            entries.Remove(duplicate);
        }

        MeasurementHistoryEntry? entry = FindById(entryId);
        if (entry == null)
        {
            entry = CreateEntry(
                DateTimeOffset.Now,
                Path.GetFileName(filePath),
                filePath,
                result,
                preview,
                session);
            entries.Insert(0, entry);
        }
        else
        {
            entry.DisplayName = Path.GetFileName(filePath);
            entry.Timestamp = DateTimeOffset.Now;
            entry.SourceFilePath = filePath;
            Fill(entry, result, preview, session);
            MoveToStart(entry);
        }

        RetainSingleFileBackedResult(entry);
        TrimEntries();
        persistence.Save(entries);
        OnChanged();
    }

    public bool Delete(Guid entryId)
    {
        MeasurementHistoryEntry? entry = FindById(entryId);
        if (entry == null)
        {
            return false;
        }

        entries.Remove(entry);
        persistence.Save(entries);
        OnChanged();
        return true;
    }

    /// <summary>The entry's result; a file-backed one is read back from its file when its cache was dropped.</summary>
    public async Task<MeasurementResult?> GetResultAsync(Guid entryId)
    {
        MeasurementHistoryEntry? entry = FindById(entryId);
        if (entry == null)
        {
            return null;
        }

        if (entry.Result != null)
        {
            return entry.Result;
        }

        if (string.IsNullOrWhiteSpace(entry.SourceFilePath) ||
            !File.Exists(entry.SourceFilePath))
        {
            return null;
        }

        ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(entry.SourceFilePath);
        MeasurementResult result = file.ToResult();
        Fill(entry, result, PreviewOf(file, result), entry.Session);
        RetainSingleFileBackedResult(entry);
        return result;
    }

    public void UpdateSession(Guid entryId, MeasurementSessionSnapshot session)
    {
        MeasurementHistoryEntry? entry = FindById(entryId);
        if (entry == null)
        {
            return;
        }

        entry.Session = session;
        persistence.Save(entries);
    }

    public MeasurementHistoryEntry? FindById(Guid entryId) =>
        entries.FirstOrDefault(entry => entry.Id == entryId);

    private MeasurementHistoryEntry? FindBySourceFilePath(string filePath) =>
        entries.FirstOrDefault(entry =>
            !string.IsNullOrWhiteSpace(entry.SourceFilePath) &&
            string.Equals(
                entry.SourceFilePath,
                filePath,
                StringComparison.OrdinalIgnoreCase));

    private static MeasurementHistoryEntry CreateEntry(
        DateTimeOffset timestamp,
        string displayName,
        string? sourceFilePath,
        MeasurementResult result,
        MeasurementHistoryPreview preview,
        MeasurementSessionSnapshot? session)
    {
        return new MeasurementHistoryEntry
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            Timestamp = timestamp,
            SourceFilePath = sourceFilePath,
            Metadata = MeasurementHistorySnapshotMetadata.FromResult(result),
            Preview = preview,
            Session = session,
            Result = result
        };
    }

    private static void Fill(
        MeasurementHistoryEntry entry,
        MeasurementResult result,
        MeasurementHistoryPreview preview,
        MeasurementSessionSnapshot? session)
    {
        entry.Metadata = MeasurementHistorySnapshotMetadata.FromResult(result);
        entry.Preview = preview;
        entry.Session = session;
        entry.Result = result;
    }

    // A file keeps the preview it was saved with; one from before previews were stored gets it rebuilt.
    private static MeasurementHistoryPreview PreviewOf(ImpulseResponseFile file, MeasurementResult result) =>
        file.ToPreview() ?? MeasurementHistoryPreviewBuilder.Build(result);

    // Memory cap: unsaved results hold full IRs, so few are kept. Depth cap: over it the oldest file-backed
    // row goes even if an unsaved one is older (the file restores it; an unsaved row is the measurement).
    // The memory cap runs first (≤10 unsaved of 30). Returns whether a persisted row was removed.
    private bool TrimEntries()
    {
        while (entries.Count(entry => !entry.IsFileBacked) > MaxInMemoryHistoryEntries)
        {
            MeasurementHistoryEntry? oldestUnsaved = entries.LastOrDefault(entry => !entry.IsFileBacked);
            if (oldestUnsaved == null)
            {
                break;
            }

            entries.Remove(oldestUnsaved);
        }

        bool removedPersisted = false;
        while (entries.Count > MaxHistoryEntries)
        {
            MeasurementHistoryEntry? oldestSaved = entries.LastOrDefault(entry => entry.IsFileBacked);
            if (oldestSaved == null)
            {
                break;
            }

            entries.Remove(oldestSaved);
            removedPersisted = true;
        }

        return removedPersisted;
    }

    // Best effort: the list is already cut, and neither caller (shell initializer, finished sweep) can report it.
    private void SaveTrimmedEntries()
    {
        try
        {
            persistence.Save(entries);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Results hold full IRs (tens of MB): one file-backed entry keeps its cache; unsaved entries always keep theirs.
    private void RetainSingleFileBackedResult(MeasurementHistoryEntry keep)
    {
        foreach (MeasurementHistoryEntry entry in entries)
        {
            if (!ReferenceEquals(entry, keep) && entry.IsFileBacked)
            {
                entry.Result = null;
            }
        }
    }

    private void MoveToStart(MeasurementHistoryEntry entry)
    {
        if (entries.Remove(entry))
        {
            entries.Insert(0, entry);
        }
    }

    private void OnChanged()
    {
        Changed?.Invoke();
    }
}
