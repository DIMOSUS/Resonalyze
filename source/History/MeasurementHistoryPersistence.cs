using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze.History;

internal sealed class MeasurementHistoryPersistence
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string pathOnDisk;

    public string? LoadWarning { get; private set; }

    public MeasurementHistoryPersistence(string? pathOnDisk = null)
    {
        this.pathOnDisk = pathOnDisk
            ?? ApplicationDataPaths.Current.HistoryFile;
    }

    public IReadOnlyList<MeasurementHistoryEntry> Load()
    {
        LoadWarning = null;
        try
        {
            if (!File.Exists(pathOnDisk))
            {
                return Array.Empty<MeasurementHistoryEntry>();
            }

            using FileStream stream = File.OpenRead(pathOnDisk);
            StoreFile? file = JsonSerializer.Deserialize<StoreFile>(stream, SerializerOptions);
            if (file == null)
            {
                throw new InvalidDataException("The history file is empty.");
            }
            if (file.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"History schema version {file.SchemaVersion} is not supported.");
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
        catch (Exception exception)
        {
            string? backupPath = BackupUnusableFile();
            string preservation = backupPath == null
                ? "The unusable file could not be backed up; check file permissions before saving history."
                : $"The unusable file was preserved as '{backupPath}'.";
            LoadWarning = $"Measurement history could not be loaded: {exception.Message}\r\n\r\n{preservation}";
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

        // Temp file + move keeps the store intact if the write is interrupted.
        string directory = Path.GetDirectoryName(pathOnDisk)
            ?? throw new InvalidOperationException("History directory cannot be resolved.");
        Directory.CreateDirectory(directory);
        string tempPath = pathOnDisk + ".tmp";
        using (FileStream stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, file, SerializerOptions);
        }

        File.Move(tempPath, pathOnDisk, overwrite: true);
    }

    private string? BackupUnusableFile()
    {
        try
        {
            if (!File.Exists(pathOnDisk))
            {
                return null;
            }

            string backupPath = GetAvailableBackupPath();
            File.Move(pathOnDisk, backupPath);
            return backupPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string GetAvailableBackupPath()
    {
        string backupPath = pathOnDisk + ".backup";
        for (int suffix = 1; File.Exists(backupPath); suffix++)
        {
            backupPath = pathOnDisk + $".backup.{suffix}";
        }

        return backupPath;
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
