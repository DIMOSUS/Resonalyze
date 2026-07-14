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
            exception => completed.TrySetException(exception));

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
}
