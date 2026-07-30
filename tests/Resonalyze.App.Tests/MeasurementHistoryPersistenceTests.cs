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
    public void Load_RemovesEntriesWhoseSourceFileIsMissing_AndSaysSo()
    {
        string sourcePath = Path.Combine(directory, "deleted.json");
        File.WriteAllText(sourcePath, "{}");
        var persistence = new MeasurementHistoryPersistence(storePath);
        persistence.Save(new[] { CreateEntry(sourcePath) });
        File.Delete(sourcePath);

        Assert.Empty(persistence.Load());
        Assert.Contains("removed", persistence.LoadWarning);
    }

    /// <summary>
    /// The owner's policy: a history row whose measurement file is gone is dead
    /// weight and is dropped AT LOAD, with the store rewritten immediately — a
    /// session without history mutations never saves, and the removal (and its
    /// one-time message) must not depend on one. The previous design retained
    /// such rows for the unplugged-drive case, which greeted every launch with
    /// the same warning.
    /// </summary>
    [Fact]
    public void Load_RemovesUnreachableEntriesFromTheStoreImmediately()
    {
        string reachablePath = Path.Combine(directory, "reachable.json");
        string unreachablePath = Path.Combine(directory, "deleted.json");
        File.WriteAllText(reachablePath, "{}");
        File.WriteAllText(unreachablePath, "{}");
        MeasurementHistoryEntry reachable = CreateEntry(reachablePath);
        var first = new MeasurementHistoryPersistence(storePath);
        first.Save(new[] { reachable, CreateEntry(unreachablePath) });

        // The file disappears; the next launch removes its row at load, with
        // no Save in between.
        File.Delete(unreachablePath);
        var second = new MeasurementHistoryPersistence(storePath);
        Assert.Equal(reachable.Id, Assert.Single(second.Load()).Id);
        Assert.NotNull(second.LoadWarning);

        // Even with the file back, the row is gone for good and the following
        // launch is quiet.
        File.WriteAllText(unreachablePath, "{}");
        var third = new MeasurementHistoryPersistence(storePath);
        Assert.Equal(reachable.Id, Assert.Single(third.Load()).Id);
        Assert.Null(third.LoadWarning);
    }

    [Fact]
    public void Load_ClearsRetentionFromAnEarlierLoadOnTheSameInstance()
    {
        string sourcePath = Path.Combine(directory, "transient.json");
        File.WriteAllText(sourcePath, "{}");
        var persistence = new MeasurementHistoryPersistence(storePath);
        persistence.Save(new[] { CreateEntry(sourcePath) });

        File.Delete(sourcePath);
        Assert.Empty(persistence.Load());
        Assert.NotNull(persistence.LoadWarning);

        // A second Load of a store that no longer mentions the file must not
        // carry the previous load's retention into the next Save.
        File.WriteAllText(storePath, "{\"schemaVersion\":1,\"entries\":[]}");
        Assert.Empty(persistence.Load());
        persistence.Save(Array.Empty<MeasurementHistoryEntry>());

        var reopened = new MeasurementHistoryPersistence(storePath);
        Assert.Empty(reopened.Load());
        Assert.Null(reopened.LoadWarning);
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
