namespace Resonalyze.Audio;

/// <summary>
/// Copies device-owned PCM packets into the bounded slot pool of
/// <see cref="CapturePump{TSlot,TBlock}"/>, which processes them away from the
/// capture callback. The pool is sized once at construction: a WASAPI/MME
/// packet size is known before the device starts.
/// </summary>
internal sealed class PcmCapturePump : CapturePump<PcmCapturePump.Slot, PcmCaptureBlock>
{
    private const int SlotCount = 16;

    public PcmCapturePump(
        int maximumPacketBytes,
        Action<PcmCaptureBlock> processBlock,
        Action<int, Exception> reportFailure)
        : base(
            SlotCount,
            "PCM",
            "PCM capture processing could not keep up with the device; input packets were not recorded.",
            processBlock,
            reportFailure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPacketBytes);
        lock (Sync)
        {
            AllocateSlots(() => new Slot(maximumPacketBytes));
        }

        StartWorker();
    }

    public bool TryEnqueue(AudioCapturePacket packet)
    {
        if (packet.BytesRecorded < 0 || packet.BytesRecorded > packet.Buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(packet.BytesRecorded));
        }

        lock (Sync)
        {
            if (IsStoppedOrFailed)
            {
                return false;
            }

            int frameCount = packet.BytesRecorded / packet.Format.BlockAlign;
            if (!TryTakeSlot(frameCount, out int slotIndex, out Slot slot))
            {
                return false;
            }

            if (packet.BytesRecorded > slot.Buffer.Length)
            {
                ReturnSlot(slotIndex);
                throw new InvalidOperationException(
                    $"PCM packet size {packet.BytesRecorded} exceeds the prepared capacity {slot.Buffer.Length}.");
            }

            packet.Buffer.Span[..packet.BytesRecorded].CopyTo(slot.Buffer);
            slot.BytesRecorded = packet.BytesRecorded;
            slot.Discontinuity = packet.Discontinuity;
            slot.Silent = packet.Silent;
            slot.TimestampError = packet.TimestampError;
            PublishSlot(slotIndex, frameCount);
            return true;
        }
    }

    protected override PcmCaptureBlock CreateBlock(Slot slot) => new(
        slot.Buffer,
        slot.BytesRecorded,
        slot.Generation,
        slot.Discontinuity,
        slot.Silent,
        slot.TimestampError);

    internal sealed class Slot(int maximumPacketBytes) : ICapturePumpSlot
    {
        public byte[] Buffer { get; } = new byte[maximumPacketBytes];
        public int BytesRecorded { get; set; }
        public int Generation { get; set; }
        public bool Discontinuity { get; set; }
        public bool Silent { get; set; }
        public bool TimestampError { get; set; }
    }
}

internal readonly record struct PcmCaptureBlock(
    byte[] Buffer,
    int BytesRecorded,
    int Generation,
    bool Discontinuity,
    bool Silent,
    bool TimestampError);
