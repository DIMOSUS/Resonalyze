using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze.History;

internal sealed class MeasurementHistoryPersistence
{
    private const int CurrentSchemaVersion = 1;
    // Separate from CurrentSchemaVersion so a bump is a migration, not a move of all history to .backup.
    private const int MinimumSupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string pathOnDisk;
    // Saves are immediate, so a transient load failure must not persist the empty recovery view.
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

            // A block: the stream must close before the rewrite below, or the atomic replace hits our own handle.
            StoreFile? file;
            using (FileStream stream = File.OpenRead(pathOnDisk))
            {
                file = JsonSerializer.Deserialize<StoreFile>(stream, SerializerOptions);
            }

            if (file == null)
            {
                throw new InvalidDataException("The history file is empty.");
            }
            if (file.SchemaVersion is < MinimumSupportedSchemaVersion or > CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"History schema version {file.SchemaVersion} is not supported.");
            }

            // Migration seam for future schema bumps.
            file.SchemaVersion = CurrentSchemaVersion;

            var reachable = new List<MeasurementHistoryEntry>(file.Entries.Count);
            var retained = new List<PersistedEntry>(file.Entries.Count);
            int removedCount = 0;
            bool storeChanged = false;
            foreach (PersistedEntry entry in file.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.SourceFilePath))
                {
                    // Dropped silently, but marks the store dirty so it does not linger in the JSON.
                    storeChanged = true;
                    continue;
                }

                if (!File.Exists(entry.SourceFilePath))
                {
                    removedCount++;
                    storeChanged = true;
                    continue;
                }

                retained.Add(entry);
                reachable.Add(new MeasurementHistoryEntry
                {
                    Id = entry.Id,
                    DisplayName = entry.DisplayName,
                    Timestamp = entry.Timestamp,
                    SourceFilePath = entry.SourceFilePath,
                    Metadata = entry.Metadata,
                    Preview = entry.Preview,
                    Session = entry.Session,
                    Snapshot = null
                });
            }

            if (storeChanged)
            {
                // Drop rows whose file is gone now (a session without changes never saves, so the warning would repeat).
                // Best effort: the next launch repeats the removal.
                try
                {
                    WriteStore(retained);
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            if (removedCount > 0)
            {
                LoadWarning =
                    $"{removedCount} measurement(s) were removed from the history " +
                    "because their files no longer exist (deleted, or a moved " +
                    "folder).\r\n\r\nOpening a measurement file adds it back to " +
                    "the history.";
            }

            preserveExistingFileBeforeSave = false;
            return reachable;
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

        List<PersistedEntry> persisted = entries
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
            .ToList();

        WriteStore(persisted);
    }

    private void WriteStore(List<PersistedEntry> entries)
    {
        StoreFile file = new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Entries = entries
        };

        AtomicFile.Write(
            pathOnDisk,
            stream => JsonSerializer.Serialize(stream, file, SerializerOptions));
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
