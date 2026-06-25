namespace Resonalyze.History;

internal sealed class MeasurementHistoryEntry
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
    public string? SourceFilePath { get; set; }
    public required MeasurementHistorySnapshotMetadata Metadata { get; set; }
    public required MeasurementHistoryPreview Preview { get; set; }
    public MeasurementHistorySnapshot? Snapshot { get; set; }

    public bool IsFileBacked => !string.IsNullOrWhiteSpace(SourceFilePath);
    public bool CanSave => Snapshot != null && !IsFileBacked;
    public string FileNameOrDisplayName =>
        !string.IsNullOrWhiteSpace(SourceFilePath)
            ? Path.GetFileName(SourceFilePath)
            : DisplayName;
}
