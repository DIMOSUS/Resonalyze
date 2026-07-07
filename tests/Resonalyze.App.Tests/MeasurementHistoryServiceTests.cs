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
