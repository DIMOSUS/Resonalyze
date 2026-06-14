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
            PlayChannel = Chanels.Left,
            MaxMagnitudeIndex = 1,
            RealSamples = [0.25, 1.0, -0.5],
            ImaginarySamples = [0, 0.125, 0]
        };

        try
        {
            await original.SaveAsync(path);

            string json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"format\": \"resonalyze-impulse-response\"", json);
            Assert.Contains("\"realSamples\"", json);

            ImpulseResponseFile loaded = await ImpulseResponseFile.LoadAsync(path);
            Complex[] samples = loaded.GetImpulseResponse();

            Assert.Equal(original.SavedAtUtc, loaded.SavedAtUtc);
            Assert.Equal(48_000, loaded.SampleRate);
            Assert.Equal(24, loaded.Bits);
            Assert.Equal(10, loaded.Octaves);
            Assert.Equal(1.5, loaded.SweepDurationSeconds);
            Assert.Equal(Chanels.Left, loaded.PlayChannel);
            Assert.Equal(1, loaded.MaxMagnitudeIndex);
            Assert.Equal(
                [new Complex(0.25, 0), new Complex(1, 0.125), new Complex(-0.5, 0)],
                samples);
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
              "maxMagnitudeIndex": 0,
              "realSamples": [1.0]
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
}
