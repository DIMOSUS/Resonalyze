using System.Runtime.InteropServices;
using NAudio.Wave.Asio;

namespace Resonalyze.Audio;

/// <summary>
/// Moves ASIO input processing off the driver's buffer-switch callback. The
/// callback only copies channel buffers into a bounded set of reusable slots;
/// conversion, accumulation and event publication run on this worker thread.
/// A full queue is reported as a terminal capture failure instead of silently
/// dropping samples and producing a plausible but invalid measurement.
/// </summary>
internal sealed class AsioCapturePump : IDisposable
{
    private const int SlotCount = 8;

    private readonly object sync = new();
    private readonly int channelCount;
    private readonly Action<AsioCaptureBlock> processBlock;
    private readonly Action<Exception> reportFailure;
    private readonly Thread worker;
    private Slot[] slots = Array.Empty<Slot>();
    private int readIndex;
    private int writeIndex;
    private int queuedCount;
    private bool stopping;
    private bool failed;

    public AsioCapturePump(
        int channelCount,
        Action<AsioCaptureBlock> processBlock,
        Action<Exception> reportFailure)
    {
        if (channelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

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

            EnsureSlots(byteCount);
            if (queuedCount == slots.Length)
            {
                failed = true;
                ThreadPool.QueueUserWorkItem(_ => reportFailure(
                    new InvalidOperationException(
                        "ASIO capture processing could not keep up with the driver; input buffers were not recorded.")));
                return false;
            }

            Slot slot = slots[writeIndex];
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
            writeIndex = (writeIndex + 1) % slots.Length;
            queuedCount++;
            Monitor.Pulse(sync);
            return true;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            stopping = true;
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
            Slot slot;
            lock (sync)
            {
                while (queuedCount == 0 && !stopping)
                {
                    Monitor.Wait(sync);
                }

                if (queuedCount == 0 && stopping)
                {
                    return;
                }

                slot = slots[readIndex];
            }

            try
            {
                processBlock(new AsioCaptureBlock(
                    slot.Channels,
                    slot.SampleType,
                    slot.FrameCount));
            }
            catch (Exception exception)
            {
                lock (sync)
                {
                    failed = true;
                }
                reportFailure(exception);
            }

            lock (sync)
            {
                readIndex = (readIndex + 1) % slots.Length;
                queuedCount--;
            }
        }
    }

    private void EnsureSlots(int byteCount)
    {
        if (slots.Length != 0 && slots[0].Channels[0].Length >= byteCount)
        {
            return;
        }
        if (queuedCount != 0)
        {
            throw new InvalidOperationException("The ASIO driver changed its buffer size while capture was active.");
        }

        slots = new Slot[SlotCount];
        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            var channels = new byte[channelCount][];
            for (int channel = 0; channel < channelCount; channel++)
            {
                channels[channel] = new byte[byteCount];
            }
            slots[slotIndex] = new Slot(channels);
        }
        readIndex = 0;
        writeIndex = 0;
    }

    private sealed class Slot(byte[][] channels)
    {
        public byte[][] Channels { get; } = channels;
        public AsioSampleType SampleType { get; set; }
        public int FrameCount { get; set; }
    }
}

internal readonly record struct AsioCaptureBlock(
    byte[][] Channels,
    AsioSampleType SampleType,
    int FrameCount);
