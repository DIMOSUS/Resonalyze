namespace Resonalyze.Audio;

/// <summary>Deinterleaved PCM: one <c>float[]</c> per channel, all the same length.</summary>
public sealed record AudioFileContent(float[][] Channels, int SampleRate)
{
    public int ChannelCount => Channels.Length;

    public int FrameCount => Channels.Length == 0 ? 0 : Channels[0].Length;
}

public sealed record AudioFileInfo(
    int ChannelCount,
    int SampleRate,
    TimeSpan Duration);
