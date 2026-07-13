namespace Resonalyze.Audio.Tests;

public sealed class PcmStreamSetTests
{
    [Fact]
    public void Pack_Encodes16BitMonoLittleEndian()
    {
        float[] samples = [0.0f, 1.0f, -1.0f];

        byte[] data = PcmStreamSet.Pack(samples, 16, PlaybackChannel.Mono);

        Assert.Equal(6, data.Length);
        Assert.Equal((short)0, BitConverter.ToInt16(data, 0));
        Assert.Equal((short)32767, BitConverter.ToInt16(data, 2));
        Assert.Equal((short)-32767, BitConverter.ToInt16(data, 4));
    }

    [Fact]
    public void Pack_LeftRoutingLeavesRightChannelSilent()
    {
        float[] samples = [0.5f];

        byte[] data = PcmStreamSet.Pack(samples, 16, PlaybackChannel.Left);

        Assert.Equal(4, data.Length);
        Assert.Equal((short)16383, BitConverter.ToInt16(data, 0));
        Assert.Equal((short)0, BitConverter.ToInt16(data, 2));
    }

    [Fact]
    public void Pack_StereoDuplicatesTheSignal()
    {
        float[] samples = [0.5f];

        byte[] data = PcmStreamSet.Pack(samples, 16, PlaybackChannel.Stereo);

        Assert.Equal(BitConverter.ToInt16(data, 0), BitConverter.ToInt16(data, 2));
    }

    [Fact]
    public void Pack_Encodes24BitSamples()
    {
        float[] samples = [1.0f, -1.0f];

        byte[] data = PcmStreamSet.Pack(samples, 24, PlaybackChannel.Mono);

        Assert.Equal(6, data.Length);
        // 0x7FFFFF little-endian, then its negation in two's complement.
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0x7F }, data[..3]);
        int negative = data[3] | (data[4] << 8) | (data[5] << 16);
        if ((negative & 0x800000) != 0)
        {
            negative |= unchecked((int)0xFF000000);
        }

        Assert.Equal(-8388607, negative);
    }

    [Fact]
    public void GetStream_IsLazyAndReturnsTheSameInstance()
    {
        using var set = new PcmStreamSet([0.25f, -0.25f], 48_000, 16);

        var first = set.GetStream(PlaybackChannel.Stereo);
        var second = set.GetStream(PlaybackChannel.Stereo);

        Assert.Same(first, second);
        Assert.Equal(2, first.WaveFormat.Channels);
        Assert.Equal(48_000, first.WaveFormat.SampleRate);
        Assert.Equal(16, first.WaveFormat.BitsPerSample);
        Assert.Equal(8, first.Length);
    }
}
