using System.Runtime.InteropServices;
using NAudio.Wave.Asio;

namespace Resonalyze.Audio.Tests;

public sealed class AsioSampleConverterTests
{
    [Theory]
    [InlineData(AsioSampleType.Int16LSB, 2)]
    [InlineData(AsioSampleType.Int24LSB, 3)]
    [InlineData(AsioSampleType.Int32LSB, 4)]
    [InlineData(AsioSampleType.Float32LSB, 4)]
    public void BytesPerSample_ReturnsPhysicalSampleWidth(AsioSampleType type, int expected)
    {
        Assert.Equal(expected, AsioSampleConverter.BytesPerSample(type));
    }

    [Fact]
    public void ConvertsFloat32()
    {
        float[] source = [0.5f, -0.25f, 1.0f, 0.0f];
        float[] result = Convert(source, AsioSampleType.Float32LSB);

        Assert.Equal(source, result);
    }

    [Fact]
    public void ConvertsInt32ToNormalizedFloats()
    {
        int[] source = [int.MaxValue, int.MinValue, 0, 1 << 30];
        float[] result = Convert(source, AsioSampleType.Int32LSB);

        Assert.Equal(int.MaxValue / 2147483648.0f, result[0]);
        Assert.Equal(-1.0f, result[1]);
        Assert.Equal(0.0f, result[2]);
        Assert.Equal(0.5f, result[3]);
    }

    [Fact]
    public void ConvertsInt16ToNormalizedFloats()
    {
        short[] source = [short.MaxValue, short.MinValue, 0, 16384];
        float[] result = Convert(source, AsioSampleType.Int16LSB);

        Assert.Equal(short.MaxValue / 32768.0f, result[0]);
        Assert.Equal(-1.0f, result[1]);
        Assert.Equal(0.0f, result[2]);
        Assert.Equal(0.5f, result[3]);
    }

    [Fact]
    public void ConvertsPackedInt24WithSignExtension()
    {
        // 0x400000 = +0.5; 0xC00000 = -0.5 (24-bit two's complement); 0xFFFFFF = -1 LSB.
        byte[] source =
        [
            0x00, 0x00, 0x40,
            0x00, 0x00, 0xC0,
            0xFF, 0xFF, 0xFF
        ];
        float[] result = Convert(source, AsioSampleType.Int24LSB, 3);

        Assert.Equal(0.5f, result[0]);
        Assert.Equal(-0.5f, result[1]);
        Assert.Equal(-1.0f / 8388608.0f, result[2]);
    }

    private static float[] Convert<T>(T[] source, AsioSampleType type)
        where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(source.AsSpan());
        return Convert(bytes, type, source.Length);
    }

    private static float[] Convert(
        ReadOnlySpan<byte> source,
        AsioSampleType type,
        int count)
    {
        var destination = new float[count];
        new AsioSampleConverter().Convert(source, type, destination, count);
        return destination;
    }
}
