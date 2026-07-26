namespace Resonalyze.Audio;

/// <summary>
/// The per-slot state every capture pump needs: which capture generation the
/// slot's payload belongs to, so a block still in flight when the session moved
/// on can be recognised and dropped.
/// </summary>
internal interface ICapturePumpSlot
{
    int Generation { get; set; }
}

/// <summary>
/// The shared machine behind <see cref="PcmCapturePump"/> and
/// <see cref="AsioCapturePump"/>: a bounded pool of preallocated slots, a
/// background worker that processes them off the device callback, and the
/// generation bookkeeping that lets a session reject a packet accepted before
/// the last reset.
///
/// The device callback must not allocate, so filling a slot stays in the
/// derived pump — it is the only part that knows the payload shape (one
/// interleaved PCM packet vs one byte buffer per ASIO channel). A derived
/// <c>TryEnqueue</c> takes <see cref="Sync"/>, runs its own validation, then
/// drives <see cref="TryTakeSlot"/> → copy → <see cref="PublishSlot"/>; the
/// bookkeeping in between belongs here.
///
/// Reset, completion and disposal all drop queued blocks; completion preserves
/// a terminal generation failure for the caller to observe.
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

        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = $"Resonalyze {backendName} capture"
        };
        worker.Start();
    }

    /// <summary>The lock guarding every field below; a derived enqueue holds it.</summary>
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

    /// <summary>
    /// Waits until every block accepted before the callback source stopped has
    /// finished processing. Callers must prevent new enqueues before entering.
    /// </summary>
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
        lock (Sync)
        {
            stopping = true;
            failurePending = false;
            while (pendingSlots.Count > 0)
            {
                freeSlots.Push(pendingSlots.Dequeue());
            }
            Monitor.PulseAll(Sync);
        }

        if (Thread.CurrentThread != worker)
        {
            worker.Join();
        }
    }

    /// <summary>The payload handed to the consumer. Called off the worker thread.</summary>
    protected abstract TBlock CreateBlock(TSlot slot);

    /// <summary>
    /// True once the pump has stopped or hit a terminal failure — a derived
    /// enqueue returns false without touching the pool. <see cref="Sync"/> held.
    /// </summary>
    protected bool IsStoppedOrFailed => stopping || failed;

    /// <summary>True while a block is still queued. <see cref="Sync"/> held.</summary>
    protected bool HasPendingSlots => pendingSlots.Count > 0;

    /// <summary>
    /// Blocks until no slot is being processed, so the pool can be reallocated
    /// underneath the worker. <see cref="Sync"/> held.
    /// </summary>
    protected void WaitForIdle()
    {
        while (inFlightCount > 0)
        {
            Monitor.Wait(Sync);
        }
    }

    /// <summary>
    /// (Re)allocates the whole slot pool. <see cref="Sync"/> held, and the
    /// worker must be idle — see <see cref="WaitForIdle"/>.
    /// </summary>
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

    /// <summary>True once <see cref="AllocateSlots"/> has run. <see cref="Sync"/> held.</summary>
    protected bool HasSlots => slots.Length > 0;

    /// <summary>
    /// Takes a free slot for the caller to fill. Returns false — after arming
    /// the terminal overflow failure — when the pool is exhausted, which means
    /// processing could not keep up with the device. <see cref="Sync"/> held.
    /// </summary>
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

        // Check the running total here, before the pop: an overflowing frame
        // count must not leave a slot taken out of the pool and never returned.
        // PublishSlot can then add unconditionally.
        _ = checked(acceptedFrames + frameCount);

        slotIndex = freeSlots.Pop();
        slot = slots[slotIndex];
        return true;
    }

    /// <summary>
    /// Hands a taken slot back unused, for a derived validation that rejects the
    /// packet after taking it. <see cref="Sync"/> held.
    /// </summary>
    protected void ReturnSlot(int slotIndex)
    {
        freeSlots.Push(slotIndex);
    }

    /// <summary>
    /// Queues a filled slot: stamps it with the current generation, counts its
    /// frames and wakes the worker. The frame count is the one already checked
    /// by <see cref="TryTakeSlot"/>. <see cref="Sync"/> held.
    /// </summary>
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
