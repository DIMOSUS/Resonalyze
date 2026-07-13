using NAudio.Wave;

namespace Resonalyze.Audio.Tests;

public sealed class AudioPlaybackRunnerTests
{
    [Fact]
    public async Task PlayToEnd_DoesNotCompleteBeforePlaybackEnds()
    {
        var playback = new FakePlaybackDevice();
        Task run = AudioPlaybackRunner.PlayToEndAsync(
            playback,
            new SilenceProvider(new WaveFormat(48_000, 16, 1)),
            CancellationToken.None);

        Assert.False(run.IsCompleted);
        playback.Complete();
        await run;

        Assert.Equal(1, playback.StartCount);
        Assert.Equal(0, playback.StopCount);
    }

    [Fact]
    public async Task PlayToEnd_CancellationStopsPlayback()
    {
        var playback = new FakePlaybackDevice();
        using var cancellation = new CancellationTokenSource();
        Task run = AudioPlaybackRunner.PlayToEndAsync(
            playback,
            new SilenceProvider(new WaveFormat(48_000, 16, 1)),
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, playback.StopCount);
    }

    [Fact]
    public async Task PlayToEnd_PlaybackFailureStopsAndPropagates()
    {
        var playback = new FakePlaybackDevice();
        Task run = AudioPlaybackRunner.PlayToEndAsync(
            playback,
            new SilenceProvider(new WaveFormat(48_000, 16, 1)),
            CancellationToken.None);
        playback.Fail(new IOException("device lost"));

        IOException exception = await Assert.ThrowsAsync<IOException>(() => run);

        Assert.Equal("device lost", exception.Message);
        Assert.Equal(1, playback.StopCount);
    }

    private sealed class FakePlaybackDevice : IAudioPlaybackDevice
    {
        private readonly TaskCompletionSource<bool> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WaveFormat PlaybackFormat { get; } = new(48_000, 16, 1);
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(IWaveProvider source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            return Task.CompletedTask;
        }

        public Task WaitForPlaybackEndAsync(CancellationToken cancellationToken) =>
            completion.Task.WaitAsync(cancellationToken);

        public Task StopAsync()
        {
            StopCount++;
            completion.TrySetResult(true);
            return Task.CompletedTask;
        }

        public void Complete() => completion.TrySetResult(true);

        public void Fail(Exception exception) => completion.TrySetException(exception);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
