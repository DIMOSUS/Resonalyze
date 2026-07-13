namespace Resonalyze.Audio;

/// <summary>
/// How PCM/float samples are encoded in an interleaved buffer. Deliberately
/// backend-neutral: the concrete NAudio <c>WaveFormatEncoding</c> never crosses
/// the <see cref="Resonalyze.Audio"/> boundary.
/// </summary>
public enum AudioSampleEncoding
{
    Pcm,
    IeeeFloat
}

/// <summary>
/// A backend-neutral description of a stream format. Replaces the NAudio
/// <c>WaveFormat</c> at the public boundary of the audio library.
/// </summary>
public sealed record AudioFormat(
    int SampleRate,
    int BitsPerSample,
    int ChannelCount,
    AudioSampleEncoding Encoding)
{
    public override string ToString() =>
        $"{BitsPerSample} bit {Encoding}: {SampleRate}Hz {ChannelCount} channels";
}
