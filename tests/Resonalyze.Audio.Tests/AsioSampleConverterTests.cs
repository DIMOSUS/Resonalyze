using System.Runtime.InteropServices;
using NAudio.Wave.Asio;

namespace Resonalyze.Audio.Tests;

public sealed class AsioSampleConverterTests
{
    [Fact]
    public void ConvertsFloat32()
    {
        float[] source = [0.5f, -0.25f, 1.0f, 0.0f];
        float[] result = Convert(source, AsioSampleType.Float32LSB, source.Length);

        Assert.Equal(source, result);
    }

    [Fact]
    public void ConvertsInt32ToNormalizedFloats()
    {
        int[] source = [int.MaxValue, int.MinValue, 0, 1 << 30];
        float[] result = Convert(source, AsioSampleType.Int32LSB, source.Length);

        Assert.Equal(int.MaxValue / 2147483648.0f, result[0]);
        Assert.Equal(-1.0f, result[1]);
        Assert.Equal(0.0f, result[2]);
        Assert.Equal(0.5f, result[3]);
    }

    [Fact]
    public void ConvertsInt16ToNormalizedFloats()
    {
        short[] source = [short.MaxValue, short.MinValue, 0, 16384];
        float[] result = Convert(source, AsioSampleType.Int16LSB, source.Length);

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

    [Fact]
    public void ScratchBuffersAreReusedAcrossCalls()
    {
        var converter = new AsioSampleConverter();
        int[] first = [1 << 30, 0];
        int[] second = [0, -(1 << 30)];

        float[] a = ConvertWith(converter, first, AsioSampleType.Int32LSB, 2);
        float[] b = ConvertWith(converter, second, AsioSampleType.Int32LSB, 2);

        Assert.Equal(new[] { 0.5f, 0.0f }, a);
        Assert.Equal(new[] { 0.0f, -0.5f }, b);
    }

    private static float[] Convert<T>(T[] source, AsioSampleType type, int count)
        where T : struct =>
        ConvertWith(new AsioSampleConverter(), source, type, count);

    private static float[] ConvertWith<T>(
        AsioSampleConverter converter,
        T[] source,
        AsioSampleType type,
        int count)
        where T : struct
    {
        var destination = new float[count];
        GCHandle handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            converter.Convert(handle.AddrOfPinnedObject(), type, destination, count);
        }
        finally
        {
            handle.Free();
        }

        return destination;
    }
}
