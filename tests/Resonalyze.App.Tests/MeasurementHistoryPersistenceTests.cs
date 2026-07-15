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
    public void Load_DropsEntriesWhoseSourceFileIsMissing()
    {
        string sourcePath = Path.Combine(directory, "deleted.json");
        File.WriteAllText(sourcePath, "{}");
        var persistence = new MeasurementHistoryPersistence(storePath);
        persistence.Save(new[] { CreateEntry(sourcePath) });
        File.Delete(sourcePath);

        Assert.Empty(persistence.Load());
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
