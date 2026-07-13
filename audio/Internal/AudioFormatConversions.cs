using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>
/// The single place NAudio <see cref="WaveFormat"/> is translated to and from
/// the neutral <see cref="AudioFormat"/>, so the concrete NAudio type stays an
/// implementation detail behind the audio boundary.
/// </summary>
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
