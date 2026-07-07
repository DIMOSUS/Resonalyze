using System.Numerics;

namespace Resonalyze.History;

internal sealed class MeasurementHistoryService
{
    public const int MaxInMemoryHistoryEntries = 10;

    private readonly MeasurementHistoryPersistence persistence;
    private readonly List<MeasurementHistoryEntry> entries;

    public MeasurementHistoryService(MeasurementHistoryPersistence? persistence = null)
    {
        this.persistence = persistence ?? new MeasurementHistoryPersistence();
        entries = this.persistence.Load().ToList();
    }

    public event Action? Changed;

    public IReadOnlyList<MeasurementHistoryEntry> Entries => entries;

    public Guid AddMeasurement(
        ExpSweepMeasurement measurement,
        MeasurementSessionSnapshot session)
    {
        MeasurementHistorySnapshot snapshot = CreateSnapshot(measurement, session);
        MeasurementHistoryEntry entry = CreateEntry(
            DateTimeOffset.Now,
            TimestampDisplayHelper.Format(DateTimeOffset.Now),
            sourceFilePath: null,
            snapshot);
        entries.Insert(0, entry);
        TrimInMemoryEntries();
        OnChanged();
        return entry.Id;
    }

    public Guid AddOrUpdateLoadedFile(
        string filePath,
        ImpulseResponseFile file,
        MeasurementSessionSnapshot session)
    {
        MeasurementHistorySnapshot snapshot = CreateSnapshot(file, session);
        MeasurementHistoryEntry? entry = FindBySourceFilePath(filePath);
        if (entry == null)
        {
            entry = CreateEntry(
                DateTimeOffset.Now,
                Path.GetFileName(filePath),
                filePath,
                snapshot);
            entries.Insert(0, entry);
        }
        else
        {
            entry.DisplayName = Path.GetFileName(filePath);
            entry.Timestamp = DateTimeOffset.Now;
            entry.SourceFilePath = filePath;
            entry.Metadata = MeasurementHistorySnapshotMetadata.FromSnapshot(snapshot);
            entry.Preview = snapshot.Preview;
            entry.Session = snapshot.Session;
            entry.Snapshot = snapshot;
            MoveToStart(entry);
        }

        RetainSingleFileBackedSnapshot(entry);
        persistence.Save(entries);
        OnChanged();
        return entry.Id;
    }

    public void MarkSaved(
        Guid entryId,
        string filePath,
        ImpulseResponseFile file,
        MeasurementSessionSnapshot? sessionOverride = null)
    {
        MeasurementHistoryEntry? existingEntry = FindById(entryId);
        MeasurementSessionSnapshot? session = sessionOverride ??
            existingEntry?.Session ??
            existingEntry?.Snapshot?.Session;
        MeasurementHistorySnapshot snapshot = CreateSnapshot(file, session);
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
                snapshot);
            entries.Insert(0, entry);
        }
        else
        {
            entry.DisplayName = Path.GetFileName(filePath);
            entry.Timestamp = DateTimeOffset.Now;
            entry.SourceFilePath = filePath;
            entry.Metadata = MeasurementHistorySnapshotMetadata.FromSnapshot(snapshot);
            entry.Preview = snapshot.Preview;
            entry.Session = snapshot.Session;
            entry.Snapshot = snapshot;
            MoveToStart(entry);
        }

        RetainSingleFileBackedSnapshot(entry);
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

    public async Task<MeasurementHistorySnapshot?> GetSnapshotAsync(Guid entryId)
    {
        MeasurementHistoryEntry? entry = FindById(entryId);
        if (entry == null)
        {
            return null;
        }

        if (entry.Snapshot != null)
        {
            return entry.Snapshot;
        }

        if (string.IsNullOrWhiteSpace(entry.SourceFilePath) ||
            !File.Exists(entry.SourceFilePath))
        {
            return null;
        }

        ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(entry.SourceFilePath);
        entry.Snapshot = CreateSnapshot(file, entry.Session);
        entry.Metadata = MeasurementHistorySnapshotMetadata.FromSnapshot(entry.Snapshot);
        entry.Preview = entry.Snapshot.Preview;
        RetainSingleFileBackedSnapshot(entry);
        return entry.Snapshot;
    }

    // Stores the latest live working state into an entry so returning to it later
    // restores what the user was actually doing, not the state at save time.
    public void UpdateSession(Guid entryId, MeasurementSessionSnapshot session)
    {
        MeasurementHistoryEntry? entry = FindById(entryId);
        if (entry == null)
        {
            return;
        }

        entry.Session = session;
        if (entry.Snapshot != null)
        {
            entry.Snapshot.Session = session;
        }

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
        MeasurementHistorySnapshot snapshot)
    {
        return new MeasurementHistoryEntry
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            Timestamp = timestamp,
            SourceFilePath = sourceFilePath,
            Metadata = MeasurementHistorySnapshotMetadata.FromSnapshot(snapshot),
            Preview = snapshot.Preview,
            Session = snapshot.Session,
            Snapshot = snapshot
        };
    }

    private static MeasurementHistorySnapshot CreateSnapshot(
        ExpSweepMeasurement measurement,
        MeasurementSessionSnapshot? session)
    {
        MeasurementImpulseResponse sweepDeconvolution = measurement.SweepDeconvolution
            ?? throw new InvalidOperationException("Measurement has no sweep-deconvolution IR.");
        MeasurementImpulseResponse? transferResult = measurement.Transfer;
        Complex[] sweep = sweepDeconvolution.ImpulseResponse;
        Complex[]? transfer = transferResult?.ImpulseResponse.ToArray();
        MeasurementHistoryPreview preview = MeasurementHistoryPreviewBuilder.Build(
            sweep,
            sweepDeconvolution.PeakIndex,
            measurement.SampleRate,
            measurement.MeasurementMode,
            transfer,
            transferResult?.PeakIndex);

        return new MeasurementHistorySnapshot
        {
            SampleRate = measurement.SampleRate,
            Bits = measurement.Bits,
            Octaves = measurement.Octaves,
            SweepDurationSeconds = measurement.Sweep?.ComputedDuration ?? 0.0,
            PlayChannel = measurement.PlaybackChannel,
            MeasurementMode = measurement.MeasurementMode,
            SweepDeconvolutionPeakIndex = sweepDeconvolution.PeakIndex,
            TransferPeakIndex = transferResult?.PeakIndex,
            AverageRunCount = measurement.AverageRunCount,
            AcceptedAverageRunCount = measurement.AcceptedAverageRunCount,
            SweepDeconvolutionImpulseResponse = sweep.ToArray(),
            TransferImpulseResponse = transfer,
            TransferCoherence = measurement.TransferCoherence?.ToArray(),
            MeterSnapshot = measurement.CurrentLevels,
            Preview = preview,
            Session = session
        };
    }

    internal static MeasurementHistorySnapshot CreateSnapshot(
        ImpulseResponseFile file,
        MeasurementSessionSnapshot? session = null)
    {
        Complex[] sweep = file.GetSweepDeconvolutionImpulseResponse();
        Complex[]? transfer = file.GetTransferImpulseResponse();
        MeasurementHistoryPreview preview = file.ToPreview() ??
            MeasurementHistoryPreviewBuilder.Build(
                sweep,
                file.SweepDeconvolutionPeakIndex,
                file.SampleRate,
                file.MeasurementMode,
                transfer,
                file.TransferPeakIndex);

        return new MeasurementHistorySnapshot
        {
            SampleRate = file.SampleRate,
            Bits = file.Bits,
            Octaves = file.Octaves,
            SweepDurationSeconds = file.SweepDurationSeconds,
            PlayChannel = file.PlayChannel,
            MeasurementMode = file.MeasurementMode,
            SweepDeconvolutionPeakIndex = file.SweepDeconvolutionPeakIndex,
            TransferPeakIndex = file.TransferPeakIndex,
            AverageRunCount = file.AverageRunCount,
            AcceptedAverageRunCount = file.AcceptedAverageRunCount,
            SweepDeconvolutionImpulseResponse = sweep,
            TransferImpulseResponse = transfer,
            TransferCoherence = file.TransferCoherence?.ToArray(),
            MeterSnapshot = file.GetMeterSnapshot(),
            Preview = preview,
            Session = session
        };
    }

    private void TrimInMemoryEntries()
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
    }

    // A snapshot holds the complete impulse responses (tens of megabytes), so at
    // most one file-backed entry keeps one cached — its file remains the source
    // of truth and an evicted snapshot is reloaded on demand. Unsaved entries
    // always keep theirs: memory is their only storage.
    private void RetainSingleFileBackedSnapshot(MeasurementHistoryEntry keep)
    {
        foreach (MeasurementHistoryEntry entry in entries)
        {
            if (!ReferenceEquals(entry, keep) && entry.IsFileBacked)
            {
                entry.Snapshot = null;
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
