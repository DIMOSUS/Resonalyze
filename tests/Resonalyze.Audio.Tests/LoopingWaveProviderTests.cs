namespace Resonalyze.Audio.Tests;

public sealed class LoopingWaveProviderTests
{
    [Fact]
    public void Read_LoopsSeamlesslyAcrossTheSourceBoundary()
    {
        FloatArrayWaveStream source = FloatArrayWaveStream.FromMonoSamples(
            new float[] { 0.25f, -0.5f },
            sampleRate: 48_000,
            PlaybackChannel.Left);
        var provider = new LoopingWaveProvider(source);

        // The stereo source is 2 frames = 16 bytes; read 2.5 loops worth.
        var buffer = new byte[40];
        int read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        // Frame 0 restarts at byte 16 and 32: left channel carries 0.25f.
        Assert.Equal(0.25f, BitConverter.ToSingle(buffer, 0));
        Assert.Equal(0.25f, BitConverter.ToSingle(buffer, 16));
        Assert.Equal(0.25f, BitConverter.ToSingle(buffer, 32));
    }

    [Fact]
    public void Read_EmptySourceYieldsSilenceInsteadOfHanging()
    {
        FloatArrayWaveStream source = FloatArrayWaveStream.FromMonoSamples(
            Array.Empty<float>(),
            sampleRate: 48_000,
            PlaybackChannel.Stereo);
        var provider = new LoopingWaveProvider(source);

        var buffer = new byte[64];
        buffer[0] = 0xFF;
        int read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        Assert.All(buffer, value => Assert.Equal(0, value));
    }
}
