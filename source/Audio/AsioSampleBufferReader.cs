using System.Runtime.InteropServices;
using NAudio.Wave.Asio;

namespace Resonalyze;

internal static class AsioSampleBufferReader
{
    public static float ReadSample(
        IntPtr buffer,
        int sampleIndex,
        AsioSampleType sampleType)
    {
        return sampleType switch
        {
            AsioSampleType.Float32LSB => ReadFloat32(buffer, sampleIndex, littleEndian: true),
            AsioSampleType.Float32MSB => ReadFloat32(buffer, sampleIndex, littleEndian: false),
            AsioSampleType.Int32LSB => ReadInt32(buffer, sampleIndex, littleEndian: true) / 2147483648.0f,
            AsioSampleType.Int32MSB => ReadInt32(buffer, sampleIndex, littleEndian: false) / 2147483648.0f,
            AsioSampleType.Int32LSB16 => ReadInt32(buffer, sampleIndex, littleEndian: true) / 2147483648.0f,
            AsioSampleType.Int32MSB16 => ReadInt32(buffer, sampleIndex, littleEndian: false) / 2147483648.0f,
            AsioSampleType.Int32LSB18 => ReadInt32(buffer, sampleIndex, littleEndian: true) / 2147483648.0f,
            AsioSampleType.Int32MSB18 => ReadInt32(buffer, sampleIndex, littleEndian: false) / 2147483648.0f,
            AsioSampleType.Int32LSB20 => ReadInt32(buffer, sampleIndex, littleEndian: true) / 2147483648.0f,
            AsioSampleType.Int32MSB20 => ReadInt32(buffer, sampleIndex, littleEndian: false) / 2147483648.0f,
            AsioSampleType.Int32LSB24 => ReadInt32(buffer, sampleIndex, littleEndian: true) / 2147483648.0f,
            AsioSampleType.Int32MSB24 => ReadInt32(buffer, sampleIndex, littleEndian: false) / 2147483648.0f,
            AsioSampleType.Int24LSB => ReadInt24(buffer, sampleIndex, littleEndian: true) / 8388608.0f,
            AsioSampleType.Int24MSB => ReadInt24(buffer, sampleIndex, littleEndian: false) / 8388608.0f,
            AsioSampleType.Int16LSB => ReadInt16(buffer, sampleIndex, littleEndian: true) / 32768.0f,
            AsioSampleType.Int16MSB => ReadInt16(buffer, sampleIndex, littleEndian: false) / 32768.0f,
            _ => throw new NotSupportedException(
                $"ASIO sample type '{sampleType}' is not supported.")
        };
    }

    private static float ReadFloat32(
        IntPtr buffer,
        int sampleIndex,
        bool littleEndian)
    {
        int raw = ReadInt32(buffer, sampleIndex, littleEndian);
        return BitConverter.Int32BitsToSingle(raw);
    }

    private static int ReadInt32(
        IntPtr buffer,
        int sampleIndex,
        bool littleEndian)
    {
        int offset = sampleIndex * 4;
        int value = Marshal.ReadInt32(buffer, offset);
        return BitConverter.IsLittleEndian == littleEndian
            ? value
            : ReverseBytes(value);
    }

    private static int ReadInt24(
        IntPtr buffer,
        int sampleIndex,
        bool littleEndian)
    {
        int offset = sampleIndex * 3;
        int b0 = Marshal.ReadByte(buffer, offset);
        int b1 = Marshal.ReadByte(buffer, offset + 1);
        int b2 = Marshal.ReadByte(buffer, offset + 2);
        int value = littleEndian
            ? b0 | (b1 << 8) | (b2 << 16)
            : b2 | (b1 << 8) | (b0 << 16);
        return (value & 0x800000) != 0
            ? value | unchecked((int)0xFF000000)
            : value;
    }

    private static short ReadInt16(
        IntPtr buffer,
        int sampleIndex,
        bool littleEndian)
    {
        int offset = sampleIndex * 2;
        short value = Marshal.ReadInt16(buffer, offset);
        return BitConverter.IsLittleEndian == littleEndian
            ? value
            : ReverseBytes(value);
    }

    private static int ReverseBytes(int value) =>
        ((value & 0x000000FF) << 24) |
        ((value & 0x0000FF00) << 8) |
        ((value & 0x00FF0000) >> 8) |
        ((value & unchecked((int)0xFF000000)) >> 24);

    private static short ReverseBytes(short value) =>
        (short)(((value & 0x00FF) << 8) | ((value & 0xFF00) >> 8));
}
