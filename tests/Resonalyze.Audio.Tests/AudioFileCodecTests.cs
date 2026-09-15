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
    public void WriteWavFloat32_ReadBack_IsExact_AndKeepsSamplesPastFullScale()
    {
        // Float writer data is not a recording (FIR taps past ±1): no scaling or clipping.
        float[] left = [0f, 1.5f, -2.25f, 0.125f, 1e-7f, -1f];
        float[] right = [3f, 0f, 0f, 0f, 0f, 0f];
        string path = PathFor("float.wav");

        AudioFileCodec.WriteWavFloat32(path, new AudioFileContent([left, right], 96_000));
        AudioFileContent back = AudioFileCodec.Read(path, TimeSpan.FromSeconds(1));

        Assert.Equal(96_000, back.SampleRate);
        Assert.Equal(2, back.ChannelCount);
        Assert.Equal(left, back.Channels[0]);
        Assert.Equal(right, back.Channels[1]);
    }

    [Fact]
    public void WriteWav_ClipsOutOfRangeSamplesInsteadOfWrapping()
    {
        float[] channel = [2.0f, -3.0f, 0.5f];
        string path = PathFor("clipped.wav");

        AudioFileCodec.WriteWav(path, new AudioFileContent([channel], 48_000));
        AudioFileContent read = AudioFileCodec.Read(path, TimeSpan.FromMinutes(1));

        Assert.True(read.Channels[0][0] > 0.99f);
        Assert.True(read.Channels[0][1] < -0.99f);
        Assert.Equal(0.5f, read.Channels[0][2], 3);
    }

    // Unique per channel and position, so rotation, swap or slip all break equality; step 0.004 is far above 24-bit tolerance.
    private static float MultichannelSample(int channel, int frame) =>
        (frame % 997 - 498) / 1_000f + channel * 0.004f;

    [Fact]
    public void Read_ChannelLimitKeepsExactlyTheLeadingChannels()
    {
        // More frames than one 32768 decoder block: the channel cursor must survive across Read calls.
        const int Rate = 48_000;
        const int Frames = 70_000;
        const int ChannelCount = 4;
        var channels = new float[ChannelCount][];
        for (int c = 0; c < ChannelCount; c++)
        {
            channels[c] = new float[Frames];
            for (int i = 0; i < Frames; i++)
            {
                channels[c][i] = MultichannelSample(c, i);
            }
        }
        string path = PathFor("multichannel.wav");
        AudioFileCodec.WriteWav(path, new AudioFileContent(channels, Rate));

        AudioFileContent limited = AudioFileCodec.Read(
            path, TimeSpan.FromMinutes(1), channelLimit: 2);

        Assert.Equal(2, limited.ChannelCount);
        Assert.Equal(Frames, limited.FrameCount);
        double quantum = 1.0 / (1 << 23);
        for (int c = 0; c < 2; c++)
        {
            for (int i = 0; i < Frames; i++)
            {
                Assert.True(
                    Math.Abs(limited.Channels[c][i] - MultichannelSample(c, i))
                        <= 2 * quantum,
                    $"Channel {c} drifted at frame {i}");
            }
        }

        AudioFileContent full = AudioFileCodec.Read(path, TimeSpan.FromMinutes(1));
        Assert.Equal(ChannelCount, full.ChannelCount);
        for (int i = 0; i < Frames; i += 1_000)
        {
            Assert.True(
                Math.Abs(full.Channels[3][i] - MultichannelSample(3, i))
                    <= 2 * quantum);
        }
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

    // 1 s mono 8 kHz float is 32 kB but the decode peak is twice that: 50 kB fits the payload, not the peak.
    [Theory]
    [InlineData(20_000)]
    [InlineData(50_000)]
    public void Read_RefusesMaterialPastTheByteBudget(long budget)
    {
        // The byte bound holds when the header lies.
        const int Rate = 8_000;
        string path = PathFor($"oversized-{budget}.wav");
        AudioFileCodec.WriteWav(
            path, new AudioFileContent([new float[Rate]], Rate));

        Assert.Throws<InvalidOperationException>(() =>
            AudioFileCodec.Read(
                path,
                TimeSpan.FromMinutes(1),
                channelLimit: int.MaxValue,
                maximumStoredBytes: budget));
    }

    [Fact]
    public void WriteWav_RefusesEmptyContent()
    {
        Assert.Throws<ArgumentException>(() =>
            AudioFileCodec.WriteWav(
                PathFor("empty.wav"),
                new AudioFileContent(Array.Empty<float[]>(), 48_000)));
    }

    // WAVE_FORMAT_EXTENSIBLE (0xFFFE) was handed to ACM as compressed and failed.
    [Theory]
    [InlineData(24)]
    [InlineData(16)]
    [InlineData(32)]
    public void Read_DecodesExtensiblePcm(int bitsPerSample)
    {
        const int Rate = 44_100;
        int[] values = [0, 1 << (bitsPerSample - 4), -(1 << (bitsPerSample - 4)), 12_345];
        string path = PathFor($"extensible-{bitsPerSample}.wav");
        WriteExtensiblePcm(path, Rate, channels: 2, bitsPerSample, values);

        AudioFileContent read = AudioFileCodec.Read(path, TimeSpan.FromMinutes(1));

        Assert.Equal(2, read.ChannelCount);
        Assert.Equal(Rate, read.SampleRate);
        Assert.Equal(values.Length / 2, read.FrameCount);
        double full = 1L << (bitsPerSample - 1);
        Assert.Equal(values[0] / full, read.Channels[0][0], tolerance: 1e-6);
        Assert.Equal(values[1] / full, read.Channels[1][0], tolerance: 1e-6);
        Assert.Equal(values[2] / full, read.Channels[0][1], tolerance: 1e-6);
        Assert.Equal(values[3] / full, read.Channels[1][1], tolerance: 1e-6);
    }

    [Fact]
    public void Probe_ReadsExtensiblePcmFormat()
    {
        string path = PathFor("extensible-probe.wav");
        WriteExtensiblePcm(path, 96_000, channels: 2, bitsPerSample: 24, [0, 0, 0, 0]);

        AudioFileInfo info = AudioFileCodec.Probe(path);

        Assert.Equal(2, info.ChannelCount);
        Assert.Equal(96_000, info.SampleRate);
    }

    private static void WriteExtensiblePcm(
        string path,
        int sampleRate,
        int channels,
        int bitsPerSample,
        int[] values)
    {
        int bytesPerSample = bitsPerSample / 8;
        int blockAlign = channels * bytesPerSample;
        var data = new byte[values.Length * bytesPerSample];
        for (int i = 0; i < values.Length; i++)
        {
            for (int b = 0; b < bytesPerSample; b++)
            {
                data[i * bytesPerSample + b] = (byte)(values[i] >> (8 * b));
            }
        }

        byte[] list = "LIST"u8.ToArray();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(4 + 48 + 12 + 8 + data.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(40);
        writer.Write((ushort)0xFFFE);
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * blockAlign);
        writer.Write((ushort)blockAlign);
        writer.Write((ushort)bitsPerSample);
        writer.Write((ushort)22);
        writer.Write((ushort)bitsPerSample);
        writer.Write(channels == 2 ? 3 : 4);
        writer.Write(new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray());
        writer.Write(list);
        writer.Write(4);
        writer.Write("INFO"u8);
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
    }

    [Fact]
    public void WriteWav_RefusesMismatchedChannelLengths()
    {
        // The interleaver indexes by the first channel's frame count.
        Assert.Throws<ArgumentException>(() =>
            AudioFileCodec.WriteWav(
                PathFor("mismatched.wav"),
                new AudioFileContent([new float[300], new float[100]], 48_000)));
    }
}
