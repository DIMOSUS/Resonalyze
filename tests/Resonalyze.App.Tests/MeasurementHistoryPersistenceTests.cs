using Resonalyze.History;

namespace Resonalyze.App.Tests;

public sealed class MeasurementHistoryPersistenceTests : IDisposable
{
    private readonly string directory;
    private readonly string storePath;

    public MeasurementHistoryPersistenceTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        storePath = Path.Combine(directory, "measurement-history.json");
    }

    [Fact]
    public void Load_PreservesEarlierBackupOnRepeatedCorruption()
    {
        File.WriteAllText(storePath + ".backup", "earlier corruption");
        File.WriteAllText(storePath, "{ later corruption");
        var persistence = new MeasurementHistoryPersistence(storePath);

        persistence.Load();

        Assert.Equal("earlier corruption", File.ReadAllText(storePath + ".backup"));
        Assert.Equal("{ later corruption", File.ReadAllText(storePath + ".backup.1"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsFileBackedEntries()
    {
        string sourcePath = Path.Combine(directory, "measurement.json");
        File.WriteAllText(sourcePath, "{}");
        var persistence = new MeasurementHistoryPersistence(storePath);
        MeasurementHistoryEntry entry = CreateEntry(sourcePath);

        persistence.Save(new[] { entry });
        IReadOnlyList<MeasurementHistoryEntry> loaded = persistence.Load();

        Assert.Single(loaded);
        Assert.Equal(entry.Id, loaded[0].Id);
        Assert.Equal(sourcePath, loaded[0].SourceFilePath);
        Assert.Null(loaded[0].Snapshot);
        // The atomic write must not leave its temp file behind.
        Assert.DoesNotContain(
            Directory.GetFiles(directory),
            file => file.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_SkipsInMemoryEntries()
    {
        var persistence = new MeasurementHistoryPersistence(storePath);

        persistence.Save(new[] { CreateEntry(sourceFilePath: null) });

        Assert.Empty(persistence.Load());
    }

    [Fact]
    public void Load_HidesEntriesWhoseSourceFileIsMissing_AndSaysSo()
    {
        string sourcePath = Path.Combine(directory, "deleted.json");
        File.WriteAllText(sourcePath, "{}");
        var persistence = new MeasurementHistoryPersistence(storePath);
        persistence.Save(new[] { CreateEntry(sourcePath) });
        File.Delete(sourcePath);

        Assert.Empty(persistence.Load());
        Assert.NotNull(persistence.LoadWarning);
    }

    /// <summary>
    /// The field case this guards: an external drive is not mounted at startup,
    /// so the measurements on it vanish from the list — and then any ordinary
    /// window close writes the truncated list back, deleting them for good.
    /// </summary>
    [Fact]
    public void Save_AfterAnUnreachableFileWasHidden_DoesNotDropItFromTheStore()
    {
        string reachablePath = Path.Combine(directory, "reachable.json");
        string unreachablePath = Path.Combine(directory, "on-external-drive.json");
        File.WriteAllText(reachablePath, "{}");
        File.WriteAllText(unreachablePath, "{}");

        MeasurementHistoryEntry reachable = CreateEntry(reachablePath);
        MeasurementHistoryEntry unreachable = CreateEntry(unreachablePath);
        var first = new MeasurementHistoryPersistence(storePath);
        first.Save(new[] { reachable, unreachable });

        // The drive goes away, the app restarts, and the user does anything at
        // all that saves — here, the still-visible entry alone is written back.
        File.Delete(unreachablePath);
        var second = new MeasurementHistoryPersistence(storePath);
        IReadOnlyList<MeasurementHistoryEntry> visible = second.Load();
        Assert.Equal(reachable.Id, Assert.Single(visible).Id);
        second.Save(visible);

        // The drive comes back.
        File.WriteAllText(unreachablePath, "{}");
        var third = new MeasurementHistoryPersistence(storePath);
        IReadOnlyList<Guid> restored = third.Load().Select(entry => entry.Id).ToList();

        Assert.Equal(2, restored.Count);
        Assert.Contains(unreachable.Id, restored);
        Assert.Contains(reachable.Id, restored);
        Assert.Null(third.LoadWarning);
    }

    /// <summary>
    /// The drive comes back mid-session and the user opens one of its files. The
    /// service looks the path up in the LIVE list, cannot see the hidden entry,
    /// and creates a fresh one with a new id — so the retained copy must be
    /// matched by path too, or the measurement shows up twice after a restart.
    /// </summary>
    [Fact]
    public void Save_WhenAHiddenFileIsReopenedUnderANewId_DoesNotDuplicateIt()
    {
        string sourcePath = Path.Combine(directory, "on-external-drive.json");
        File.WriteAllText(sourcePath, "{}");
        var first = new MeasurementHistoryPersistence(storePath);
        first.Save(new[] { CreateEntry(sourcePath) });

        File.Delete(sourcePath);
        var second = new MeasurementHistoryPersistence(storePath);
        Assert.Empty(second.Load());

        // Drive back; the service re-adds the same file as a brand new entry.
        File.WriteAllText(sourcePath, "{}");
        MeasurementHistoryEntry reopened = CreateEntry(sourcePath);
        second.Save(new[] { reopened });

        var third = new MeasurementHistoryPersistence(storePath);
        MeasurementHistoryEntry only = Assert.Single(third.Load());
        Assert.Equal(reopened.Id, only.Id);
    }

    [Fact]
    public void Save_WhenAHiddenFileIsReopened_MatchesThePathCaseInsensitively()
    {
        string sourcePath = Path.Combine(directory, "Mixed Case.json");
        File.WriteAllText(sourcePath, "{}");
        var first = new MeasurementHistoryPersistence(storePath);
        first.Save(new[] { CreateEntry(sourcePath) });

        File.Delete(sourcePath);
        var second = new MeasurementHistoryPersistence(storePath);
        second.Load();

        File.WriteAllText(sourcePath, "{}");
        second.Save(new[] { CreateEntry(sourcePath.ToUpperInvariant()) });

        var third = new MeasurementHistoryPersistence(storePath);
        Assert.Single(third.Load());
    }

    [Fact]
    public void Save_DoesNotResurrectAnEntryDeletedWhileItWasReachable()
    {
        string keptPath = Path.Combine(directory, "kept.json");
        string removedPath = Path.Combine(directory, "removed.json");
        File.WriteAllText(keptPath, "{}");
        File.WriteAllText(removedPath, "{}");

        MeasurementHistoryEntry kept = CreateEntry(keptPath);
        MeasurementHistoryEntry removed = CreateEntry(removedPath);
        var persistence = new MeasurementHistoryPersistence(storePath);
        persistence.Save(new[] { kept, removed });

        // Both files are present, so nothing is retained; dropping an entry from
        // the list is a real delete and must stick.
        var reopened = new MeasurementHistoryPersistence(storePath);
        Assert.Equal(2, reopened.Load().Count);
        reopened.Save(new[] { kept });

        var third = new MeasurementHistoryPersistence(storePath);
        Assert.Equal(kept.Id, Assert.Single(third.Load()).Id);
    }

    [Fact]
    public void Load_ReturnsEmptyOnCorruptedStore()
    {
        File.WriteAllText(storePath, "not json at all {{{");

        var persistence = new MeasurementHistoryPersistence(storePath);

        Assert.Empty(persistence.Load());
        Assert.NotNull(persistence.LoadWarning);
        Assert.False(File.Exists(storePath));
        Assert.True(File.Exists(storePath + ".backup"));
    }

    [Fact]
    public void Load_ReportsAndBacksUpUnsupportedStore()
    {
        File.WriteAllText(storePath, "{ \"schemaVersion\": 999, \"entries\": [] }");
        var persistence = new MeasurementHistoryPersistence(storePath);

        Assert.Empty(persistence.Load());
        Assert.Contains("version 999 is not supported", persistence.LoadWarning);
        Assert.True(File.Exists(storePath + ".backup"));
    }

    [Fact]
    public void Save_DoesNotOverwriteStoreThatCouldNotBeReadOrBackedUp()
    {
        const string original = "{ \"schemaVersion\": 1, \"entries\": [] }";
        File.WriteAllText(storePath, original);
        var persistence = new MeasurementHistoryPersistence(storePath);

        using (File.Open(storePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(persistence.Load());
            persistence.Save(Array.Empty<MeasurementHistoryEntry>());

            Assert.True(File.Exists(storePath));
            Assert.False(File.Exists(storePath + ".backup"));
        }

        Assert.Equal(original, File.ReadAllText(storePath));
        persistence.Save(Array.Empty<MeasurementHistoryEntry>());

        Assert.Equal(original, File.ReadAllText(storePath + ".backup"));
        Assert.True(File.Exists(storePath));
    }

    internal static MeasurementHistoryEntry CreateEntry(string? sourceFilePath) =>
        new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "entry",
            Timestamp = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero),
            SourceFilePath = sourceFilePath,
            Metadata = new MeasurementHistorySnapshotMetadata
            {
                Bits = 24,
                MeterSnapshot = InputLevelMeterSnapshot.Empty
            },
            Preview = new MeasurementHistoryPreview()
        };
}
