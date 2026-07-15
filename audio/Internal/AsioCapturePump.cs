using System.Runtime.InteropServices;
using NAudio.Wave.Asio;

namespace Resonalyze.Audio;

/// <summary>
/// Moves ASIO input processing off the driver's buffer-switch callback. Slots and
/// channel buffers are prepared before playback starts; the callback only copies
/// into the fixed pool. Reset and disposal drop queued packets; reset also advances
/// the capture epoch.
/// </summary>
internal sealed class AsioCapturePump : IDisposable
{
    private const int SlotCount = 8;

    private readonly object sync = new();
    private readonly int channelCount;
    private readonly Action<AsioCaptureBlock> processBlock;
    private readonly Action<int, Exception> reportFailure;
    private readonly InvalidOperationException overflowException = new(
        "ASIO capture processing could not keep up with the driver; input buffers were not recorded.");
    private readonly Thread worker;
    private readonly Queue<int> pendingSlots = new(SlotCount);
    private readonly Stack<int> freeSlots = new(SlotCount);
    private Slot[] slots = Array.Empty<Slot>();
    private int preparedByteCapacity;
    private int generation;
    private int failureGeneration;
    private int inFlightCount;
    private bool failurePending;
    private bool stopping;
    private bool failed;

    public AsioCapturePump(
        int channelCount,
        Action<AsioCaptureBlock> processBlock,
        Action<int, Exception> reportFailure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        this.channelCount = channelCount;
        this.processBlock = processBlock ?? throw new ArgumentNullException(nameof(processBlock));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "Resonalyze ASIO capture"
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

    /// <summary>Allocates the complete callback buffer pool before the driver starts.</summary>
    public void Prepare(int maximumByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumByteCount);
        lock (sync)
        {
            while (inFlightCount > 0)
            {
                Monitor.Wait(sync);
            }
            if (pendingSlots.Count != 0)
            {
                throw new InvalidOperationException("Reset the ASIO capture pump before preparing it.");
            }
            if (preparedByteCapacity >= maximumByteCount)
            {
                return;
            }

            slots = new Slot[SlotCount];
            freeSlots.Clear();
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                slots[slotIndex] = new Slot(channelCount, maximumByteCount);
                freeSlots.Push(slotIndex);
            }
            preparedByteCapacity = maximumByteCount;
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

    public bool TryEnqueue(
        IntPtr[] inputBuffers,
        int inputChannelOffset,
        AsioSampleType sampleType,
        int frameCount)
    {
        int bytesPerSample = AsioSampleConverter.BytesPerSample(sampleType);
        int byteCount = checked(frameCount * bytesPerSample);

        lock (sync)
        {
            if (stopping || failed)
            {
                return false;
            }
            if (slots.Length == 0)
            {
                throw new InvalidOperationException("ASIO capture buffers were not prepared before playback.");
            }
            if (byteCount > preparedByteCapacity)
            {
                throw new InvalidOperationException(
                    $"ASIO packet size {byteCount} exceeds the prepared capacity {preparedByteCapacity}.");
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
            slot.Generation = generation;
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
                    inFlightCount++;
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
                processBlock(new AsioCaptureBlock(
                    slot.Channels,
                    slot.SampleType,
                    slot.FrameCount,
                    slot.Generation));
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
                    inFlightCount--;
                    freeSlots.Push(slotIndex);
                    Monitor.PulseAll(sync);
                }
            }
        }
    }

    private sealed class Slot
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
