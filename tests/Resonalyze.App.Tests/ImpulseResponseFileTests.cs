using System.Numerics;
using NAudio.Wave;

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
            AverageRunCount = 4,
            AcceptedAverageRunCount = 3,
            AudioSession = new ImpulseResponseFile.AudioSessionFileEntry
            {
                Backend = "WasapiShared",
                CaptureEndpointId = "capture-id",
                RenderEndpointId = "render-id",
                ShareMode = "Shared",
                CaptureFormat = "32 bit float: 48kHz 2 channels",
                RenderFormat = "32 bit float: 48kHz 2 channels",
                CaptureSampleRate = 48_000,
                RenderSampleRate = 48_000,
                AnalysisSampleRate = 48_000,
                FormatConversionOccurred = false,
                RequestedBufferMilliseconds = 100,
                ActualBufferFrames = 4_800,
                CapturePackets = 123,
                Discontinuities = 1
            },
            MicrophoneLevels = new ImpulseResponseFile.LevelSnapshotFileEntry
            {
                PeakDbFs = -6.5,
                RmsDbFs = -18.25,
                Clipped = true
            },
            LoopbackLevels = new ImpulseResponseFile.LevelSnapshotFileEntry
            {
                PeakDbFs = 0.0,
                RmsDbFs = -5.4,
                FullScaleReference = true
            },
            SweepDeconvolutionRealSamples = [0.125, 0.5, 1.0, -0.25],
            TransferPeakIndex = 1,
            // Coherence must be N/2 + 1 bins for an N-sample transfer IR.
            TransferRealSamples = [0.25, 1.0, -0.5, 0.125],
            TransferImaginarySamples = [0, 0.125, 0, 0],
            TransferCoherence = [0.91, 0.82, 0.73]
        };

        try
        {
            await original.SaveAsync(path);

            string json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"format\": \"resonalyze-impulse-response\"", json);
            Assert.Contains($"\"version\": {ImpulseResponseFile.CurrentVersion}", json);
            Assert.Contains("\"measurementMode\": \"LoopbackTransfer\"", json);
            Assert.Contains("\"sweepDeconvolutionPeakIndex\": 2", json);
            Assert.Contains("\"averageRunCount\": 4", json);
            Assert.Contains("\"acceptedAverageRunCount\": 3", json);
            Assert.Contains("\"audioSession\"", json);
            Assert.Contains("\"transferPeakIndex\": 1", json);
            Assert.Contains("\"transferCoherence\"", json);
            Assert.Contains("\"microphoneLevels\"", json);
            Assert.Contains("\"loopbackLevels\"", json);
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
            Assert.Equal(4, loaded.AverageRunCount);
            Assert.Equal(3, loaded.AcceptedAverageRunCount);
            Assert.NotNull(loaded.AudioSession);
            Assert.Equal("WasapiShared", loaded.AudioSession.Backend);
            Assert.Equal("capture-id", loaded.AudioSession.CaptureEndpointId);
            Assert.Equal(123, loaded.AudioSession.CapturePackets);
            Assert.Equal(1, loaded.AudioSession.Discontinuities);
            Assert.NotNull(loaded.MicrophoneLevels);
            Assert.Equal(-6.5, loaded.MicrophoneLevels.PeakDbFs);
            Assert.Equal(-18.25, loaded.MicrophoneLevels.RmsDbFs);
            Assert.True(loaded.MicrophoneLevels.Clipped);
            Assert.NotNull(loaded.LoopbackLevels);
            Assert.Equal(0.0, loaded.LoopbackLevels.PeakDbFs);
            Assert.Equal(-5.4, loaded.LoopbackLevels.RmsDbFs);
            Assert.True(loaded.LoopbackLevels.FullScaleReference);
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
                [
                    new Complex(0.25, 0),
                    new Complex(1, 0.125),
                    new Complex(-0.5, 0),
                    new Complex(0.125, 0)
                ],
                transferSamples);
            Assert.NotNull(loaded.TransferCoherence);
            Assert.Equal([0.91, 0.82, 0.73], loaded.TransferCoherence);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AudioSessionEntry_CapturesWasapiDiagnostics()
    {
        var diagnostics = new AudioSessionDiagnostics(
            "WasapiExclusive",
            "capture-id",
            "render-id",
            WaveFormat.CreateCustomFormat(
                WaveFormatEncoding.Pcm, 96_000, 2, 576_000, 6, 24),
            WaveFormat.CreateCustomFormat(
                WaveFormatEncoding.Pcm, 96_000, 2, 576_000, 6, 24),
            40,
            3_840,
            50,
            12,
            2,
            3,
            4,
            5,
            6);

        ImpulseResponseFile.AudioSessionFileEntry? entry =
            ImpulseResponseFile.CreateAudioSessionFileEntry(diagnostics, 96_000, 24);

        Assert.NotNull(entry);
        Assert.Equal("Exclusive", entry.ShareMode);
        Assert.Equal(96_000, entry.AnalysisSampleRate);
        Assert.False(entry.FormatConversionOccurred);
        Assert.Equal(50, entry.CapturePackets);
        Assert.Equal(3_840, entry.ActualBufferFrames);
        Assert.Equal(12, entry.RenderCallbacks);
        Assert.Equal(6, entry.RenderUnderruns);
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
    public void Capture_StoresCurrentLevelSnapshot()
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
        measurement.RestoreLevelSnapshot(
            new InputLevelMeterSnapshot(
                new InputLevelMeterEntry(true, -12.3, -20.4, true, false),
                new InputLevelMeterEntry(true, 0.0, -5.4, false, true)));

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);

        Assert.NotNull(file.MicrophoneLevels);
        Assert.Equal(-12.3, file.MicrophoneLevels.PeakDbFs);
        Assert.Equal(-20.4, file.MicrophoneLevels.RmsDbFs);
        Assert.True(file.MicrophoneLevels.Clipped);
        Assert.NotNull(file.LoopbackLevels);
        Assert.Equal(0.0, file.LoopbackLevels.PeakDbFs);
        Assert.Equal(-5.4, file.LoopbackLevels.RmsDbFs);
        Assert.True(file.LoopbackLevels.FullScaleReference);
    }

    [Fact]
    public async Task Load_IgnoresJsonPropertyOrder()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-ir-{Guid.NewGuid():N}.json");
        const string json = """
            {
              "sweepDeconvolutionRealSamples": [0.25, 1.0, -0.5],
              "loopbackLevels": {
                "fullScaleReference": true,
                "rmsDbFs": -5.4,
                "peakDbFs": 0.0,
                "clipped": false
              },
              "sweepDeconvolutionPeakIndex": 1,
              "measurementMode": "SweepDeconvolution",
              "playChannel": "Right",
              "sweepDurationSeconds": 1.0,
              "octaves": 12,
              "bits": 24,
              "sampleRate": 48000,
              "savedAtUtc": "2026-06-15T10:20:30+00:00",
              "version": 4,
              "format": "resonalyze-impulse-response"
            }
            """;

        try
        {
            await File.WriteAllTextAsync(path, json);

            ImpulseResponseFile loaded = await ImpulseResponseFile.LoadAsync(path);

            Assert.Equal(48_000, loaded.SampleRate);
            Assert.Equal(PlaybackChannel.Right, loaded.PlayChannel);
            Assert.Equal(1, loaded.SweepDeconvolutionPeakIndex);
            Assert.NotNull(loaded.LoopbackLevels);
            Assert.True(loaded.LoopbackLevels.FullScaleReference);
        }
        finally
        {
            File.Delete(path);
        }
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
