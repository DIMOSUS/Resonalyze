using System.Runtime.InteropServices;
using NAudio.Wave.Asio;

namespace Resonalyze.Audio;

/// <summary>
/// Moves ASIO input processing off the driver's buffer-switch callback via the
/// bounded slot pool of <see cref="CapturePump{TSlot,TBlock}"/>. Unlike the PCM
/// pump, the pool cannot be sized at construction: the buffer size only becomes
/// known when the driver opens, so <see cref="Prepare"/> allocates the slots and
/// their per-channel buffers before playback starts and the callback then only
/// copies into that fixed pool.
/// </summary>
internal sealed class AsioCapturePump : CapturePump<AsioCapturePump.Slot, AsioCaptureBlock>
{
    private const int SlotCount = 8;

    private readonly int channelCount;
    private int preparedByteCapacity;

    public AsioCapturePump(
        int channelCount,
        Action<AsioCaptureBlock> processBlock,
        Action<int, Exception> reportFailure)
        : base(
            SlotCount,
            "ASIO",
            "ASIO capture processing could not keep up with the driver; input buffers were not recorded.",
            processBlock,
            reportFailure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        this.channelCount = channelCount;
    }

    /// <summary>Allocates the complete callback buffer pool before the driver starts.</summary>
    public void Prepare(int maximumByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumByteCount);
        lock (Sync)
        {
            WaitForIdle();
            if (HasPendingSlots)
            {
                throw new InvalidOperationException("Reset the ASIO capture pump before preparing it.");
            }
            if (preparedByteCapacity >= maximumByteCount)
            {
                return;
            }

            AllocateSlots(() => new Slot(channelCount, maximumByteCount));
            preparedByteCapacity = maximumByteCount;
        }
    }

    public bool TryEnqueue(
        IntPtr[] inputBuffers,
        int inputChannelOffset,
        AsioSampleType sampleType,
        int frameCount)
    {
        int bytesPerSample = AsioSampleConverter.BytesPerSample(sampleType);
        int byteCount = checked(frameCount * bytesPerSample);

        lock (Sync)
        {
            if (IsStoppedOrFailed)
            {
                return false;
            }
            if (!HasSlots)
            {
                throw new InvalidOperationException("ASIO capture buffers were not prepared before playback.");
            }
            if (byteCount > preparedByteCapacity)
            {
                throw new InvalidOperationException(
                    $"ASIO packet size {byteCount} exceeds the prepared capacity {preparedByteCapacity}.");
            }
            if (!TryTakeSlot(frameCount, out int slotIndex, out Slot slot))
            {
                return false;
            }

            for (int channel = 0; channel < channelCount; channel++)
            {
                Marshal.Copy(
                    inputBuffers[inputChannelOffset + channel],
                    slot.Channels[channel],
                    0,
                    byteCount);
            }

            slot.FrameCount = frameCount;
            slot.SampleType = sampleType;
            PublishSlot(slotIndex, frameCount);
            return true;
        }
    }

    protected override AsioCaptureBlock CreateBlock(Slot slot) => new(
        slot.Channels,
        slot.SampleType,
        slot.FrameCount,
        slot.Generation);

    internal sealed class Slot : ICapturePumpSlot
    {
        public Slot(int channelCount, int maximumByteCount)
        {
            Channels = new byte[channelCount][];
            for (int channel = 0; channel < channelCount; channel++)
            {
                Channels[channel] = new byte[maximumByteCount];
            }
        }

        public byte[][] Channels { get; }
        public AsioSampleType SampleType { get; set; }
        public int FrameCount { get; set; }
        public int Generation { get; set; }
    }
}

internal readonly record struct AsioCaptureBlock(
    byte[][] Channels,
    AsioSampleType SampleType,
    int FrameCount,
    int Generation);
