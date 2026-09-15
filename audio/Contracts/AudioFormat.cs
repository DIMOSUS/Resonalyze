namespace Resonalyze.Audio;

public enum AudioSampleEncoding
{
    Pcm,
    IeeeFloat
}

public sealed record AudioFormat(
    int SampleRate,
    int BitsPerSample,
    int ChannelCount,
    AudioSampleEncoding Encoding)
{
    public override string ToString() =>
        $"{BitsPerSample} bit {Encoding}: {SampleRate}Hz {ChannelCount} channels";
}
