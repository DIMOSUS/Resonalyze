namespace Resonalyze.Audio;

/// <summary>
/// Copies device-owned PCM packets into a bounded reusable queue and processes
/// them away from the capture callback. Generation tags prevent packets queued
/// before a reset from leaking into the next averaged measurement run.
/// </summary>
internal sealed class PcmCapturePump : IDisposable
{
    private const int SlotCount = 16;

    private readonly object sync = new();
    private readonly Action<PcmCaptureBlock> processBlock;
    private readonly Action<Exception> reportFailure;
    private readonly Thread worker;
    private Slot[] slots = Array.Empty<Slot>();
    private int readIndex;
    private int writeIndex;
    private int queuedCount;
    private int generation;
    private bool stopping;
    private bool failed;

    public PcmCapturePump(
        Action<PcmCaptureBlock> processBlock,
        Action<Exception> reportFailure)
    {
        this.processBlock = processBlock ?? throw new ArgumentNullException(nameof(processBlock));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "Resonalyze PCM capture"
        };
        worker.Start();
    }

    public void Reset(int newGeneration)
    {
        lock (sync)
        {
            failed = false;
            generation = newGeneration;
        }
    }

    public bool TryEnqueue(AudioCaptureDataEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.BytesRecorded < 0 || args.BytesRecorded > args.Buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(args.BytesRecorded));
        }

        Exception? overflow = null;
        lock (sync)
        {
            if (stopping || failed)
            {
                return false;
            }

            EnsureSlots(args.BytesRecorded);
            if (queuedCount == slots.Length)
            {
                failed = true;
                overflow = new InvalidOperationException(
                    "PCM capture processing could not keep up with the device; input packets were not recorded.");
            }
            else
            {
                Slot slot = slots[writeIndex];
                args.Buffer.Span[..args.BytesRecorded].CopyTo(slot.Buffer);
                slot.BytesRecorded = args.BytesRecorded;
                slot.Generation = generation;
                slot.Discontinuity = args.Discontinuity;
                slot.Silent = args.Silent;
                slot.TimestampError = args.TimestampError;
                writeIndex = (writeIndex + 1) % slots.Length;
                queuedCount++;
                Monitor.Pulse(sync);
            }
        }

        if (overflow != null)
        {
            reportFailure(overflow);
            return false;
        }
        return true;
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
        if (slots.Length != 0 && slots[0].Buffer.Length >= byteCount)
        {
            return;
        }
        if (queuedCount != 0)
        {
            throw new InvalidOperationException(
                "The capture device changed its packet size while packets were queued.");
        }

        slots = new Slot[SlotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = new Slot(new byte[byteCount]);
        }
        readIndex = 0;
        writeIndex = 0;
    }

    private sealed class Slot(byte[] buffer)
    {
        public byte[] Buffer { get; } = buffer;
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
