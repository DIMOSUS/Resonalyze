using NAudio.Wave;

namespace Resonalyze.Audio.Tests;

public sealed class AudioRenderBufferReaderTests
{
    [Fact]
    public void Fill_ContinuesAfterShortReadsUntilBufferIsFull()
    {
        var source = new ChunkedWaveProvider([1, 2, 3, 4, 5, 6], chunkSize: 2);
        var buffer = new byte[6];

        AudioRenderBufferRead result = AudioRenderBufferReader.Fill(source, buffer);

        Assert.Equal(6, result.BytesRead);
        Assert.False(result.SourceEnded);
        Assert.Equal([1, 2, 3, 4, 5, 6], buffer);
    }

    [Fact]
    public void Fill_ZeroPadsFinalPartialBufferAndReportsEnd()
    {
        var source = new ChunkedWaveProvider([1, 2, 3], chunkSize: 2);
        var buffer = Enumerable.Repeat((byte)0xCC, 6).ToArray();

        AudioRenderBufferRead result = AudioRenderBufferReader.Fill(source, buffer);

        Assert.Equal(3, result.BytesRead);
        Assert.True(result.SourceEnded);
        Assert.Equal([1, 2, 3, 0, 0, 0], buffer);
    }

    [Fact]
    public void Fill_EmptySourceProducesSilentBuffer()
    {
        var source = new ChunkedWaveProvider([], chunkSize: 2);
        var buffer = Enumerable.Repeat((byte)0xCC, 4).ToArray();

        AudioRenderBufferRead result = AudioRenderBufferReader.Fill(source, buffer);

        Assert.Equal(0, result.BytesRead);
        Assert.True(result.SourceEnded);
        Assert.Equal([0, 0, 0, 0], buffer);
    }

    [Fact]
    public void RewoundSourceCanFillAnotherRun()
    {
        byte[] samples = [1, 2, 3, 4];
        using var memory = new MemoryStream(samples, writable: false);
        using var source = new RawSourceWaveStream(memory, new WaveFormat(48_000, 16, 1));
        var first = new byte[4];
        var second = new byte[4];

        AudioRenderBufferReader.Fill(source, first);
        source.Position = 0;
        AudioRenderBufferReader.Fill(source, second);

        Assert.Equal(first, second);
        Assert.Equal(samples, second);
    }

    private sealed class ChunkedWaveProvider : IWaveProvider
    {
        private readonly byte[] data;
        private readonly int chunkSize;
        private int position;

        public ChunkedWaveProvider(byte[] data, int chunkSize)
        {
            this.data = data;
            this.chunkSize = chunkSize;
        }

        public WaveFormat WaveFormat { get; } = new(48_000, 16, 1);

        public int Read(byte[] buffer, int offset, int count)
        {
            int available = Math.Min(data.Length - position, Math.Min(count, chunkSize));
            if (available <= 0)
            {
                return 0;
            }
            Array.Copy(data, position, buffer, offset, available);
            position += available;
            return available;
        }
    }
}
