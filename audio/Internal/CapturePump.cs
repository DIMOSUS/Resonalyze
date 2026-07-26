namespace Resonalyze.Audio;

/// <summary>Which capture generation a slot's payload belongs to, so a block
/// still in flight when the session moved on can be dropped.</summary>
internal interface ICapturePumpSlot
{
    int Generation { get; set; }
}

/// <summary>
/// The shared machine behind <see cref="PcmCapturePump"/> and
/// <see cref="AsioCapturePump"/>: a bounded slot pool, a worker that processes
/// slots off the device callback, and the generation bookkeeping that lets a
/// session reject a packet accepted before the last reset. Reset, completion and
/// disposal drop queued blocks; completion preserves a terminal failure.
///
/// Filling a slot stays in the derived pump: the callback must not allocate, and
/// only the derived pump knows the payload shape. Its <c>TryEnqueue</c> takes
/// <see cref="Sync"/>, validates, then drives TryTakeSlot → copy → PublishSlot.
/// Every "<see cref="Sync"/> held" member below is part of that sequence.
/// </summary>
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
    private int acceptedFrames;
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

        // Created, NOT started: a derived constructor still validates its own
        // arguments and lays out its slots, and anything it throws leaves the
        // object unreachable — a worker running by then could never be disposed.
        // Every derived constructor ends with StartWorker().
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = $"Resonalyze {backendName} capture"
        };
    }

    /// <summary>Last statement of a derived constructor — see the base one.</summary>
    protected void StartWorker()
    {
        // Both under the lock so a concurrent Dispose cannot see the flag and the
        // thread state out of step; Run() blocks on Sync until this returns.
        lock (Sync)
        {
            worker.Start();
            workerStarted = true;
        }
    }

    /// <summary>Test seam: pins that the base constructor leaves the worker unstarted.</summary>
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

    /// <summary>The lock guarding every field; a derived enqueue holds it.</summary>
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

    public int AcceptedFrames
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

    /// <summary>Waits out every block accepted before the callback source stopped.
    /// Callers must prevent new enqueues before entering.</summary>
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

        // Join on a never-started thread throws, so a pump abandoned by a failed
        // derived constructor still disposes cleanly.
        if (started && Thread.CurrentThread != worker)
        {
            worker.Join();
        }
    }

    /// <summary>The payload handed to the consumer, built on the worker thread.</summary>
    protected abstract TBlock CreateBlock(TSlot slot);

    /// <summary>Stopped or terminally failed: a derived enqueue returns false
    /// without touching the pool. Sync held.</summary>
    protected bool IsStoppedOrFailed => stopping || failed;

    /// <summary>A block is still queued. Sync held.</summary>
    protected bool HasPendingSlots => pendingSlots.Count > 0;

    /// <summary>Blocks until no slot is in flight, so the pool can be
    /// reallocated underneath the worker. Sync held.</summary>
    protected void WaitForIdle()
    {
        while (inFlightCount > 0)
        {
            Monitor.Wait(Sync);
        }
    }

    /// <summary>(Re)allocates the pool. Sync held, worker idle — see WaitForIdle.</summary>
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

    /// <summary>The pool has been allocated. Sync held.</summary>
    protected bool HasSlots => slots.Length > 0;

    /// <summary>Takes a free slot to fill. False — after arming the terminal
    /// overflow failure — when the pool is exhausted, i.e. processing fell behind
    /// the device. Sync held.</summary>
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

        // Before the pop: an overflowing frame count must not leave a slot taken
        // out of the pool and never returned. PublishSlot can then add blindly.
        _ = checked(acceptedFrames + frameCount);

        slotIndex = freeSlots.Pop();
        slot = slots[slotIndex];
        return true;
    }

    /// <summary>Returns a taken slot unused, for a derived validation that
    /// rejects the packet after taking it. Sync held.</summary>
    protected void ReturnSlot(int slotIndex)
    {
        freeSlots.Push(slotIndex);
    }

    /// <summary>Queues a filled slot: stamps the generation, counts the frames
    /// (already checked by TryTakeSlot) and wakes the worker. Sync held.</summary>
    protected void PublishSlot(int slotIndex, int frameCount)
    {
        acceptedFrames = checked(acceptedFrames + frameCount);
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
