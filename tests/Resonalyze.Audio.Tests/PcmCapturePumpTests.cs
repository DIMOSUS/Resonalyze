using NAudio.Wave;

namespace Resonalyze.Audio.Tests;

public sealed class PcmCapturePumpTests
{
    [Fact]
    public async Task TryEnqueue_CopiesDeviceBuffer_AndProcessesOnWorker()
    {
        int callbackThread = Environment.CurrentManagedThreadId;
        var completed = new TaskCompletionSource<(byte[] Bytes, int ThreadId)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new PcmCapturePump(
            4,
            block => completed.TrySetResult((
                block.Buffer[..block.BytesRecorded],
                Environment.CurrentManagedThreadId)),
            (_, exception) => completed.TrySetException(exception));
        pump.Reset(1);
        byte[] source = [1, 2, 3, 4];

        Assert.True(pump.TryEnqueue(CreatePacket(source)));
        Array.Clear(source);

        (byte[] bytes, int workerThread) = await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal([1, 2, 3, 4], bytes);
        Assert.NotEqual(callbackThread, workerThread);
    }

    [Fact]
    public async Task Overflow_Reset_AllowsNextGenerationToProcess()
    {
        using var workerStarted = new ManualResetEventSlim();
        using var releaseWorker = new ManualResetEventSlim();
        var failure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new PcmCapturePump(
            2,
            block =>
            {
                if (block.Generation == 1)
                {
                    workerStarted.Set();
                    releaseWorker.Wait();
                }
                else
                {
                    completed.TrySetResult();
                }
            },
            (_, exception) => failure.TrySetResult(exception));
        pump.Reset(1);
        AudioCapturePacket packet = CreatePacket([0, 0]);

        try
        {
            Assert.True(pump.TryEnqueue(packet));
            Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(2)));
            int accepted = 1;
            while (pump.TryEnqueue(packet))
            {
                accepted++;
            }

            releaseWorker.Set();
            Exception exception = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(16, accepted);
            Assert.Contains("could not keep up", exception.Message);

            pump.Reset(2);
            Assert.True(pump.TryEnqueue(packet));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            releaseWorker.Set();
        }
    }

    [Fact]
    public async Task WorkerException_Reset_AllowsNextGenerationToProcess()
    {
        var failure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new PcmCapturePump(
            2,
            block =>
            {
                if (block.Generation == 1)
                {
                    throw new IOException("worker failed");
                }
                completed.TrySetResult();
            },
            (_, exception) => failure.TrySetResult(exception));
        AudioCapturePacket packet = CreatePacket([0, 0]);
        pump.Reset(1);

        Assert.True(pump.TryEnqueue(packet));
        Exception error = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("worker failed", error.Message);

        pump.Reset(2);
        Assert.True(pump.TryEnqueue(packet));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Reset_WhileOldWorkerLaterFails_DoesNotPoisonNewGeneration()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpectedFailure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new PcmCapturePump(
            2,
            block =>
            {
                if (block.Generation == 1)
                {
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(2));
                    throw new IOException("stale worker failed");
                }
                completed.TrySetResult();
            },
            (_, exception) => unexpectedFailure.TrySetResult(exception));
        AudioCapturePacket packet = CreatePacket([0, 0]);
        pump.Reset(1);
        Assert.True(pump.TryEnqueue(packet));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));

        pump.Reset(2);
        releaseFirst.Set();
        Assert.True(pump.TryEnqueue(packet));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(unexpectedFailure.Task.IsCompleted);
    }

    [Fact]
    public async Task TryEnqueue_AllowsDifferentPacketSizesWhileEarlierPacketIsQueued()
    {
        using var releaseFirst = new ManualResetEventSlim();
        var results = new List<byte[]>();
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new PcmCapturePump(
            8,
            block =>
            {
                lock (results)
                {
                    results.Add(block.Buffer[..block.BytesRecorded]);
                    if (results.Count == 1)
                    {
                        releaseFirst.Wait(TimeSpan.FromSeconds(2));
                    }
                    if (results.Count == 2)
                    {
                        completed.TrySetResult();
                    }
                }
            },
            (_, exception) => completed.TrySetException(exception));
        pump.Reset(1);

        Assert.True(pump.TryEnqueue(CreatePacket([1, 2])));
        Assert.True(pump.TryEnqueue(CreatePacket([3, 4, 5, 6, 7, 8])));
        releaseFirst.Set();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([1, 2], results[0]);
        Assert.Equal([3, 4, 5, 6, 7, 8], results[1]);
    }

    [Fact]
    public async Task Reset_DrainsQueuedOldPackets_AndRetagsNewPackets()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var results = new List<(int Generation, byte Value)>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new PcmCapturePump(
            2,
            block =>
            {
                if (block.Buffer[0] == 1)
                {
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(2));
                }
                lock (results)
                {
                    results.Add((block.Generation, block.Buffer[0]));
                }
                if (block.Buffer[0] == 9)
                {
                    completed.TrySetResult();
                }
            },
            (_, exception) => completed.TrySetException(exception));
        pump.Reset(1);

        Assert.True(pump.TryEnqueue(CreatePacket([1, 0])));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(pump.TryEnqueue(CreatePacket([2, 0])));
        Assert.True(pump.TryEnqueue(CreatePacket([3, 0])));

        pump.Reset(2);
        Assert.True(pump.TryEnqueue(CreatePacket([9, 0])));
        releaseFirst.Set();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([(1, (byte)1), (2, (byte)9)], results);
    }

    [Fact]
    public async Task TryEnqueue_AfterWarmup_DoesNotAllocateOnCallingThread()
    {
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new PcmCapturePump(
            2,
            _ => processed.TrySetResult(),
            (_, exception) => processed.TrySetException(exception));
        AudioCapturePacket packet = CreatePacket([1, 0]);
        pump.Reset(1);
        Assert.True(pump.TryEnqueue(packet));
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        pump.Reset(2);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool accepted = pump.TryEnqueue(packet);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(accepted);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task Dispose_WithQueuedPackets_DropsPendingWorkBeforeWorkerStops()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        int processed = 0;
        var pump = new PcmCapturePump(
            2,
            _ =>
            {
                if (Interlocked.Increment(ref processed) == 1)
                {
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(2));
                }
            },
            (_, exception) => throw exception);
        pump.Reset(1);
        AudioCapturePacket packet = CreatePacket([0, 0]);
        Assert.True(pump.TryEnqueue(packet));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(pump.TryEnqueue(packet));

        Task release = Task.Run(() =>
        {
            Assert.True(SpinWait.SpinUntil(
                () => pump.IsStopping,
                TimeSpan.FromSeconds(2)));
            releaseFirst.Set();
        });
        pump.Dispose();
        await release.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, Volatile.Read(ref processed));
    }

    private static AudioCapturePacket CreatePacket(byte[] bytes) => new(
        bytes,
        bytes.Length,
        new WaveFormat(48000, 16, 1));
}
