using System.Runtime.InteropServices;
using NAudio.Wave.Asio;

namespace Resonalyze.Audio;

/// <summary>
/// Converts one ASIO input channel buffer to normalized floats in bulk: one
/// <see cref="Marshal.Copy(IntPtr, int[], int, int)"/> plus one typed pass,
/// instead of a per-sample interop call. The instance owns reusable scratch
/// buffers, so steady-state conversion allocates nothing — this runs inside the
/// ASIO buffer-switch callback where every microsecond delays playback.
/// </summary>
internal sealed class AsioSampleConverter
{
    private const float Int32Scale = 1.0f / 2147483648.0f;
    private const float Int24Scale = 1.0f / 8388608.0f;
    private const float Int16Scale = 1.0f / 32768.0f;

    private int[] intScratch = Array.Empty<int>();
    private short[] shortScratch = Array.Empty<short>();
    private byte[] byteScratch = Array.Empty<byte>();

    public void Convert(
        IntPtr buffer,
        AsioSampleType sampleType,
        float[] destination,
        int count)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (count < 0 || count > destination.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        switch (sampleType)
        {
            case AsioSampleType.Float32LSB:
                ConvertFloat32(buffer, destination, count, littleEndian: true);
                break;
            case AsioSampleType.Float32MSB:
                ConvertFloat32(buffer, destination, count, littleEndian: false);
                break;
            case AsioSampleType.Int32LSB:
            case AsioSampleType.Int32LSB16:
            case AsioSampleType.Int32LSB18:
            case AsioSampleType.Int32LSB20:
            case AsioSampleType.Int32LSB24:
                ConvertInt32(buffer, destination, count, littleEndian: true);
                break;
            case AsioSampleType.Int32MSB:
            case AsioSampleType.Int32MSB16:
            case AsioSampleType.Int32MSB18:
            case AsioSampleType.Int32MSB20:
            case AsioSampleType.Int32MSB24:
                ConvertInt32(buffer, destination, count, littleEndian: false);
                break;
            case AsioSampleType.Int24LSB:
                ConvertInt24(buffer, destination, count, littleEndian: true);
                break;
            case AsioSampleType.Int24MSB:
                ConvertInt24(buffer, destination, count, littleEndian: false);
                break;
            case AsioSampleType.Int16LSB:
                ConvertInt16(buffer, destination, count, littleEndian: true);
                break;
            case AsioSampleType.Int16MSB:
                ConvertInt16(buffer, destination, count, littleEndian: false);
                break;
            default:
                throw new NotSupportedException(
                    $"ASIO sample type '{sampleType}' is not supported.");
        }
    }

    private void ConvertFloat32(
        IntPtr buffer,
        float[] destination,
        int count,
        bool littleEndian)
    {
        if (littleEndian == BitConverter.IsLittleEndian)
        {
            Marshal.Copy(buffer, destination, 0, count);
            return;
        }

        int[] raw = EnsureIntScratch(count);
        Marshal.Copy(buffer, raw, 0, count);
        for (int i = 0; i < count; i++)
        {
            destination[i] = BitConverter.Int32BitsToSingle(ReverseBytes(raw[i]));
        }
    }

    private void ConvertInt32(
        IntPtr buffer,
        float[] destination,
        int count,
        bool littleEndian)
    {
        int[] raw = EnsureIntScratch(count);
        Marshal.Copy(buffer, raw, 0, count);
        if (littleEndian == BitConverter.IsLittleEndian)
        {
            for (int i = 0; i < count; i++)
            {
                destination[i] = raw[i] * Int32Scale;
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                destination[i] = ReverseBytes(raw[i]) * Int32Scale;
            }
        }
    }

    private void ConvertInt24(
        IntPtr buffer,
        float[] destination,
        int count,
        bool littleEndian)
    {
        byte[] raw = EnsureByteScratch(count * 3);
        Marshal.Copy(buffer, raw, 0, count * 3);
        for (int i = 0; i < count; i++)
        {
            int offset = i * 3;
            int value = littleEndian
                ? raw[offset] | (raw[offset + 1] << 8) | (raw[offset + 2] << 16)
                : raw[offset + 2] | (raw[offset + 1] << 8) | (raw[offset] << 16);
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }

            destination[i] = value * Int24Scale;
        }
    }

    private void ConvertInt16(
        IntPtr buffer,
        float[] destination,
        int count,
        bool littleEndian)
    {
        short[] raw = EnsureShortScratch(count);
        Marshal.Copy(buffer, raw, 0, count);
        if (littleEndian == BitConverter.IsLittleEndian)
        {
            for (int i = 0; i < count; i++)
            {
                destination[i] = raw[i] * Int16Scale;
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                destination[i] = ReverseBytes(raw[i]) * Int16Scale;
            }
        }
    }

    private int[] EnsureIntScratch(int count)
    {
        if (intScratch.Length < count)
        {
            intScratch = new int[count];
        }

        return intScratch;
    }

    private short[] EnsureShortScratch(int count)
    {
        if (shortScratch.Length < count)
        {
            shortScratch = new short[count];
        }

        return shortScratch;
    }

    private byte[] EnsureByteScratch(int count)
    {
        if (byteScratch.Length < count)
        {
            byteScratch = new byte[count];
        }

        return byteScratch;
    }

    private static int ReverseBytes(int value) =>
        ((value & 0x000000FF) << 24) |
        ((value & 0x0000FF00) << 8) |
        ((value & 0x00FF0000) >> 8) |
        ((value >> 24) & 0x000000FF);

    private static short ReverseBytes(short value) =>
        (short)(((value & 0x00FF) << 8) | ((value >> 8) & 0x00FF));
}
