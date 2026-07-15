using NAudio.Wave.Asio;

namespace Resonalyze.Audio.Tests;

public sealed class AsioFullDuplexSessionTests
{
    [Fact]
    public async Task ResetCapture_WhileOldBlockWaitsToCommit_DropsOldGeneration()
    {
        using var oldBlockAtCommit = new ManualResetEventSlim();
        using var releaseOldBlock = new ManualResetEventSlim();
        int commitCount = 0;
        using var session = new AsioFullDuplexSession(
            "test-driver",
            inputChannelOffset: 0,
            outputChannelOffset: 0,
            beforeCaptureCommit: () =>
            {
                if (Interlocked.Increment(ref commitCount) == 1)
                {
                    oldBlockAtCommit.Set();
                    releaseOldBlock.Wait(TimeSpan.FromSeconds(2));
                }
            });
        session.ResetCapture(expectedTotalSamples: 8);

        Task oldProcessing = Task.Run(() =>
            session.ProcessCaptureBlock(CreateBlock(value: 1, generation: 1)));
        Assert.True(oldBlockAtCommit.Wait(TimeSpan.FromSeconds(2)));

        session.ResetCapture(expectedTotalSamples: 8);
        releaseOldBlock.Set();
        await oldProcessing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, session.ReadSamples);

        session.ProcessCaptureBlock(CreateBlock(value: 2, generation: 2));

        Assert.Equal([2f], session.CompleteCaptureSnapshot()[0]);
    }

    [Fact]
    public async Task CompleteCaptureSnapshot_CopiesAfterReleasingSessionLock()
    {
        using var copyStarted = new ManualResetEventSlim();
        using var releaseCopy = new ManualResetEventSlim();
        using var session = new AsioFullDuplexSession(
            "test-driver",
            inputChannelOffset: 0,
            outputChannelOffset: 0,
            beforeSnapshotCopy: () =>
            {
                copyStarted.Set();
                releaseCopy.Wait(TimeSpan.FromSeconds(2));
            });
        session.ResetCapture(expectedTotalSamples: 8);
        session.ProcessCaptureBlock(CreateBlock(value: 1, generation: 1));

        Task<float[][]> snapshotTask = Task.Run(session.CompleteCaptureSnapshot);
        Assert.True(copyStarted.Wait(TimeSpan.FromSeconds(2)));

        // Completion advanced the epoch and detached the accumulator. A new
        // worker block must acquire the session lock and return its queue slot
        // even while the old accumulator performs its potentially large copy.
        Task worker = Task.Run(() =>
            session.ProcessCaptureBlock(CreateBlock(value: 2, generation: 2)));
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, session.ReadSamples);

        releaseCopy.Set();
        float[][] snapshot = await snapshotTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal([1f], snapshot[0]);
    }

    private static AsioCaptureBlock CreateBlock(float value, int generation)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        return new AsioCaptureBlock(
            [bytes],
            AsioSampleType.Float32LSB,
            FrameCount: 1,
            Generation: generation);
    }
}
