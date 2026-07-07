namespace Resonalyze.App.Tests;

public sealed class ImpulseResponseFileAtomicSaveTests : IDisposable
{
    private readonly string directory;

    public ImpulseResponseFileAtomicSaveTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-ir-atomic-{Guid.NewGuid():N}");
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
    public async Task SaveAsync_OverwritesExistingFileAndLeavesNoTempFile()
    {
        string path = Path.Combine(directory, "ir.json");
        await CreateFile(sampleValue: 1.0).SaveAsync(path);
        await CreateFile(sampleValue: 0.5).SaveAsync(path);

        ImpulseResponseFile loaded = await ImpulseResponseFile.LoadAsync(path);
        Assert.Equal(0.5, loaded.SweepDeconvolutionRealSamples[0]);
        Assert.Equal(new[] { "ir.json" }, ListFileNames());
    }

    [Fact]
    public async Task SaveAsync_KeepsExistingFileIntactWhenWritingFails()
    {
        string path = Path.Combine(directory, "ir.json");
        await CreateFile(sampleValue: 1.0).SaveAsync(path);

        // A cancelled write must not have truncated the previously saved file.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateFile(sampleValue: 0.5).SaveAsync(path, cancellation.Token));

        ImpulseResponseFile survivor = await ImpulseResponseFile.LoadAsync(path);
        Assert.Equal(1.0, survivor.SweepDeconvolutionRealSamples[0]);
        Assert.Equal(new[] { "ir.json" }, ListFileNames());
    }

    private string[] ListFileNames() =>
        Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();

    internal static ImpulseResponseFile CreateFile(double sampleValue) =>
        new()
        {
            SavedAtUtc = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero),
            SampleRate = 48_000,
            Bits = 24,
            Octaves = 10,
            SweepDurationSeconds = 1.0,
            PlayChannel = PlaybackChannel.Left,
            SweepDeconvolutionPeakIndex = 0,
            SweepDeconvolutionRealSamples = [sampleValue, 0.25]
        };
}
