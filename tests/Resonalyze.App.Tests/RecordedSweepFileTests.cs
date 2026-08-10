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

    [Fact]
    public void MonoFileIsUsedAsIs()
    {
        float[] mono = Tone(2_048, 0.4);

        RecordedSweepChannel selected = RecordedSweepFile.SelectLoudestChannel(
            new AudioFileContent([mono], 48_000));

        Assert.Equal(0, selected.ChannelIndex);
        Assert.Equal(1, selected.ChannelCount);
        Assert.Equal(48_000, selected.SampleRate);
        Assert.Same(mono, selected.Samples);
    }

    [Fact]
    public void LoudestChannelWins()
    {
        float[] quiet = Tone(2_048, 0.02, seed: 2);
        float[] loud = Tone(2_048, 0.4, seed: 3);

        RecordedSweepChannel selected = RecordedSweepFile.SelectLoudestChannel(
            new AudioFileContent([quiet, loud], 48_000));

        Assert.Equal(1, selected.ChannelIndex);
        Assert.Equal(2, selected.ChannelCount);
        Assert.Same(loud, selected.Samples);
        Assert.InRange(selected.PeakDbFs, -9.0, -7.0);
    }

    // Peak alone would pick the dead channel here: a single sample of full-scale
    // click beats a sweep that never leaves -20 dBFS. RMS is what carries the
    // measurement, so RMS is what decides.
    [Fact]
    public void ASingleClickDoesNotWinOverASweep()
    {
        float[] measurement = Tone(2_048, 0.1, seed: 4);
        var clickOnly = new float[2_048];
        clickOnly[17] = 1.0f;

        RecordedSweepChannel selected = RecordedSweepFile.SelectLoudestChannel(
            new AudioFileContent([clickOnly, measurement], 48_000));

        Assert.Equal(1, selected.ChannelIndex);
    }

    [Fact]
    public void EmptyContentIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RecordedSweepFile.SelectLoudestChannel(new AudioFileContent([], 48_000)));
    }

    [Fact]
    public void LoadReadsTheLoudestChannelOfAStereoFile()
    {
        string path = Path.Combine(directory, "recording.wav");
        float[] quiet = Tone(4_096, 0.01, seed: 5);
        float[] loud = Tone(4_096, 0.5, seed: 6);
        AudioFileCodec.WriteWav(path, new AudioFileContent([quiet, loud], 44_100));

        RecordedSweepChannel selected = RecordedSweepFile.Load(path);

        Assert.Equal(1, selected.ChannelIndex);
        Assert.Equal(2, selected.ChannelCount);
        Assert.Equal(44_100, selected.SampleRate);
        Assert.Equal(4_096, selected.Samples.Length);
        // 24-bit round trip: the samples come back, not merely the channel choice.
        for (int i = 0; i < loud.Length; i++)
        {
            Assert.Equal(loud[i], selected.Samples[i], tolerance: 1e-6f);
        }
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
