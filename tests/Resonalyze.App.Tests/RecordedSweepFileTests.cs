using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

public sealed class RecordedSweepFileTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "resonalyze-recorded-sweep-" + Guid.NewGuid().ToString("N"));

    public RecordedSweepFileTests() => Directory.CreateDirectory(directory);

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

    private static float[] Tone(int length, double amplitude, int seed = 1)
    {
        var samples = new float[length];
        var random = new Random(seed);
        for (int i = 0; i < length; i++)
        {
            samples[i] = (float)(Math.Sin(i * 0.05) * amplitude +
                (random.NextDouble() - 0.5) * amplitude * 0.01);
        }

        return samples;
    }

    // Every channel is read: which one carries the measurement is decided later,
    // by matching them against the sweep, and that decision needs them all.
    [Fact]
    public void LoadKeepsEveryChannel()
    {
        string path = Path.Combine(directory, "recording.wav");
        float[] quiet = Tone(4_096, 0.01, seed: 5);
        float[] loud = Tone(4_096, 0.5, seed: 6);
        AudioFileCodec.WriteWav(path, new AudioFileContent([quiet, loud], 44_100));

        AudioFileContent content = RecordedSweepFile.Load(path);

        Assert.Equal(2, content.ChannelCount);
        Assert.Equal(44_100, content.SampleRate);
        Assert.Equal(4_096, content.FrameCount);
        // 24-bit round trip: the samples come back, not merely the shape.
        for (int i = 0; i < loud.Length; i++)
        {
            Assert.Equal(quiet[i], content.Channels[0][i], tolerance: 1e-6f);
            Assert.Equal(loud[i], content.Channels[1][i], tolerance: 1e-6f);
        }
    }

    [Fact]
    public void AnEmptyFileIsRefused()
    {
        string path = Path.Combine(directory, "empty.wav");
        AudioFileCodec.WriteWav(path, new AudioFileContent([new float[1]], 44_100));
        // A one-sample file decodes, but a recording of nothing is not a take.
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..44]);

        Assert.ThrowsAny<Exception>(() => RecordedSweepFile.Load(path));
    }

    [Theory]
    [InlineData(0, 2, "left")]
    [InlineData(1, 2, "right")]
    [InlineData(2, 4, "channel 3")]
    public void ChannelNamesReadAsAListenerWouldSayThem(
        int channelIndex,
        int channelCount,
        string expected)
    {
        Assert.Equal(expected, RecordedSweepFile.DescribeChannel(channelIndex, channelCount));
    }
}
