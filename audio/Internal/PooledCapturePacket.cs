using System.Buffers;
using System.Runtime.InteropServices;

namespace Resonalyze.Audio;

/// <summary>
/// Owns a temporary managed copy of a native capture packet. The memory is valid only until
/// disposal, so capture callbacks must copy any data they need to retain.
/// </summary>
internal sealed class PooledCapturePacket : IDisposable
{
    private readonly ArrayPool<byte> pool;
    private byte[]? buffer;

    private PooledCapturePacket(ArrayPool<byte> pool, int byteCount)
    {
        this.pool = pool;
        buffer = pool.Rent(byteCount);
        Length = byteCount;
    }

    public int Length { get; }

    public ReadOnlyMemory<byte> Memory
    {
        get
        {
            byte[] current = buffer ?? throw new ObjectDisposedException(nameof(PooledCapturePacket));
            return current.AsMemory(0, Length);
        }
    }

    public static PooledCapturePacket CopyFromNative(
        IntPtr source,
        int byteCount,
        bool silent,
        ArrayPool<byte>? pool = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        if (!silent && byteCount > 0 && source == IntPtr.Zero)
        {
            throw new ArgumentException("A non-silent packet requires a native source buffer.", nameof(source));
        }

        var packet = new PooledCapturePacket(pool ?? ArrayPool<byte>.Shared, byteCount);
        try
        {
            byte[] rented = packet.buffer!;
            Span<byte> destination = rented.AsSpan(0, byteCount);
            if (silent)
            {
                destination.Clear();
            }
            else if (byteCount > 0)
            {
                Marshal.Copy(source, rented, 0, byteCount);
            }

            return packet;
        }
        catch
        {
            packet.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        byte[]? returned = Interlocked.Exchange(ref buffer, null);
        if (returned != null)
        {
            pool.Return(returned);
        }
    }
}
