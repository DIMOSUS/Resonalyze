namespace Resonalyze;

/// <summary>
/// The sample-count waiter list shared by the capture classes (SoundRecorder,
/// AsioFullDuplexSession): callers await "N samples recorded" and the audio
/// callback completes the due waiters as the accumulator advances.
///
/// The registry does no locking of its own — every member must be called under
/// the owner's capture lock, the same lock that guards the accumulator, so a
/// waiter can never be added after its threshold silently passed. Completing
/// under the lock is safe because the signals run their continuations
/// asynchronously.
/// </summary>
internal sealed class SampleWaiterRegistry
{
    private readonly List<SampleWaiter> waiters = new();

    /// <summary>
    /// A capture signal whose awaiters never run inline on the audio thread.
    /// </summary>
    public static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Add(int sampleCount, CancellationToken cancellationToken)
    {
        var waiter = new SampleWaiter(sampleCount, cancellationToken);
        waiters.Add(waiter);
        return waiter.Task;
    }

    public void CompleteUpTo(int readSamples)
    {
        for (int i = waiters.Count - 1; i >= 0; i--)
        {
            if (readSamples >= waiters[i].SampleCount)
            {
                waiters[i].Complete();
                waiters.RemoveAt(i);
            }
        }
    }

    public void CancelAll()
    {
        foreach (SampleWaiter waiter in waiters)
        {
            waiter.Cancel();
        }

        waiters.Clear();
    }

    /// <summary>
    /// Faults every pending waiter. Called when the capture stops
    /// unexpectedly (device unplugged, driver error): a waiter blocked on a
    /// sample count that will never arrive used to hang forever — the stop
    /// event completed only the first-buffer and stopped signals.
    /// </summary>
    public void FaultAll(Exception exception)
    {
        foreach (SampleWaiter waiter in waiters)
        {
            waiter.Fault(exception);
        }

        waiters.Clear();
    }

    private sealed class SampleWaiter
    {
        private readonly TaskCompletionSource<bool> completion = NewSignal();
        private readonly CancellationTokenRegistration registration;

        public SampleWaiter(int sampleCount, CancellationToken cancellationToken)
        {
            SampleCount = sampleCount;
            registration = cancellationToken.Register(() =>
                completion.TrySetCanceled(cancellationToken));
        }

        public int SampleCount { get; }
        public Task Task => completion.Task;

        public void Complete()
        {
            registration.Dispose();
            completion.TrySetResult(true);
        }

        public void Cancel()
        {
            registration.Dispose();
            completion.TrySetCanceled();
        }

        public void Fault(Exception exception)
        {
            registration.Dispose();
            completion.TrySetException(exception);
        }
    }
}
