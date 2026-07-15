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
    // History operations save immediately, so a transient load/access failure
    // must not turn the empty recovery view into the persisted source of truth.
    private bool preserveExistingFileBeforeSave;

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
                preserveExistingFileBeforeSave = false;
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

            IReadOnlyList<MeasurementHistoryEntry> entries = file.Entries
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
            preserveExistingFileBeforeSave = false;
            return entries;
        }
        catch (Exception exception)
        {
            BackupResult backup = BackupUnusableFile();
            string? backupPath = backup.Path;
            string preservation = backupPath == null
                ? "The unusable file could not be backed up. History changes will not be saved " +
                    "until the original file can be preserved; check file permissions."
                : $"The unusable file was preserved as '{backupPath}'.";
            LoadWarning = $"Measurement history could not be loaded: {exception.Message}\r\n\r\n{preservation}";
            preserveExistingFileBeforeSave = backup.Status == BackupStatus.Failed;
            return Array.Empty<MeasurementHistoryEntry>();
        }
    }

    public void Save(IReadOnlyList<MeasurementHistoryEntry> entries)
    {
        if (preserveExistingFileBeforeSave)
        {
            BackupResult backup = BackupUnusableFile();
            if (backup.Status == BackupStatus.Failed)
            {
                return;
            }

            preserveExistingFileBeforeSave = false;
        }

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

    private BackupResult BackupUnusableFile()
    {
        try
        {
            if (!File.Exists(pathOnDisk))
            {
                return new BackupResult(BackupStatus.NotFound, null);
            }

            string backupPath = GetAvailableBackupPath();
            File.Move(pathOnDisk, backupPath);
            return new BackupResult(BackupStatus.Preserved, backupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new BackupResult(BackupStatus.Failed, null);
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

    private enum BackupStatus
    {
        NotFound,
        Preserved,
        Failed
    }

    private readonly record struct BackupResult(BackupStatus Status, string? Path);

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
