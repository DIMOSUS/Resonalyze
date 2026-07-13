using NAudio.Wave;

namespace Resonalyze.Audio;

internal sealed class FloatArrayWaveStream : WaveStream
{
    private readonly byte[] bytes;
    private long position;

    private FloatArrayWaveStream(byte[] bytes, int sampleRate, int channels)
    {
        this.bytes = bytes;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public override WaveFormat WaveFormat { get; }
    public override long Length => bytes.Length;

    public override long Position
    {
        get => position;
        set => position = Math.Clamp(value, 0, bytes.Length);
    }

    public static FloatArrayWaveStream FromMonoSamples(
        IReadOnlyList<float> monoSamples,
        int sampleRate,
        PlaybackChannel playbackChannel)
    {
        // ASIO does not duplicate mono providers to stereo outputs for us. Keep the
        // provider stereo and encode the requested routing explicitly.
        const int channels = 2;
        byte[] data = new byte[monoSamples.Count * channels * sizeof(float)];
        for (int frame = 0; frame < monoSamples.Count; frame++)
        {
            float sample = monoSamples[frame];
            int frameOffset = frame * channels * sizeof(float);
            float left = playbackChannel is PlaybackChannel.Mono or PlaybackChannel.Left or PlaybackChannel.Stereo
                ? sample
                : 0.0f;
            float right = playbackChannel is PlaybackChannel.Mono or PlaybackChannel.Right or PlaybackChannel.Stereo
                ? sample
                : 0.0f;
            WriteFloat(data, frameOffset, left);
            WriteFloat(data, frameOffset + sizeof(float), right);
        }

        return new FloatArrayWaveStream(data, sampleRate, channels);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int available = (int)Math.Min(count, bytes.Length - position);
        if (available <= 0)
        {
            return 0;
        }

        Array.Copy(bytes, position, buffer, offset, available);
        position += available;
        return available;
    }

    private static void WriteFloat(byte[] destination, int offset, float value)
    {
        // No per-sample byte[] allocation: this runs once per sample over
        // buffers up to minutes long when the signal is prepared.
        BitConverter.TryWriteBytes(destination.AsSpan(offset), value);
    }
}
