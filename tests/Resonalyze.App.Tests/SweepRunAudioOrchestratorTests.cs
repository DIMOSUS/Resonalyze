using NAudio.Wave;

namespace Resonalyze.App.Tests;

public sealed class SweepRunAudioOrchestratorTests
{
    [Fact]
    public async Task FirstRunStartsCaptureBeforePlaybackAndWaitsForTail()
    {
        List<string> calls = [];
        var capture = new FakeCaptureSession(calls) { ReadSamples = 7 };
        var playback = new CompletingPlaybackDevice(calls);
        var orchestrator = new SweepRunAudioOrchestrator(capture, playback);

        float[][] result = await orchestrator.CaptureAsync(
            new SilenceProvider(new WaveFormat(48_000, 16, 1)),
            sweepSamples: 100,
            tailSamples: 25,
            CancellationToken.None);

        Assert.Equal(
            ["capture-start", "playback-start", "playback-wait", "capture-wait", "snapshot"],
            calls);
        Assert.Equal(132, capture.RequiredSamples);
        Assert.Same(capture.Snapshot, result);
    }

    [Fact]
    public async Task SubsequentRunResetsCaptureWithoutRestartingDevice()
    {
        List<string> calls = [];
        var capture = new FakeCaptureSession(calls);
        var playback = new CompletingPlaybackDevice(calls);
        var orchestrator = new SweepRunAudioOrchestrator(capture, playback);
        var source = new SilenceProvider(new WaveFormat(48_000, 16, 1));

        await orchestrator.CaptureAsync(source, 100, 25, CancellationToken.None);
        calls.Clear();
        await orchestrator.CaptureAsync(source, 100, 25, CancellationToken.None);

        Assert.Equal(
            ["capture-reset", "playback-start", "playback-wait", "capture-wait", "snapshot"],
            calls);
        Assert.Equal(1, capture.StartCount);
        Assert.Equal(1, capture.ResetCount);
        Assert.Equal(2, playback.StartCount);
    }

    [Fact]
    public async Task PlaybackCancellationDoesNotWaitForCaptureTail()
    {
        List<string> calls = [];
        var capture = new FakeCaptureSession(calls);
        var playback = new BlockingPlaybackDevice(calls);
        var orchestrator = new SweepRunAudioOrchestrator(capture, playback);
        using var cancellation = new CancellationTokenSource();
        Task<float[][]> run = orchestrator.CaptureAsync(
            new SilenceProvider(new WaveFormat(48_000, 16, 1)),
            100,
            25,
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.DoesNotContain("capture-wait", calls);
        Assert.Contains("playback-stop", calls);
    }

    private sealed class FakeCaptureSession : ISweepCaptureSession
    {
        private readonly List<string> calls;

        public FakeCaptureSession(List<string> calls)
        {
            this.calls = calls;
        }

        public int ReadSamples { get; set; }
        public int RequiredSamples { get; private set; }
        public int StartCount { get; private set; }
        public int ResetCount { get; private set; }
        public float[][] Snapshot { get; } = [[1.0f], [2.0f]];

        public Task StartAsync(CancellationToken cancellationToken)
        {
            calls.Add("capture-start");
            StartCount++;
            return Task.CompletedTask;
        }

        public void Reset()
        {
            calls.Add("capture-reset");
            ResetCount++;
            ReadSamples = 0;
        }

        public Task WaitForSamplesAsync(int sampleCount, CancellationToken cancellationToken)
        {
            calls.Add("capture-wait");
            RequiredSamples = sampleCount;
            return Task.CompletedTask;
        }

        public float[][] GetSamplesSnapshot()
        {
            calls.Add("snapshot");
            return Snapshot;
        }
    }

    private sealed class CompletingPlaybackDevice : IAudioPlaybackDevice
    {
        private readonly List<string> calls;

        public CompletingPlaybackDevice(List<string> calls)
        {
            this.calls = calls;
        }

        public WaveFormat PlaybackFormat { get; } = new(48_000, 16, 1);
        public int StartCount { get; private set; }

        public Task StartAsync(IWaveProvider source, CancellationToken cancellationToken)
        {
            calls.Add("playback-start");
            StartCount++;
            return Task.CompletedTask;
        }

        public Task WaitForPlaybackEndAsync(CancellationToken cancellationToken)
        {
            calls.Add("playback-wait");
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingPlaybackDevice : IAudioPlaybackDevice
    {
        private readonly List<string> calls;
        private readonly TaskCompletionSource<bool> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingPlaybackDevice(List<string> calls)
        {
            this.calls = calls;
        }

        public WaveFormat PlaybackFormat { get; } = new(48_000, 16, 1);

        public Task StartAsync(IWaveProvider source, CancellationToken cancellationToken)
        {
            calls.Add("playback-start");
            return Task.CompletedTask;
        }

        public Task WaitForPlaybackEndAsync(CancellationToken cancellationToken)
        {
            calls.Add("playback-wait");
            return completion.Task.WaitAsync(cancellationToken);
        }

        public Task StopAsync()
        {
            calls.Add("playback-stop");
            completion.TrySetResult(true);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
