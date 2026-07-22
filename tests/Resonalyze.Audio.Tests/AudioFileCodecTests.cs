namespace Resonalyze.Audio.Tests;

public sealed class AudioFileCodecTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "resonalyze-codec-tests-" + Guid.NewGuid().ToString("N"));

    public AudioFileCodecTests()
    {
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
            // Temp cleanup is best-effort.
        }
    }

    private string PathFor(string name) => Path.Combine(directory, name);

    [Fact]
    public void WriteWav_ReadBack_RoundTripsWithin24BitPrecision()
    {
        const int Rate = 48_000;
        var left = new float[Rate / 10];
        var right = new float[left.Length];
        for (int i = 0; i < left.Length; i++)
        {
            left[i] = (float)(0.8 * Math.Sin(2.0 * Math.PI * 440.0 * i / Rate));
            right[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * 1_000.0 * i / Rate));
        }
        string path = PathFor("roundtrip.wav");

        AudioFileCodec.WriteWav(path, new AudioFileContent([left, right], Rate));
        AudioFileContent read = AudioFileCodec.Read(
            path, TimeSpan.FromMinutes(1));

        Assert.Equal(2, read.ChannelCount);
        Assert.Equal(Rate, read.SampleRate);
        Assert.Equal(left.Length, read.FrameCount);
        double quantum = 1.0 / (1 << 23);
        for (int i = 0; i < left.Length; i++)
        {
            Assert.True(Math.Abs(read.Channels[0][i] - left[i]) <= 2 * quantum);
            Assert.True(Math.Abs(read.Channels[1][i] - right[i]) <= 2 * quantum);
        }
    }

    [Fact]
    public void WriteWav_ClipsOutOfRangeSamplesInsteadOfWrapping()
    {
        float[] channel = [2.0f, -3.0f, 0.5f];
        string path = PathFor("clipped.wav");

        AudioFileCodec.WriteWav(path, new AudioFileContent([channel], 48_000));
        AudioFileContent read = AudioFileCodec.Read(path, TimeSpan.FromMinutes(1));

        // A wrapped +2.0 would come back near -1; clipping keeps the signs.
        Assert.True(read.Channels[0][0] > 0.99f);
        Assert.True(read.Channels[0][1] < -0.99f);
        Assert.Equal(0.5f, read.Channels[0][2], 3);
    }

    [Fact]
    public void Read_RefusesMaterialLongerThanTheBound()
    {
        const int Rate = 8_000;
        var channel = new float[Rate * 3];
        string path = PathFor("long.wav");
        AudioFileCodec.WriteWav(path, new AudioFileContent([channel], Rate));

        Assert.Throws<InvalidOperationException>(() =>
            AudioFileCodec.Read(path, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void WriteWav_RefusesEmptyContent()
    {
        Assert.Throws<ArgumentException>(() =>
            AudioFileCodec.WriteWav(
                PathFor("empty.wav"),
                new AudioFileContent(Array.Empty<float[]>(), 48_000)));
    }

    [Fact]
    public void WriteWav_RefusesMismatchedChannelLengths()
    {
        // The interleaver indexes every channel by the first one's frame count;
        // a shorter channel used to crash it mid-write with an index error.
        Assert.Throws<ArgumentException>(() =>
            AudioFileCodec.WriteWav(
                PathFor("mismatched.wav"),
                new AudioFileContent([new float[300], new float[100]], 48_000)));
    }
}
