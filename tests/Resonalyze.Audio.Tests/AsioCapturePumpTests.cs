using System.Runtime.InteropServices;
using NAudio.Wave.Asio;

namespace Resonalyze.Audio.Tests;

public sealed class AsioCapturePumpTests
{
    [Fact]
    public async Task TryEnqueue_CopiesBeforeReturning_AndProcessesOnWorker()
    {
        int callbackThread = Environment.CurrentManagedThreadId;
        var completed = new TaskCompletionSource<(float[] Samples, int ThreadId)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var converter = new AsioSampleConverter();
        using var pump = new AsioCapturePump(
            1,
            block =>
            {
                var samples = new float[block.FrameCount];
                converter.Convert(block.Channels[0], block.SampleType, samples, block.FrameCount);
                completed.TrySetResult((samples, Environment.CurrentManagedThreadId));
            },
            (_, exception) => completed.TrySetException(exception));
        pump.Prepare(maximumByteCount: 12);
        pump.Reset(1);

        float[] source = [0.25f, -0.5f, 0.75f];
        GCHandle handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            Assert.True(pump.TryEnqueue(
                [handle.AddrOfPinnedObject()],
                0,
                AsioSampleType.Float32LSB,
                source.Length));
            Array.Clear(source);
        }
        finally
        {
            handle.Free();
        }

        (float[] samples, int workerThread) = await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal([0.25f, -0.5f, 0.75f], samples);
        Assert.NotEqual(callbackThread, workerThread);
    }

    [Fact]
    public async Task Reset_DrainsQueuedOldBlocks_AndRetagsNewBlocks()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var results = new List<(int Generation, float Value)>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var converter = new AsioSampleConverter();
        using var pump = new AsioCapturePump(
            1,
            block =>
            {
                var sample = new float[1];
                converter.Convert(block.Channels[0], block.SampleType, sample, 1);
                if (sample[0] == 1)
                {
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(2));
                }
                lock (results)
                {
                    results.Add((block.Generation, sample[0]));
                }
                if (sample[0] == 9)
                {
                    completed.TrySetResult();
                }
            },
            (_, exception) => completed.TrySetException(exception));
        pump.Prepare(4);
        pump.Reset(1);

        using var first = new PinnedFloat(1);
        using var second = new PinnedFloat(2);
        using var third = new PinnedFloat(3);
        using var newest = new PinnedFloat(9);
        Assert.True(Enqueue(pump, first));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(Enqueue(pump, second));
        Assert.True(Enqueue(pump, third));

        pump.Reset(2);
        Assert.True(Enqueue(pump, newest));
        releaseFirst.Set();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([(1, 1f), (2, 9f)], results);
    }

    [Fact]
    public async Task Overflow_Reset_AllowsNextGenerationToProcess()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var failure = new TaskCompletionSource<(int Generation, Exception Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new AsioCapturePump(
            1,
            block =>
            {
                if (block.Generation == 1)
                {
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(2));
                }
                else
                {
                    completed.TrySetResult();
                }
            },
            (generation, exception) => failure.TrySetResult((generation, exception)));
        pump.Prepare(4);
        pump.Reset(1);
        using var sample = new PinnedFloat(0.5f);

        Assert.True(Enqueue(pump, sample));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        for (int index = 1; index < 8; index++)
        {
            Assert.True(Enqueue(pump, sample));
        }
        Assert.False(Enqueue(pump, sample));
        releaseFirst.Set();

        (int failedGeneration, Exception error) =
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, failedGeneration);
        Assert.Contains("could not keep up", error.Message);

        pump.Reset(2);
        Assert.True(Enqueue(pump, sample));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WorkerException_Reset_AllowsNextGenerationToProcess()
    {
        var failure = new TaskCompletionSource<(int Generation, Exception Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new AsioCapturePump(
            1,
            block =>
            {
                if (block.Generation == 1)
                {
                    throw new IOException("worker failed");
                }
                completed.TrySetResult();
            },
            (generation, exception) => failure.TrySetResult((generation, exception)));
        pump.Prepare(4);
        pump.Reset(1);
        using var sample = new PinnedFloat(0.5f);

        Assert.True(Enqueue(pump, sample));
        (int failedGeneration, Exception error) =
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, failedGeneration);
        Assert.Equal("worker failed", error.Message);

        pump.Reset(2);
        Assert.True(Enqueue(pump, sample));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void TryEnqueue_AfterWarmup_DoesNotAllocateOnCallingThread()
    {
        using var processed = new ManualResetEventSlim();
        using var pump = new AsioCapturePump(
            1,
            _ => processed.Set(),
            (_, exception) => throw exception);
        pump.Prepare(4);
        pump.Reset(1);
        using var sample = new PinnedFloat(0.5f);
        IntPtr[] inputBuffers = [sample.Pointer];
        Assert.True(pump.TryEnqueue(inputBuffers, 0, AsioSampleType.Float32LSB, 1));
        Assert.True(processed.Wait(TimeSpan.FromSeconds(2)));
        processed.Reset();
        pump.Reset(2);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool accepted = pump.TryEnqueue(inputBuffers, 0, AsioSampleType.Float32LSB, 1);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(accepted);
        Assert.Equal(0, allocated);
        Assert.True(processed.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Dispose_WithQueuedBlocks_ProcessesQueueBeforeWorkerStops()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        int processed = 0;
        using var pump = new AsioCapturePump(
            1,
            _ =>
            {
                if (Interlocked.Increment(ref processed) == 1)
                {
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(2));
                }
            },
            (_, exception) => throw exception);
        pump.Prepare(4);
        pump.Reset(1);
        using var sample = new PinnedFloat(0.5f);
        Assert.True(Enqueue(pump, sample));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(Enqueue(pump, sample));

        Task disposing = Task.Run(pump.Dispose);
        releaseFirst.Set();
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, Volatile.Read(ref processed));
    }

    private static bool Enqueue(AsioCapturePump pump, PinnedFloat sample) =>
        pump.TryEnqueue([sample.Pointer], 0, AsioSampleType.Float32LSB, 1);

    private sealed class PinnedFloat : IDisposable
    {
        private readonly float[] sample;
        private GCHandle handle;

        public PinnedFloat(float value)
        {
            sample = [value];
            handle = GCHandle.Alloc(sample, GCHandleType.Pinned);
        }

        public IntPtr Pointer => handle.AddrOfPinnedObject();

        public void Dispose()
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }
}
