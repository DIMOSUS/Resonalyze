using NAudio.Wave;

namespace Resonalyze.App.Tests;

public sealed class WasapiFormatSupportTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void CreateDeviceFormatPreservesRequestedShape(int bits)
    {
        WaveFormat format = WasapiFormatSupport.CreateDeviceFormat(96_000, bits, 8);

        Assert.Equal(96_000, format.SampleRate);
        Assert.Equal(bits, format.BitsPerSample);
        Assert.Equal(8, format.Channels);
        Assert.Equal(WaveFormatEncoding.Extensible, format.Encoding);
        Guid expectedSubFormat = bits == 32
            ? new Guid("00000003-0000-0010-8000-00aa00389b71")
            : new Guid("00000001-0000-0010-8000-00aa00389b71");
        Assert.Equal(expectedSubFormat, Assert.IsType<WaveFormatExtensible>(format).SubFormat);
    }

    [Fact]
    public void CreateDeviceFormatRejectsUnsupportedBitDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WasapiFormatSupport.CreateDeviceFormat(48_000, 20, 2));
    }
}
