using System.Buffers.Binary;
using NAudio.Wave.Asio;

namespace Resonalyze.Audio;

/// <summary>
/// Converts one copied ASIO queue channel to normalized floats on the capture
/// worker. The driver callback only copies bytes into a preallocated pump slot.
/// </summary>
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
                AsioSampleType.Int32LSB or AsioSampleType.Int32LSB16 or
                    AsioSampleType.Int32LSB18 or AsioSampleType.Int32LSB20 or
                    AsioSampleType.Int32LSB24 =>
                    BinaryPrimitives.ReadInt32LittleEndian(sample) * Int32Scale,
                AsioSampleType.Int32MSB or AsioSampleType.Int32MSB16 or
                    AsioSampleType.Int32MSB18 or AsioSampleType.Int32MSB20 or
                    AsioSampleType.Int32MSB24 =>
                    BinaryPrimitives.ReadInt32BigEndian(sample) * Int32Scale,
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
