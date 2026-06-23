using NAudio.Wave;

namespace Resonalyze;

/// <summary>
/// Generates deterministic broadband noise for repeatable live-spectrum measurements.
/// </summary>
public sealed class NoiseSignal : IDisposable
{
    private MemoryStream[] memoryStreams = Array.Empty<MemoryStream>();
    private RawSourceWaveStream[] sourceStreams = Array.Empty<RawSourceWaveStream>();
    private bool disposed;

    public byte[][] ByteData { get; private set; } = Array.Empty<byte[]>();
    public float[] FloatData { get; private set; } = Array.Empty<float>();
    public int SampleRate { get; private set; }
    public int Samples { get; private set; }
    public int BitsPerSample { get; private set; }
    public double RequestedDuration { get; private set; }

    public void FillData(
        double requestedDuration,
        int bitsPerSample = 24,
        int sampleRate = 44_100)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(requestedDuration) || requestedDuration <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedDuration));
        }
        if (bitsPerSample is not (16 or 24))
        {
            throw new NotSupportedException($"Unsupported sample size: {bitsPerSample} bits.");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        BitsPerSample = bitsPerSample;
        SampleRate = sampleRate;
        RequestedDuration = requestedDuration;
        Samples = checked((int)(sampleRate * requestedDuration));

        DisposeStreams();
        int channelModeCount = Enum.GetValues<PlaybackChannel>().Length;
        ByteData = new byte[channelModeCount][];
        FloatData = new float[Samples];
        memoryStreams = new MemoryStream[channelModeCount];
        sourceStreams = new RawSourceWaveStream[channelModeCount];

        // A fixed seed keeps measurements reproducible and makes regressions diagnosable.
        var random = new Random(42);
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
        {
            FloatData[sampleIndex] = (float)(random.NextDouble() - 0.5);
        }

        foreach (PlaybackChannel channel in Enum.GetValues<PlaybackChannel>())
        {
            int outputChannelCount = channel == PlaybackChannel.Mono ? 1 : 2;
            int bytesPerSample = bitsPerSample / 8;
            int maxValue = int.MaxValue >> (32 - bitsPerSample);
            byte[] data = new byte[Samples * bytesPerSample * outputChannelCount];

            for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++)
            {
                int sample = (int)(FloatData[sampleIndex] * maxValue);
                sample *= (int)Math.Pow(256, 4 - bytesPerSample);
                byte[] bytes = BitConverter.GetBytes(sample);
                WriteSample(data, sampleIndex, bytes, bytesPerSample, outputChannelCount, channel);
            }

            int channelIndex = (int)channel;
            ByteData[channelIndex] = data;
            memoryStreams[channelIndex] = new MemoryStream(data, writable: false);
            sourceStreams[channelIndex] = new RawSourceWaveStream(
                memoryStreams[channelIndex],
                new WaveFormat(SampleRate, BitsPerSample, outputChannelCount));
        }
    }

    public RawSourceWaveStream GetStream(PlaybackChannel channel)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        return sourceStreams[(int)channel];
    }

    private static void WriteSample(
        byte[] destination,
        int sampleIndex,
        byte[] source,
        int bytesPerSample,
        int channelCount,
        PlaybackChannel channel)
    {
        int frameOffset = sampleIndex * bytesPerSample * channelCount;
        int sourceOffset = 4 - bytesPerSample;

        if (channel == PlaybackChannel.Mono)
        {
            Array.Copy(source, sourceOffset, destination, frameOffset, bytesPerSample);
            return;
        }

        if (channel is PlaybackChannel.Left or PlaybackChannel.Stereo)
        {
            Array.Copy(source, sourceOffset, destination, frameOffset, bytesPerSample);
        }
        if (channel is PlaybackChannel.Right or PlaybackChannel.Stereo)
        {
            Array.Copy(
                source,
                sourceOffset,
                destination,
                frameOffset + bytesPerSample,
                bytesPerSample);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void DisposeStreams()
    {
        foreach (RawSourceWaveStream stream in sourceStreams)
        {
            stream.Dispose();
        }
        foreach (MemoryStream stream in memoryStreams)
        {
            stream.Dispose();
        }

        sourceStreams = Array.Empty<RawSourceWaveStream>();
        memoryStreams = Array.Empty<MemoryStream>();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisposeStreams();
        GC.SuppressFinalize(this);
    }
}
