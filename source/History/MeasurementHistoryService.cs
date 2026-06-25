using System.Numerics;

namespace Resonalyze.History;

internal sealed class MeasurementHistoryService
{
    public const int MaxInMemoryHistoryEntries = 10;

    private readonly MeasurementHistoryPersistence persistence = new();
    private readonly List<MeasurementHistoryEntry> entries;

    public MeasurementHistoryService()
    {
        entries = persistence.Load().ToList();
    }

    public event Action? Changed;

    public IReadOnlyList<MeasurementHistoryEntry> Entries => entries;

    public Guid AddMeasurement(ExpSweepMeasurement measurement)
    {
        MeasurementHistorySnapshot snapshot = CreateSnapshot(measurement);
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

    public Guid AddOrUpdateLoadedFile(string filePath, ImpulseResponseFile file)
    {
        MeasurementHistorySnapshot snapshot = CreateSnapshot(file);
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
            entry.Snapshot = snapshot;
            MoveToStart(entry);
        }

        persistence.Save(entries);
        OnChanged();
        return entry.Id;
    }

    public void MarkSaved(Guid entryId, string filePath, ImpulseResponseFile file)
    {
        MeasurementHistorySnapshot snapshot = CreateSnapshot(file);
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
            entry.Snapshot = snapshot;
            MoveToStart(entry);
        }

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
        entry.Snapshot = CreateSnapshot(file);
        entry.Metadata = MeasurementHistorySnapshotMetadata.FromSnapshot(entry.Snapshot);
        entry.Preview = entry.Snapshot.Preview;
        return entry.Snapshot;
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
            Snapshot = snapshot
        };
    }

    private static MeasurementHistorySnapshot CreateSnapshot(ExpSweepMeasurement measurement)
    {
        Complex[] sweep = measurement.SweepDeconvolutionImpulseResponse
            ?? throw new InvalidOperationException("Measurement has no sweep-deconvolution IR.");
        Complex[]? transfer = measurement.TransferImpulseResponse?.ToArray();
        MeasurementHistoryPreview preview = MeasurementHistoryPreviewBuilder.Build(
            sweep,
            measurement.SweepDeconvolutionPeakIndex,
            measurement.SampleRate,
            measurement.MeasurementMode,
            transfer,
            transfer != null ? measurement.TransferPeakIndex : null);

        return new MeasurementHistorySnapshot
        {
            SampleRate = measurement.SampleRate,
            Bits = measurement.Bits,
            Octaves = measurement.Octaves,
            SweepDurationSeconds = measurement.Sweep?.ComputedDuration ?? 0.0,
            PlayChannel = measurement.PlaybackChannel,
            MeasurementMode = measurement.MeasurementMode,
            SweepDeconvolutionPeakIndex = measurement.SweepDeconvolutionPeakIndex,
            TransferPeakIndex = transfer != null ? measurement.TransferPeakIndex : null,
            SweepDeconvolutionImpulseResponse = sweep.ToArray(),
            TransferImpulseResponse = transfer,
            MeterSnapshot = measurement.CurrentLevels,
            Preview = preview
        };
    }

    private static MeasurementHistorySnapshot CreateSnapshot(ImpulseResponseFile file)
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
            SweepDeconvolutionImpulseResponse = sweep,
            TransferImpulseResponse = transfer,
            MeterSnapshot = file.GetMeterSnapshot(),
            Preview = preview
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
