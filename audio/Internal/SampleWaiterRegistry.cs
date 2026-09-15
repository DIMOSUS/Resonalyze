namespace Resonalyze.Audio;

/// <summary>No locking of its own: call under the owner's capture lock, so a waiter cannot miss its threshold.</summary>
internal sealed class SampleWaiterRegistry
{
    private readonly List<SampleWaiter> waiters = new();

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

    /// <summary>The stop event completes only the first-buffer and stopped signals, so pending sample waiters must be faulted.</summary>
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
