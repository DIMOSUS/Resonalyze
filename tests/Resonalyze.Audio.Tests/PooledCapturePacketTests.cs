using System.Buffers;
using System.Runtime.InteropServices;

namespace Resonalyze.Audio.Tests;

public sealed class PooledCapturePacketTests
{
    [Fact]
    public void CopyFromNative_CopiesPacketIntoPooledMemory()
    {
        byte[] source = [1, 2, 3, 4];
        GCHandle handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            using PooledCapturePacket packet = PooledCapturePacket.CopyFromNative(
                handle.AddrOfPinnedObject(),
                source.Length,
                silent: false);

            Array.Clear(source);

            Assert.Equal([1, 2, 3, 4], packet.Memory.ToArray());
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void CopyFromNative_SilentPacketClearsReusedMemory()
    {
        var pool = new TrackingArrayPool(8, 0x7f);

        using (PooledCapturePacket packet = PooledCapturePacket.CopyFromNative(
            IntPtr.Zero,
            4,
            silent: true,
            pool))
        {
            Assert.Equal([0, 0, 0, 0], packet.Memory.ToArray());
        }

        Assert.Equal(1, pool.ReturnCount);
    }

    [Fact]
    public void Dispose_ReturnsBufferOnlyOnce_AndInvalidatesMemory()
    {
        var pool = new TrackingArrayPool(8, 0);
        PooledCapturePacket packet = PooledCapturePacket.CopyFromNative(
            IntPtr.Zero,
            4,
            silent: true,
            pool);

        packet.Dispose();
        packet.Dispose();

        Assert.Equal(1, pool.ReturnCount);
        Assert.Throws<ObjectDisposedException>(() => packet.Memory);
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly byte[] buffer;

        public TrackingArrayPool(int length, byte initialValue)
        {
            buffer = new byte[length];
            Array.Fill(buffer, initialValue);
        }

        public int ReturnCount { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            Assert.True(minimumLength <= buffer.Length);
            return buffer;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.Same(buffer, array);
            ReturnCount++;
        }
    }
}
