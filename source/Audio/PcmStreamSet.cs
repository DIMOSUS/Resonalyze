using NAudio.Wave;

namespace Resonalyze;

/// <summary>
/// Packs a mono float signal into PCM playback streams, one per
/// <see cref="PlaybackChannel"/> routing, built lazily on first request: a
/// measurement uses exactly one routing, while the previous eager four-way
/// packing kept four PCM copies of the signal (tens of megabytes for long
/// sweeps and noise buffers) alive for its whole lifetime.
/// </summary>
internal sealed class PcmStreamSet : IDisposable
{
    private static readonly int ChannelModeCount =
        Enum.GetValues<PlaybackChannel>().Length;

    private readonly float[] samples;
    private readonly int sampleRate;
    private readonly int bitsPerSample;
    private readonly MemoryStream?[] memoryStreams = new MemoryStream?[ChannelModeCount];
    private readonly RawSourceWaveStream?[] sourceStreams =
        new RawSourceWaveStream?[ChannelModeCount];
    private bool disposed;

    public PcmStreamSet(float[] samples, int sampleRate, int bitsPerSample)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (bitsPerSample is not (16 or 24))
        {
            throw new NotSupportedException(
                $"Unsupported sample size: {bitsPerSample} bits.");
        }

        this.samples = samples;
        this.sampleRate = sampleRate;
        this.bitsPerSample = bitsPerSample;
    }

    public RawSourceWaveStream GetStream(PlaybackChannel channel)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        int index = (int)channel;
        if (sourceStreams[index] is { } existing)
        {
            return existing;
        }

        var memory = new MemoryStream(
            Pack(samples, bitsPerSample, channel),
            writable: false);
        int outputChannelCount = channel == PlaybackChannel.Mono ? 1 : 2;
        var stream = new RawSourceWaveStream(
            memory,
            new WaveFormat(sampleRate, bitsPerSample, outputChannelCount));
        memoryStreams[index] = memory;
        sourceStreams[index] = stream;
        return stream;
    }

    /// <summary>
    /// Packs normalized floats into little-endian PCM frames for the routing:
    /// Mono is a single channel; Left/Right are stereo frames with the other
    /// side silent; Stereo carries the signal on both sides.
    /// </summary>
    internal static byte[] Pack(
        IReadOnlyList<float> samples,
        int bitsPerSample,
        PlaybackChannel channel)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (bitsPerSample is not (16 or 24))
        {
            throw new NotSupportedException(
                $"Unsupported sample size: {bitsPerSample} bits.");
        }
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        int outputChannelCount = channel == PlaybackChannel.Mono ? 1 : 2;
        int bytesPerSample = bitsPerSample / 8;
        int maxValue = int.MaxValue >> (32 - bitsPerSample);
        var data = new byte[samples.Count * bytesPerSample * outputChannelCount];

        for (int sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
        {
            int value = (int)(Math.Clamp(samples[sampleIndex], -1.0f, 1.0f) * maxValue);
            int frameOffset = sampleIndex * bytesPerSample * outputChannelCount;
            if (channel is PlaybackChannel.Mono or PlaybackChannel.Left or PlaybackChannel.Stereo)
            {
                WritePcmSample(data, frameOffset, value, bytesPerSample);
            }
            if (channel is PlaybackChannel.Right or PlaybackChannel.Stereo)
            {
                WritePcmSample(data, frameOffset + bytesPerSample, value, bytesPerSample);
            }
        }

        return data;
    }

    private static void WritePcmSample(
        byte[] destination,
        int offset,
        int value,
        int bytesPerSample)
    {
        for (int i = 0; i < bytesPerSample; i++)
        {
            destination[offset + i] = (byte)(value >> (8 * i));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (RawSourceWaveStream? stream in sourceStreams)
        {
            stream?.Dispose();
        }
        foreach (MemoryStream? stream in memoryStreams)
        {
            stream?.Dispose();
        }
    }
}
