using NAudio.Wave;

namespace Resonalyze.Audio;

internal static class AudioFormatConversions
{
    private static readonly Guid FloatSubFormat =
        new("00000003-0000-0010-8000-00aa00389b71");

    public static AudioFormat ToAudioFormat(WaveFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format is WaveFormatExtensible extensible && extensible.SubFormat == FloatSubFormat);
        return new AudioFormat(
            format.SampleRate,
            format.BitsPerSample,
            format.Channels,
            isFloat ? AudioSampleEncoding.IeeeFloat : AudioSampleEncoding.Pcm);
    }
}
