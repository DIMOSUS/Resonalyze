using System.Buffers.Binary;
using NAudio.Wave;

namespace Resonalyze.Audio;

internal static class InterleavedSampleDecoder
{
    private static readonly Guid PcmSubFormat =
        new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubFormat =
        new("00000003-0000-0010-8000-00aa00389b71");

    public static IInterleavedSampleDecoder Create(WaveFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extensible &&
            extensible.SubFormat == FloatSubFormat;
        if (isFloat && format.BitsPerSample == 32)
        {
            return new Decoder(format.Channels, format.BlockAlign, 4, SampleEncoding.Float32);
        }

        bool isPcm = format.Encoding == WaveFormatEncoding.Pcm ||
            format is WaveFormatExtensible pcmExtensible &&
            pcmExtensible.SubFormat == PcmSubFormat;
        if (!isPcm || format.BitsPerSample is not (16 or 24 or 32))
        {
            throw new NotSupportedException($"Unsupported capture format: {format}.");
        }

        SampleEncoding encoding = format.BitsPerSample switch
        {
            16 => SampleEncoding.Pcm16,
            24 => SampleEncoding.Pcm24,
            _ => SampleEncoding.Pcm32
        };
        return new Decoder(
            format.Channels,
            format.BlockAlign,
            format.BitsPerSample / 8,
            encoding);
    }

    private enum SampleEncoding
    {
        Pcm16,
        Pcm24,
        Pcm32,
        Float32
    }

    private sealed class Decoder : IInterleavedSampleDecoder
    {
        private readonly int bytesPerFrame;
        private readonly int bytesPerSample;
        private readonly SampleEncoding encoding;

        public Decoder(
            int channelCount,
            int bytesPerFrame,
            int bytesPerSample,
            SampleEncoding encoding)
        {
            ChannelCount = channelCount;
            this.bytesPerFrame = bytesPerFrame;
            this.bytesPerSample = bytesPerSample;
            this.encoding = encoding;
        }

        public int ChannelCount { get; }

        public int Decode(ReadOnlySpan<byte> source, float[][] destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (destination.Length < ChannelCount)
            {
                throw new ArgumentException("A destination is required for every channel.", nameof(destination));
            }

            int frameCount = source.Length / bytesPerFrame;
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                if (destination[channel].Length < frameCount)
                {
                    throw new ArgumentException("Destination buffers are too short.", nameof(destination));
                }
            }

            for (int frame = 0; frame < frameCount; frame++)
            {
                int frameOffset = frame * bytesPerFrame;
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    int offset = frameOffset + channel * bytesPerSample;
                    destination[channel][frame] = DecodeSample(source.Slice(offset, bytesPerSample));
                }
            }

            return frameCount;
        }

        private float DecodeSample(ReadOnlySpan<byte> sample)
        {
            if (encoding == SampleEncoding.Float32)
            {
                float floatValue = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(sample));
                return float.IsFinite(floatValue)
                    ? Math.Clamp(floatValue, -1.0f, 1.0f)
                    : 0.0f;
            }

            int value = encoding switch
            {
                SampleEncoding.Pcm16 => BinaryPrimitives.ReadInt16LittleEndian(sample),
                SampleEncoding.Pcm24 => ReadPcm24(sample),
                _ => BinaryPrimitives.ReadInt32LittleEndian(sample)
            };
            double scale = encoding switch
            {
                SampleEncoding.Pcm16 => 32768.0,
                SampleEncoding.Pcm24 => 8388608.0,
                _ => 2147483648.0
            };
            return (float)Math.Clamp(value / scale, -1.0, 1.0);
        }

        private static int ReadPcm24(ReadOnlySpan<byte> sample)
        {
            int value = sample[0] | sample[1] << 8 | sample[2] << 16;
            return (value & 0x800000) == 0 ? value : value | unchecked((int)0xff000000);
        }
    }
}
