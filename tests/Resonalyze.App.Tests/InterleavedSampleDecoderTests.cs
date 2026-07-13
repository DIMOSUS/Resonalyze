using NAudio.Wave;

namespace Resonalyze.App.Tests;

public sealed class InterleavedSampleDecoderTests
{
    [Fact]
    public void Pcm16DecodesInterleavedChannelsAndIgnoresTrailingBytes()
    {
        IInterleavedSampleDecoder decoder = InterleavedSampleDecoder.Create(
            new WaveFormat(48000, 16, 2));
        byte[] source = [0x00, 0x80, 0xff, 0x7f, 0x00, 0x00, 0x34];
        float[][] destination = [new float[2], new float[2]];

        int frames = decoder.Decode(source, destination);

        Assert.Equal(1, frames);
        Assert.Equal(-1.0f, destination[0][0]);
        Assert.InRange(destination[1][0], 0.9999f, 1.0f);
    }

    [Fact]
    public void Pcm24SignExtendsNegativeSamples()
    {
        IInterleavedSampleDecoder decoder = InterleavedSampleDecoder.Create(
            new WaveFormat(48000, 24, 1));
        float[][] destination = [new float[3]];

        int frames = decoder.Decode(
            [0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0xff, 0xff, 0x7f],
            destination);

        Assert.Equal(3, frames);
        Assert.Equal(-1.0f, destination[0][0]);
        Assert.Equal(0.0f, destination[0][1]);
        Assert.InRange(destination[0][2], 0.9999f, 1.0f);
    }

    [Fact]
    public void Pcm32DecodesFullScaleValues()
    {
        IInterleavedSampleDecoder decoder = InterleavedSampleDecoder.Create(
            new WaveFormat(48000, 32, 1));
        float[][] destination = [new float[2]];

        decoder.Decode([0, 0, 0, 0x80, 0xff, 0xff, 0xff, 0x7f], destination);

        Assert.Equal(-1.0f, destination[0][0]);
        Assert.InRange(destination[0][1], 0.9999f, 1.0f);
    }

    [Fact]
    public void Float32ClampsAndReplacesNonFiniteValues()
    {
        IInterleavedSampleDecoder decoder = InterleavedSampleDecoder.Create(
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        float[][] destination = [new float[4]];
        byte[] source = new float[] { -2.0f, 0.25f, 2.0f, float.NaN }
            .SelectMany(BitConverter.GetBytes)
            .ToArray();

        decoder.Decode(source, destination);

        Assert.Equal([-1.0f, 0.25f, 1.0f, 0.0f], destination[0]);
    }

    [Fact]
    public void DecodeSupportsEightChannels()
    {
        IInterleavedSampleDecoder decoder = InterleavedSampleDecoder.Create(
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 8));
        float[] values = Enumerable.Range(0, 8).Select(index => index / 8.0f).ToArray();
        byte[] source = values.SelectMany(BitConverter.GetBytes).ToArray();
        float[][] destination = Enumerable.Range(0, 8).Select(_ => new float[1]).ToArray();

        Assert.Equal(1, decoder.Decode(source, destination));
        for (int channel = 0; channel < values.Length; channel++)
        {
            Assert.Equal(values[channel], destination[channel][0]);
        }
    }
}
