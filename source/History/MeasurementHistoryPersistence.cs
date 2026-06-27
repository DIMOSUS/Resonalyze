using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze.History;

internal sealed class MeasurementHistoryPersistence
{
    private const int CurrentSchemaVersion = 1;
    private const string FileName = "measurement-history.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string PathOnDisk => Path.Combine(AppContext.BaseDirectory, FileName);

    public IReadOnlyList<MeasurementHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(PathOnDisk))
            {
                return Array.Empty<MeasurementHistoryEntry>();
            }

            using FileStream stream = File.OpenRead(PathOnDisk);
            StoreFile? file = JsonSerializer.Deserialize<StoreFile>(stream, SerializerOptions);
            if (file?.SchemaVersion != CurrentSchemaVersion)
            {
                return Array.Empty<MeasurementHistoryEntry>();
            }

            return file.Entries
                .Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.SourceFilePath) &&
                    File.Exists(entry.SourceFilePath))
                .Select(entry => new MeasurementHistoryEntry
                {
                    Id = entry.Id,
                    DisplayName = entry.DisplayName,
                    Timestamp = entry.Timestamp,
                    SourceFilePath = entry.SourceFilePath,
                    Metadata = entry.Metadata,
                    Preview = entry.Preview,
                    Session = entry.Session,
                    Snapshot = null
                })
                .ToArray();
        }
        catch
        {
            return Array.Empty<MeasurementHistoryEntry>();
        }
    }

    public void Save(IReadOnlyList<MeasurementHistoryEntry> entries)
    {
        StoreFile file = new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Entries = entries
                .Where(entry => entry.IsFileBacked)
                .Select(entry => new PersistedEntry
                {
                    Id = entry.Id,
                    DisplayName = entry.DisplayName,
                    Timestamp = entry.Timestamp,
                    SourceFilePath = entry.SourceFilePath!,
                    Metadata = entry.Metadata,
                    Preview = entry.Preview,
                    Session = entry.Session
                })
                .ToList()
        };

        using FileStream stream = File.Create(PathOnDisk);
        JsonSerializer.Serialize(stream, file, SerializerOptions);
    }

    private sealed class StoreFile
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<PersistedEntry> Entries { get; set; } = [];
    }

    private sealed class PersistedEntry
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public string SourceFilePath { get; set; } = string.Empty;
        public MeasurementHistorySnapshotMetadata Metadata { get; set; } = new()
        {
            Bits = 24,
            MeterSnapshot = InputLevelMeterSnapshot.Empty
        };
        public MeasurementHistoryPreview Preview { get; set; } = new();
        public MeasurementSessionSnapshot? Session { get; set; }
    }
}
