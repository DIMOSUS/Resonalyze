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

    // Unique per channel AND per position: a channel rotation, a swap, or a
    // one-frame slip each break the equality somewhere. The inter-channel step
    // (0.004) is far above the 24-bit round-trip tolerance.
    private static float MultichannelSample(int channel, int frame) =>
        (frame % 997 - 498) / 1_000f + channel * 0.004f;

    [Fact]
    public void Read_ChannelLimitKeepsExactlyTheLeadingChannels()
    {
        // Four channels over MORE frames than one decoder block (32768), so
        // the running channel cursor crosses several Read calls — the place a
        // per-block restart would rotate the channel assignment. A WAV reader
        // always returns whole frames, so the mid-frame boundary itself cannot
        // be staged here; the cursor continuity across calls is what this
        // exercises.
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

        // Without a limit the same file yields every channel, and the LAST one
        // carries its own signal — the limited read kept the right two, not
        // just any two.
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

    // One second of mono 8 kHz float is a 32 kB payload, but the decode's
    // PEAK is twice that: while the final array fills, every chunk still
    // exists. 20 kB fails on the payload alone; 50 kB fits the payload and
    // must still be refused for the assembly peak (64 kB).
    [Theory]
    [InlineData(20_000)]
    [InlineData(50_000)]
    public void Read_RefusesMaterialPastTheByteBudget(long budget)
    {
        // The duration bound trusts the header; the byte bound holds when the
        // header lies or the file was swapped after probing.
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

    // WAVE_FORMAT_EXTENSIBLE (0xFFFE) is what most recorders and DAWs write for
    // 24-bit and multichannel files. Read used to hand it to ACM as if it were
    // compressed and fail with "NoDriver calling acmFormatSuggest" — an ordinary
    // 24-bit recording, refused.
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

    // A 40-byte extensible fmt chunk with the PCM subformat GUID, plus a LIST
    // chunk between fmt and data — both of which real recorders write.
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
        // The interleaver indexes every channel by the first one's frame count;
        // a shorter channel used to crash it mid-write with an index error.
        Assert.Throws<ArgumentException>(() =>
            AudioFileCodec.WriteWav(
                PathFor("mismatched.wav"),
                new AudioFileContent([new float[300], new float[100]], 48_000)));
    }
}
