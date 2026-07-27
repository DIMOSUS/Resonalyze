using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze.History;

internal sealed class MeasurementHistoryPersistence
{
    private const int CurrentSchemaVersion = 1;
    // The oldest schema this build can still read. Kept as its own constant so
    // the next bump is a migration rather than a mass move to .backup: with a
    // strict equality check, raising CurrentSchemaVersion would have sent every
    // user's whole history to a backup file on first launch of the new build.
    private const int MinimumSupportedSchemaVersion = 1;
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
    // Entries whose measurement file was not reachable at load. They are hidden
    // from the caller — nothing can be done with a snapshot whose file is gone —
    // but they are written back out on every Save. Without this an unmounted
    // external drive at startup silently truncated the history, and the next
    // ordinary window close persisted the truncation permanently.
    private List<PersistedEntry> unreachableEntries = [];

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
            if (file.SchemaVersion is < MinimumSupportedSchemaVersion or > CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"History schema version {file.SchemaVersion} is not supported.");
            }

            // Migration seam. Nothing to do while the only supported version is
            // the current one; a future bump adds its upgrade step here instead
            // of failing the load.
            file.SchemaVersion = CurrentSchemaVersion;

            var reachable = new List<MeasurementHistoryEntry>(file.Entries.Count);
            var unreachable = new List<PersistedEntry>();
            foreach (PersistedEntry entry in file.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.SourceFilePath))
                {
                    // No path at all is a broken record, not an absent drive:
                    // there is nothing to come back for, so it is dropped.
                    continue;
                }

                if (!File.Exists(entry.SourceFilePath))
                {
                    unreachable.Add(entry);
                    continue;
                }

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

            unreachableEntries = unreachable;
            if (unreachable.Count > 0)
            {
                LoadWarning =
                    $"{unreachable.Count} measurement(s) are not listed because their files " +
                    "could not be found — a disconnected drive or a moved folder does this.\r\n\r\n" +
                    "They are kept in the history file and reappear once the files are " +
                    "reachable again; nothing has been deleted.";
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

        // Carry the entries hidden at load back into the file. An Id that came
        // back (the drive was reconnected mid-session and the measurement was
        // re-added) is written from the live entry, not from the stale copy.
        if (unreachableEntries.Count > 0)
        {
            HashSet<Guid> live = persisted.Select(entry => entry.Id).ToHashSet();
            persisted.AddRange(unreachableEntries.Where(entry => !live.Contains(entry.Id)));
            // Newest first, matching how the service orders the live list.
            persisted = persisted.OrderByDescending(entry => entry.Timestamp).ToList();
        }

        StoreFile file = new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Entries = persisted
        };

        // Temp file + move keeps the store intact if the write is interrupted.
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
