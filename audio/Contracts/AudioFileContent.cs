namespace Resonalyze.Audio;

/// <summary>
/// Deinterleaved PCM decoded from — or destined for — a media file: one
/// <c>float[]</c> per channel, all of the same length, plus the rate they were
/// sampled at. Backend-neutral like the rest of the contracts, so the decoder's
/// NAudio/Media Foundation machinery stays inside this project.
/// </summary>
public sealed record AudioFileContent(float[][] Channels, int SampleRate)
{
    public int ChannelCount => Channels.Length;

    public int FrameCount => Channels.Length == 0 ? 0 : Channels[0].Length;
}

/// <summary>
/// A media file's shape without its samples — what a picker can show the user
/// before committing to a full decode.
/// </summary>
public sealed record AudioFileInfo(
    int ChannelCount,
    int SampleRate,
    TimeSpan Duration);
