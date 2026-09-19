namespace Resonalyze.History;

internal sealed class MeasurementHistoryEntry
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
    public string? SourceFilePath { get; set; }
    public required MeasurementHistorySnapshotMetadata Metadata { get; set; }
    public required MeasurementHistoryPreview Preview { get; set; }
    public MeasurementSessionSnapshot? Session { get; set; }
    /// <summary>Kept for unsaved entries and the one file-backed entry last opened; otherwise read back from the file.</summary>
    public MeasurementResult? Result { get; set; }

    public bool IsFileBacked => !string.IsNullOrWhiteSpace(SourceFilePath);
    public bool CanSave => Result != null && !IsFileBacked;
    public string FileNameOrDisplayName =>
        !string.IsNullOrWhiteSpace(SourceFilePath)
            ? Path.GetFileName(SourceFilePath)
            : DisplayName;
}
