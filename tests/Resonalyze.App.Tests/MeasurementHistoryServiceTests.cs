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
