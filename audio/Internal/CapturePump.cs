namespace Resonalyze.Audio;

/// <summary>Capture generation of a slot's payload, so a block in flight across a reset is dropped.</summary>
internal interface ICapturePumpSlot
{
    int Generation { get; set; }
}

/// <summary>Shared slot pool + worker + generation bookkeeping for the PCM and ASIO pumps. A derived <c>TryEnqueue</c> holds <see cref="Sync"/> and drives TryTakeSlot → copy → PublishSlot (no allocation in the callback).</summary>
internal abstract class CapturePump<TSlot, TBlock> : IDisposable
    where TSlot : class, ICapturePumpSlot
{
    private readonly Action<TBlock> processBlock;
    private readonly Action<int, Exception> reportFailure;
    private readonly InvalidOperationException overflowException;
    private readonly string backendName;
    private readonly int slotCount;
    private readonly Thread worker;
    private readonly Queue<int> pendingSlots;
    private readonly Stack<int> freeSlots;
    private TSlot[] slots = Array.Empty<TSlot>();
    private int generation;
    private int failureGeneration;
    private Exception? failureException;
    // Long: an int runs out after 3.1 hours of Live at 192 kHz.
    private long acceptedFrames;
    private int inFlightCount;
    private bool failurePending;
    private bool stopping;
    private bool failed;
    private bool workerStarted;

    protected CapturePump(
        int slotCount,
        string backendName,
        string overflowMessage,
        Action<TBlock> processBlock,
        Action<int, Exception> reportFailure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slotCount);
        this.slotCount = slotCount;
        this.backendName = backendName;
        this.processBlock = processBlock ?? throw new ArgumentNullException(nameof(processBlock));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        overflowException = new InvalidOperationException(overflowMessage);
        pendingSlots = new Queue<int>(slotCount);
        freeSlots = new Stack<int>(slotCount);

        // Not started here: a throwing derived constructor would leave an undisposable running worker. Derived constructors end with StartWorker().
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = $"Resonalyze {backendName} capture"
        };
    }

    protected void StartWorker()
    {
        // Flag and thread start under one lock so a concurrent Dispose sees them in step.
        lock (Sync)
        {
            worker.Start();
            workerStarted = true;
        }
    }

    internal bool WorkerStarted
    {
        get
        {
            lock (Sync)
            {
                return workerStarted;
            }
        }
    }

    protected object Sync { get; } = new();

    internal bool IsStopping
    {
        get
        {
            lock (Sync)
            {
                return stopping;
            }
        }
    }

    public long AcceptedFrames
    {
        get
        {
            lock (Sync)
            {
                return acceptedFrames;
            }
        }
    }

    public void Reset(int newGeneration)
    {
        lock (Sync)
        {
            ResetCore(newGeneration);
        }
    }

    public Exception? CompleteGeneration(int completedGeneration, int newGeneration)
    {
        lock (Sync)
        {
            if (generation != completedGeneration)
            {
                throw new InvalidOperationException(
                    $"Cannot complete {backendName} capture generation {completedGeneration}; " +
                    $"current generation is {generation}.");
            }
            Exception? failure = failed && failureGeneration == completedGeneration
                ? failureException
                : null;
            ResetCore(newGeneration);
            return failure;
        }
    }

    /// <summary>Waits out every accepted block; callers must stop new enqueues first.</summary>
    public void Drain()
    {
        if (Thread.CurrentThread == worker)
        {
            throw new InvalidOperationException(
                $"The {backendName} capture worker cannot drain itself.");
        }

        lock (Sync)
        {
            while (pendingSlots.Count > 0 || inFlightCount > 0)
            {
                Monitor.Wait(Sync);
            }
        }
    }

    public void Dispose()
    {
        bool started;
        lock (Sync)
        {
            started = workerStarted;
            stopping = true;
            failurePending = false;
            while (pendingSlots.Count > 0)
            {
                freeSlots.Push(pendingSlots.Dequeue());
            }
            Monitor.PulseAll(Sync);
        }

        // Join on a never-started thread throws.
        if (started && Thread.CurrentThread != worker)
        {
            worker.Join();
        }
    }

    protected abstract TBlock CreateBlock(TSlot slot);

    /// <summary>Sync held.</summary>
    protected bool IsStoppedOrFailed => stopping || failed;

    protected bool HasPendingSlots => pendingSlots.Count > 0;

    /// <summary>Blocks until no slot is in flight, so the pool can be reallocated. Sync held.</summary>
    protected void WaitForIdle()
    {
        while (inFlightCount > 0)
        {
            Monitor.Wait(Sync);
        }
    }

    protected void AllocateSlots(Func<TSlot> createSlot)
    {
        slots = new TSlot[slotCount];
        freeSlots.Clear();
        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            slots[slotIndex] = createSlot();
            freeSlots.Push(slotIndex);
        }
    }

    protected bool HasSlots => slots.Length > 0;

    /// <summary>False, after arming the terminal overflow failure, when processing fell behind the device. Sync held.</summary>
    protected bool TryTakeSlot(int frameCount, out int slotIndex, out TSlot slot)
    {
        if (freeSlots.Count == 0)
        {
            failed = true;
            failurePending = true;
            failureGeneration = generation;
            failureException = overflowException;
            Monitor.Pulse(Sync);
            slotIndex = -1;
            slot = null!;
            return false;
        }

        slotIndex = freeSlots.Pop();
        slot = slots[slotIndex];
        return true;
    }

    protected void ReturnSlot(int slotIndex)
    {
        freeSlots.Push(slotIndex);
    }

    protected void PublishSlot(int slotIndex, int frameCount)
    {
        acceptedFrames += frameCount;
        slots[slotIndex].Generation = generation;
        pendingSlots.Enqueue(slotIndex);
        Monitor.Pulse(Sync);
    }

    private void Run()
    {
        while (true)
        {
            int slotIndex = -1;
            int blockGeneration = 0;
            Exception? failure = null;
            lock (Sync)
            {
                while (pendingSlots.Count == 0 && !failurePending && !stopping)
                {
                    Monitor.Wait(Sync);
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
                TSlot slot = slots[slotIndex];
                blockGeneration = slot.Generation;
                processBlock(CreateBlock(slot));
            }
            catch (Exception exception)
            {
                bool report;
                lock (Sync)
                {
                    report = blockGeneration == generation;
                    if (report)
                    {
                        failed = true;
                        failureGeneration = blockGeneration;
                        failureException = exception;
                    }
                }
                if (report)
                {
                    reportFailure(blockGeneration, exception);
                }
            }
            finally
            {
                lock (Sync)
                {
                    inFlightCount--;
                    freeSlots.Push(slotIndex);
                    Monitor.PulseAll(Sync);
                }
            }
        }
    }

    private void ResetCore(int newGeneration)
    {
        generation = newGeneration;
        failed = false;
        failurePending = false;
        failureException = null;
        acceptedFrames = 0;
        while (pendingSlots.Count > 0)
        {
            freeSlots.Push(pendingSlots.Dequeue());
        }
        Monitor.PulseAll(Sync);
    }
}
