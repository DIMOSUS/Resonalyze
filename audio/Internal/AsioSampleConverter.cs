using System.Buffers.Binary;
using NAudio.Wave.Asio;

namespace Resonalyze.Audio;

internal sealed class AsioSampleConverter
{
    private const float Int32Scale = 1.0f / 2147483648.0f;
    private const float Int24Scale = 1.0f / 8388608.0f;
    private const float Int16Scale = 1.0f / 32768.0f;

    public void Convert(
        ReadOnlySpan<byte> buffer,
        AsioSampleType sampleType,
        Span<float> destination,
        int count)
    {
        int bytesPerSample = BytesPerSample(sampleType);
        if (count < 0 || count > destination.Length || buffer.Length < count * bytesPerSample)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> sample = buffer.Slice(i * bytesPerSample, bytesPerSample);
            destination[i] = sampleType switch
            {
                AsioSampleType.Float32LSB => BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(sample)),
                AsioSampleType.Float32MSB => BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32BigEndian(sample)),
                AsioSampleType.Int32LSB =>
                    BinaryPrimitives.ReadInt32LittleEndian(sample) * Int32Scale,
                AsioSampleType.Int32MSB =>
                    BinaryPrimitives.ReadInt32BigEndian(sample) * Int32Scale,
                // Right-aligned: 16-24 valid bits in a 32-bit container, full scale at 2^(bits-1), not 2^31.
                AsioSampleType.Int32LSB16 or AsioSampleType.Int32LSB18 or
                    AsioSampleType.Int32LSB20 or AsioSampleType.Int32LSB24 =>
                    RightAligned(BinaryPrimitives.ReadInt32LittleEndian(sample), ValidBits(sampleType)),
                AsioSampleType.Int32MSB16 or AsioSampleType.Int32MSB18 or
                    AsioSampleType.Int32MSB20 or AsioSampleType.Int32MSB24 =>
                    RightAligned(BinaryPrimitives.ReadInt32BigEndian(sample), ValidBits(sampleType)),
                AsioSampleType.Int24LSB => ReadInt24LittleEndian(sample) * Int24Scale,
                AsioSampleType.Int24MSB => ReadInt24BigEndian(sample) * Int24Scale,
                AsioSampleType.Int16LSB =>
                    BinaryPrimitives.ReadInt16LittleEndian(sample) * Int16Scale,
                AsioSampleType.Int16MSB =>
                    BinaryPrimitives.ReadInt16BigEndian(sample) * Int16Scale,
                _ => throw new NotSupportedException(
                    $"ASIO sample type '{sampleType}' is not supported.")
            };
        }
    }

    internal static int BytesPerSample(AsioSampleType sampleType) => sampleType switch
    {
        AsioSampleType.Int16LSB or AsioSampleType.Int16MSB => 2,
        AsioSampleType.Int24LSB or AsioSampleType.Int24MSB => 3,
        AsioSampleType.Float32LSB or AsioSampleType.Float32MSB or
            AsioSampleType.Int32LSB or AsioSampleType.Int32MSB or
            AsioSampleType.Int32LSB16 or AsioSampleType.Int32MSB16 or
            AsioSampleType.Int32LSB18 or AsioSampleType.Int32MSB18 or
            AsioSampleType.Int32LSB20 or AsioSampleType.Int32MSB20 or
            AsioSampleType.Int32LSB24 or AsioSampleType.Int32MSB24 => 4,
        _ => throw new NotSupportedException(
            $"ASIO sample type '{sampleType}' is not supported.")
    };

    private static int ValidBits(AsioSampleType sampleType) => sampleType switch
    {
        AsioSampleType.Int32LSB16 or AsioSampleType.Int32MSB16 => 16,
        AsioSampleType.Int32LSB18 or AsioSampleType.Int32MSB18 => 18,
        AsioSampleType.Int32LSB20 or AsioSampleType.Int32MSB20 => 20,
        _ => 24
    };

    // Sign-extended from the valid bits, whether or not the driver extended it into the container.
    private static float RightAligned(int container, int bits)
    {
        int shift = 32 - bits;
        int value = (container << shift) >> shift;
        return value / (float)(1 << (bits - 1));
    }

    private static int ReadInt24LittleEndian(ReadOnlySpan<byte> value)
    {
        int result = value[0] | (value[1] << 8) | (value[2] << 16);
        return (result & 0x800000) != 0
            ? result | unchecked((int)0xFF000000)
            : result;
    }

    private static int ReadInt24BigEndian(ReadOnlySpan<byte> value)
    {
        int result = value[2] | (value[1] << 8) | (value[0] << 16);
        return (result & 0x800000) != 0
            ? result | unchecked((int)0xFF000000)
            : result;
    }
}
