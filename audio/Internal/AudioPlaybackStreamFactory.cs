using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>PCM for Wave/MME/WASAPI, IEEE float for ASIO.</summary>
internal static class AudioPlaybackStreamFactory
{
    public static PcmPlaybackStream CreatePcm(AudioPlaybackSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var set = new PcmStreamSet(signal.MonoSamples, signal.SampleRate, signal.BitsPerSample);
        RawSourceWaveStream stream = set.GetStream(signal.PlaybackChannel);
        return new PcmPlaybackStream(set, stream);
    }

    public static FloatArrayWaveStream CreateFloat(AudioPlaybackSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return FloatArrayWaveStream.FromMonoSamples(
            signal.MonoSamples,
            signal.SampleRate,
            signal.PlaybackChannel);
    }
}

internal sealed class PcmPlaybackStream : IDisposable
{
    private readonly PcmStreamSet set;

    public PcmPlaybackStream(PcmStreamSet set, WaveStream stream)
    {
        this.set = set;
        Stream = stream;
    }

    public WaveStream Stream { get; }

    public void Rewind() => Stream.Position = 0;

    public void Dispose() => set.Dispose();
}
