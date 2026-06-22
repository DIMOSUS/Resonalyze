using System.Numerics;

namespace Resonalyze.App.Tests;

public sealed class ImpulseResponseFileTests
{
    [Fact]
    public async Task SaveAndLoad_PreservesMetadataAndComplexSamples()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-ir-{Guid.NewGuid():N}.json");
        var original = new ImpulseResponseFile
        {
            SavedAtUtc = new DateTimeOffset(2026, 6, 15, 10, 20, 30, TimeSpan.Zero),
            SampleRate = 48_000,
            Bits = 24,
            Octaves = 10,
            SweepDurationSeconds = 1.5,
            PlayChannel = PlaybackChannel.Left,
            MeasurementMode = SweepMeasurementMode.LoopbackTransfer,
            SweepDeconvolutionPeakIndex = 2,
            SweepDeconvolutionRealSamples = [0.125, 0.5, 1.0, -0.25],
            TransferPeakIndex = 1,
            TransferRealSamples = [0.25, 1.0, -0.5],
            TransferImaginarySamples = [0, 0.125, 0]
        };

        try
        {
            await original.SaveAsync(path);

            string json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"format\": \"resonalyze-impulse-response\"", json);
            Assert.Contains("\"version\": 4", json);
            Assert.Contains("\"measurementMode\": \"LoopbackTransfer\"", json);
            Assert.Contains("\"sweepDeconvolutionPeakIndex\": 2", json);
            Assert.Contains("\"transferPeakIndex\": 1", json);
            Assert.Contains("\"sweepDeconvolutionRealSamples\"", json);
            Assert.Contains("\"transferRealSamples\"", json);
            Assert.True(
                json.IndexOf("\"transferPeakIndex\"", StringComparison.Ordinal) <
                json.IndexOf("\"sweepDeconvolutionRealSamples\"", StringComparison.Ordinal));
            Assert.True(
                json.IndexOf("\"transferPeakIndex\"", StringComparison.Ordinal) <
                json.IndexOf("\"transferRealSamples\"", StringComparison.Ordinal));

            ImpulseResponseFile loaded = await ImpulseResponseFile.LoadAsync(path);
            Complex[] sweepSamples = loaded.GetSweepDeconvolutionImpulseResponse();
            Complex[]? transferSamples = loaded.GetTransferImpulseResponse();
            Assert.NotNull(transferSamples);

            Assert.Equal(original.SavedAtUtc, loaded.SavedAtUtc);
            Assert.Equal(48_000, loaded.SampleRate);
            Assert.Equal(24, loaded.Bits);
            Assert.Equal(10, loaded.Octaves);
            Assert.Equal(1.5, loaded.SweepDurationSeconds);
            Assert.Equal(PlaybackChannel.Left, loaded.PlayChannel);
            Assert.Equal(SweepMeasurementMode.LoopbackTransfer, loaded.MeasurementMode);
            Assert.Equal(2, loaded.SweepDeconvolutionPeakIndex);
            Assert.Equal(
                [
                    new Complex(0.125, 0),
                    new Complex(0.5, 0),
                    new Complex(1.0, 0),
                    new Complex(-0.25, 0)
                ],
                sweepSamples);
            Assert.Equal(1, loaded.TransferPeakIndex);
            Assert.Equal(
                [new Complex(0.25, 0), new Complex(1, 0.125), new Complex(-0.5, 0)],
                transferSamples);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Capture_StoresComputedSweepDuration()
    {
        using var measurement = new ExpSweepMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Right,
            sweepDeconvolutionImpulseResponse:
            [
                Complex.Zero,
                Complex.One,
                Complex.Zero
            ],
            sweepDeconvolutionPeakIndex: 1);

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);

        Assert.NotEqual(measurement.Sweep!.RequestedDuration, file.SweepDurationSeconds);
        Assert.Equal(measurement.Sweep.ComputedDuration, file.SweepDurationSeconds);
    }

    [Fact]
    public async Task Load_RejectsUnsupportedVersion()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-ir-{Guid.NewGuid():N}.json");
        const string json = """
            {
              "format": "resonalyze-impulse-response",
              "version": 999,
              "sampleRate": 48000,
              "bits": 24,
              "octaves": 10,
              "sweepDurationSeconds": 1.0,
              "playChannel": "Mono",
              "measurementMode": "SweepDeconvolution",
              "sweepDeconvolutionPeakIndex": 0,
              "sweepDeconvolutionRealSamples": [1.0]
            }
            """;

        try
        {
            await File.WriteAllTextAsync(path, json);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => ImpulseResponseFile.LoadAsync(path));
            Assert.Contains("version 999", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_RejectsLoopbackTransferWithoutSweepDeconvolutionSamples()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-ir-{Guid.NewGuid():N}.json");
        const string json = """
            {
              "format": "resonalyze-impulse-response",
              "version": 4,
              "sampleRate": 48000,
              "bits": 24,
              "octaves": 10,
              "sweepDurationSeconds": 1.0,
              "playChannel": "Mono",
              "measurementMode": "LoopbackTransfer",
              "sweepDeconvolutionPeakIndex": 0,
              "sweepDeconvolutionRealSamples": [1.0]
            }
            """;

        try
        {
            await File.WriteAllTextAsync(path, json);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => ImpulseResponseFile.LoadAsync(path));
            Assert.Contains("transfer impulse response samples", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
