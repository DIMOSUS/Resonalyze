namespace Resonalyze.Audio;

/// <summary>
/// Copies device-owned PCM packets into a bounded preallocated queue and processes
/// them away from the capture callback. Reset and disposal drop queued packets;
/// generation tags let the session reject an older packet already in flight.
/// </summary>
internal sealed class PcmCapturePump : IDisposable
{
    private const int SlotCount = 16;

    private readonly object sync = new();
    private readonly Action<PcmCaptureBlock> processBlock;
    private readonly Action<int, Exception> reportFailure;
    private readonly InvalidOperationException overflowException = new(
        "PCM capture processing could not keep up with the device; input packets were not recorded.");
    private readonly Thread worker;
    private readonly Slot[] slots;
    private readonly Queue<int> pendingSlots = new(SlotCount);
    private readonly Stack<int> freeSlots = new(SlotCount);
    private int generation;
    private int failureGeneration;
    private bool failurePending;
    private bool stopping;
    private bool failed;

    public PcmCapturePump(
        int maximumPacketBytes,
        Action<PcmCaptureBlock> processBlock,
        Action<int, Exception> reportFailure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPacketBytes);
        this.processBlock = processBlock ?? throw new ArgumentNullException(nameof(processBlock));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));

        slots = new Slot[SlotCount];
        for (int index = 0; index < slots.Length; index++)
        {
            slots[index] = new Slot(maximumPacketBytes);
            freeSlots.Push(index);
        }

        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "Resonalyze PCM capture"
        };
        worker.Start();
    }

    internal bool IsStopping
    {
        get
        {
            lock (sync)
            {
                return stopping;
            }
        }
    }

    public void Reset(int newGeneration)
    {
        lock (sync)
        {
            generation = newGeneration;
            failed = false;
            failurePending = false;
            while (pendingSlots.Count > 0)
            {
                freeSlots.Push(pendingSlots.Dequeue());
            }
            Monitor.PulseAll(sync);
        }
    }

    public bool TryEnqueue(AudioCapturePacket packet)
    {
        if (packet.BytesRecorded < 0 || packet.BytesRecorded > packet.Buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(packet.BytesRecorded));
        }

        lock (sync)
        {
            if (stopping || failed)
            {
                return false;
            }
            if (freeSlots.Count == 0)
            {
                failed = true;
                failurePending = true;
                failureGeneration = generation;
                Monitor.Pulse(sync);
                return false;
            }

            int slotIndex = freeSlots.Pop();
            Slot slot = slots[slotIndex];
            if (packet.BytesRecorded > slot.Buffer.Length)
            {
                freeSlots.Push(slotIndex);
                throw new InvalidOperationException(
                    $"PCM packet size {packet.BytesRecorded} exceeds the prepared capacity {slot.Buffer.Length}.");
            }

            packet.Buffer.Span[..packet.BytesRecorded].CopyTo(slot.Buffer);
            slot.BytesRecorded = packet.BytesRecorded;
            slot.Generation = generation;
            slot.Discontinuity = packet.Discontinuity;
            slot.Silent = packet.Silent;
            slot.TimestampError = packet.TimestampError;
            pendingSlots.Enqueue(slotIndex);
            Monitor.Pulse(sync);
            return true;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            stopping = true;
            failurePending = false;
            while (pendingSlots.Count > 0)
            {
                freeSlots.Push(pendingSlots.Dequeue());
            }
            Monitor.PulseAll(sync);
        }

        if (Thread.CurrentThread != worker)
        {
            worker.Join();
        }
    }

    private void Run()
    {
        while (true)
        {
            int slotIndex = -1;
            int blockGeneration = 0;
            Exception? failure = null;
            lock (sync)
            {
                while (pendingSlots.Count == 0 && !failurePending && !stopping)
                {
                    Monitor.Wait(sync);
                }

                if (failurePending)
                {
                    failurePending = false;
                    failure = overflowException;
                    blockGeneration = failureGeneration;
                }
                else if (pendingSlots.Count > 0)
                {
                    slotIndex = pendingSlots.Dequeue();
                }
                else if (stopping)
                {
                    return;
                }
            }

            if (failure != null)
            {
                reportFailure(blockGeneration, failure);
                continue;
            }

            try
            {
                Slot slot = slots[slotIndex];
                blockGeneration = slot.Generation;
                processBlock(new PcmCaptureBlock(
                    slot.Buffer,
                    slot.BytesRecorded,
                    slot.Generation,
                    slot.Discontinuity,
                    slot.Silent,
                    slot.TimestampError));
            }
            catch (Exception exception)
            {
                lock (sync)
                {
                    failed = true;
                }
                reportFailure(blockGeneration, exception);
            }
            finally
            {
                lock (sync)
                {
                    freeSlots.Push(slotIndex);
                    Monitor.PulseAll(sync);
                }
            }
        }
    }

    private sealed class Slot(int maximumPacketBytes)
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
