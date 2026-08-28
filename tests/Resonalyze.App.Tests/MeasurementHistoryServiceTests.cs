using System.Numerics;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

public sealed class MeasurementHistoryServiceTests : IDisposable
{
    private readonly string directory;

    public MeasurementHistoryServiceTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-history-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
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
    public async Task FileBackedEntries_KeepAtMostOneFullSnapshotInMemory()
    {
        MeasurementHistoryService service = CreateService();
        string pathA = await CreateImpulseResponseFileAsync("a.json");
        string pathB = await CreateImpulseResponseFileAsync("b.json");

        Guid idA = service.AddOrUpdateLoadedFile(
            pathA,
            await ImpulseResponseFile.LoadAsync(pathA),
            new MeasurementSessionSnapshot());
        Guid idB = service.AddOrUpdateLoadedFile(
            pathB,
            await ImpulseResponseFile.LoadAsync(pathB),
            new MeasurementSessionSnapshot());

        // Each snapshot holds the complete IR; only the most recent file-backed
        // entry may keep one — the file itself remains the source of truth.
        Assert.Null(service.FindById(idA)!.Snapshot);
        Assert.NotNull(service.FindById(idB)!.Snapshot);

        MeasurementHistorySnapshot? reloaded = await service.GetSnapshotAsync(idA);

        Assert.NotNull(reloaded);
        Assert.NotNull(service.FindById(idA)!.Snapshot);
        Assert.Null(service.FindById(idB)!.Snapshot);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReloadsEvictedSnapshotFromDisk()
    {
        MeasurementHistoryService service = CreateService();
        string pathA = await CreateImpulseResponseFileAsync("a.json");
        string pathB = await CreateImpulseResponseFileAsync("b.json");
        Guid idA = service.AddOrUpdateLoadedFile(
            pathA,
            await ImpulseResponseFile.LoadAsync(pathA),
            new MeasurementSessionSnapshot());
        service.AddOrUpdateLoadedFile(
            pathB,
            await ImpulseResponseFile.LoadAsync(pathB),
            new MeasurementSessionSnapshot());

        MeasurementHistorySnapshot? snapshot = await service.GetSnapshotAsync(idA);

        Assert.NotNull(snapshot);
        Assert.Equal(48_000, snapshot!.SampleRate);
        Assert.NotEmpty(snapshot.SweepDeconvolutionImpulseResponse);
    }

    // Both halves of K used to be split up by the history: the loopback level rode
    // along in the meter snapshot while the anchor was dropped, so a restored entry
    // — or a Compare picked from history — could not be shown in dB SPL, and saving
    // the entry back to disk wrote a file with no calibration at all.
    [Fact]
    public void Snapshot_CarriesTheSplAnchorFromTheFileAndBackToIt()
    {
        ImpulseResponseFile file = ImpulseResponseFileAtomicSaveTests.CreateFile(
            sampleValue: 1.0);
        file.SplCalibration = new SplCalibration
        {
            ReferenceLevelDbSpl = 94,
            MeasuredLevelDbFs = -20,
            Backend = Resonalyze.Audio.AudioBackend.Wave,
            SampleRate = 48_000,
            Bits = 24
        };
        file.LoopbackLevels = new ImpulseResponseFile.LevelSnapshotFileEntry
        {
            PeakDbFs = -6,
            RmsDbFs = -9
        };

        MeasurementHistorySnapshot snapshot = MeasurementHistoryService.CreateSnapshot(file);

        Assert.Same(file.SplCalibration, snapshot.SplCalibration);
        Assert.Equal(108.0, snapshot.SplOffsetDb!.Value, tolerance: 1e-9);
        Assert.Same(file.SplCalibration, snapshot.ToImpulseResponseFile().SplCalibration);
    }

    [Fact]
    public async Task TheListIsCappedAtItsMaximumDepth_OldestFirst()
    {
        MeasurementHistoryService service = CreateService();
        var added = new List<Guid>();
        for (int index = 0; index < MeasurementHistoryService.MaxHistoryEntries + 3; index++)
        {
            string path = await CreateImpulseResponseFileAsync($"m{index}.json");
            added.Add(service.AddOrUpdateLoadedFile(
                path,
                await ImpulseResponseFile.LoadAsync(path),
                new MeasurementSessionSnapshot()));
        }

        Assert.Equal(MeasurementHistoryService.MaxHistoryEntries, service.Entries.Count);
        // The three that fell off are the three opened first, and the newest is
        // still at the top: the cap cuts the tail, it does not reorder the list.
        Assert.Null(service.FindById(added[0]));
        Assert.Null(service.FindById(added[1]));
        Assert.Null(service.FindById(added[2]));
        Assert.NotNull(service.FindById(added[3]));
        Assert.Equal(added[^1], service.Entries[0].Id);
    }

    [Fact]
    public async Task OverDepth_AnUnsavedEntryOutlivesAnOlderSavedOne()
    {
        MeasurementHistoryService service = CreateService();
        string firstPath = await CreateImpulseResponseFileAsync("first.json");
        Guid oldestSaved = service.AddOrUpdateLoadedFile(
            firstPath,
            await ImpulseResponseFile.LoadAsync(firstPath),
            new MeasurementSessionSnapshot());
        using ExpSweepMeasurement measurement = CreateMeasurement();
        Guid unsaved = service.AddMeasurement(measurement, new MeasurementSessionSnapshot());
        for (int index = 0; index < MeasurementHistoryService.MaxHistoryEntries - 1; index++)
        {
            string path = await CreateImpulseResponseFileAsync($"filler{index}.json");
            service.AddOrUpdateLoadedFile(
                path,
                await ImpulseResponseFile.LoadAsync(path),
                new MeasurementSessionSnapshot());
        }

        Assert.Equal(MeasurementHistoryService.MaxHistoryEntries, service.Entries.Count);
        // The unsaved row is older than every filler and would have gone first on a
        // plain tail cut. It is the measurement itself — the saved row it displaced
        // is a pointer to a file still on disk.
        Assert.NotNull(service.FindById(unsaved));
        Assert.Null(service.FindById(oldestSaved));
    }

    [Fact]
    public void AStoreOverDepthIsCutWhenItLoads_AndTheCutReachesDisk()
    {
        string storePath = Path.Combine(directory, "measurement-history.json");
        new MeasurementHistoryPersistence(storePath).Save(OverDepthEntries());

        var service = new MeasurementHistoryService(new MeasurementHistoryPersistence(storePath));

        Assert.Equal(MeasurementHistoryService.MaxHistoryEntries, service.Entries.Count);
        // Rewritten on load, not at the next mutation: a session that only reads
        // the history never saves it, and the cut would not survive the launch.
        Assert.Equal(
            MeasurementHistoryService.MaxHistoryEntries,
            new MeasurementHistoryPersistence(storePath).Load().Count);
    }

    [Fact]
    public void AStoreThatCannotBeRewrittenStillOpens()
    {
        string storePath = Path.Combine(directory, "measurement-history.json");
        new MeasurementHistoryPersistence(storePath).Save(OverDepthEntries());
        // The trim's write fails on this exactly as it would against a store held
        // by another instance or marked read-only by the user's backup tool.
        File.SetAttributes(storePath, FileAttributes.ReadOnly);
        try
        {
            // The list is cut in memory whatever the disk does; the launch is not
            // the place to report that the cut could not be persisted, and the next
            // launch tries again.
            var service = new MeasurementHistoryService(
                new MeasurementHistoryPersistence(storePath));

            Assert.Equal(MeasurementHistoryService.MaxHistoryEntries, service.Entries.Count);
            Assert.Equal(
                MeasurementHistoryService.MaxHistoryEntries + 5,
                new MeasurementHistoryPersistence(storePath).Load().Count);
        }
        finally
        {
            File.SetAttributes(storePath, FileAttributes.Normal);
        }
    }

    private List<MeasurementHistoryEntry> OverDepthEntries()
    {
        var stored = new List<MeasurementHistoryEntry>();
        for (int index = 0; index < MeasurementHistoryService.MaxHistoryEntries + 5; index++)
        {
            string path = Path.Combine(directory, $"stored{index}.json");
            File.WriteAllText(path, "{}");
            stored.Add(MeasurementHistoryPersistenceTests.CreateEntry(path));
        }

        return stored;
    }

    private static ExpSweepMeasurement CreateMeasurement()
    {
        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            20, 20_000, 48_000, 24, 1.0, PlaybackChannel.Mono,
            [Complex.Zero, Complex.One, Complex.Zero],
            sweepDeconvolutionPeakIndex: 1);
        return measurement;
    }

    private MeasurementHistoryService CreateService() =>
        new(new MeasurementHistoryPersistence(
            Path.Combine(directory, "measurement-history.json")));

    private async Task<string> CreateImpulseResponseFileAsync(string fileName)
    {
        string path = Path.Combine(directory, fileName);
        await ImpulseResponseFileAtomicSaveTests.CreateFile(sampleValue: 1.0)
            .SaveAsync(path);
        return path;
    }
}
