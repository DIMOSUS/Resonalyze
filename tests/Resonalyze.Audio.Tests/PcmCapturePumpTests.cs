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
            block => completed.TrySetResult((
                block.Buffer[..block.BytesRecorded],
                Environment.CurrentManagedThreadId)),
            exception => completed.TrySetException(exception));
        pump.Reset(1);
        byte[] source = [1, 2, 3, 4];

        Assert.True(pump.TryEnqueue(CreatePacket(source)));
        Array.Clear(source);

        (byte[] bytes, int workerThread) = await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal([1, 2, 3, 4], bytes);
        Assert.NotEqual(callbackThread, workerThread);
    }

    [Fact]
    public async Task TryEnqueue_WhenQueueIsFull_ReportsTerminalFailure()
    {
        using var releaseWorker = new ManualResetEventSlim();
        var failure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new PcmCapturePump(
            _ => releaseWorker.Wait(TimeSpan.FromSeconds(2)),
            exception => failure.TrySetResult(exception));
        pump.Reset(1);
        AudioCaptureDataEventArgs packet = CreatePacket([0, 0]);

        int accepted = 0;
        while (pump.TryEnqueue(packet))
        {
            accepted++;
        }

        Exception exception = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseWorker.Set();
        Assert.Equal(16, accepted);
        Assert.Contains("could not keep up", exception.Message);
    }

    private static AudioCaptureDataEventArgs CreatePacket(byte[] bytes) => new()
    {
        Buffer = bytes,
        BytesRecorded = bytes.Length,
        Format = new WaveFormat(48000, 16, 1)
    };
}
