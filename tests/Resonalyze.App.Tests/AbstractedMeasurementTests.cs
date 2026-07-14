using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

/// <summary>
/// Verifies the measurement layer runs end-to-end against a fake audio session
/// with no NAudio and no hardware — the core acceptance criterion of the audio
/// refactor.
/// </summary>
public sealed class AbstractedMeasurementTests
{
    private static ExpSweepMeasurement CreateSweep(IAudioSessionFactory factory, int runs = 1)
    {
        var measurement = new ExpSweepMeasurement(factory);
        measurement.Init(
            octaves: 4,
            sampleRate: 44_100,
            bits: 24,
            requestedDuration: 0.05,
            playbackChannel: PlaybackChannel.Mono,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1,
            averageRunCount: runs);
        return measurement;
    }

    [Fact]
    public async Task SweepMeasurementRunsAgainstFakeSession()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        Assert.True(measurement.HasImpulseResponse);
        Assert.Equal(1, factory.DuplexOpenCount);
    }

    [Fact]
    public async Task AveragingReusesTheOpenSession()
    {
        RecordingDuplexSession? opened = null;
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => opened = new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, runs: 3);

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        Assert.Equal(1, factory.DuplexOpenCount);
        Assert.NotNull(opened);
        Assert.Equal(3, opened!.CaptureCount);
        Assert.Equal(3, measurement.AcceptedAverageRunCount);
    }

    [Fact]
    public async Task RejectedRunIsRetried()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (attempt, s, tail, _) => Task.FromResult(attempt == 1
                    ? SyntheticCapture.SilentMicrophone(s, tail)
                    : SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        Assert.Equal(1, measurement.AcceptedAverageRunCount);
        Assert.NotNull(measurement.QualityReport);
    }

    [Fact]
    public async Task CancellationDisposesTheSession()
    {
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingDuplexSession? opened = null;
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => opened = new RecordingDuplexSession(
                signal,
                async (_, _, _, ct) =>
                {
                    captureStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return null!;
                }));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        Task<bool> running = measurement.RunAsync();
        await captureStarted.Task;
        await measurement.AbortAsync();

        Assert.False(await running);
        Assert.Null(measurement.LastError);
        Assert.False(measurement.InProgress);
        Assert.NotNull(opened);
        Assert.True(opened!.Disposed);
    }

    [Fact]
    public async Task DeviceErrorSurfacesInResult()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, _, _, _) => throw new InvalidOperationException("device unplugged")));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        Assert.Contains("device unplugged", measurement.LastError!.Message);
    }

    [Fact]
    public async Task OpenFailureSurfacesInResult()
    {
        var factory = new ThrowingOpenFactory();
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
    }

    [Fact]
    public async Task LiveSpectrumFinishesOnDeviceFailure()
    {
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => new RecordingStreamingSession(framesToRaise: 2, failAfterFrames: true));
        using var measurement = new NoiseMeasurement(factory);
        measurement.Init(
            44_100, 24, 0.5, PlaybackChannel.Mono,
            sequenceLength: 1024,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
    }

    [Fact]
    public async Task LiveSpectrumProducesSnapshotThenStops()
    {
        RecordingStreamingSession? opened = null;
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => opened = new RecordingStreamingSession(
                framesToRaise: 40, failAfterFrames: false));
        using var measurement = new NoiseMeasurement(factory);
        measurement.Init(
            44_100, 24, 0.5, PlaybackChannel.Mono,
            sequenceLength: 1024,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1);

        Task<bool> running = measurement.RunAsync();
        LiveSpectrumSnapshot? snapshot = null;
        for (int i = 0; i < 200 && snapshot == null; i++)
        {
            await Task.Delay(20);
            snapshot = measurement.GetAccumulatedSpectrumSnapshot();
        }
        await measurement.AbortAsync();

        Assert.True(await running, measurement.LastError?.ToString());
        Assert.NotNull(snapshot);
        LiveSpectrumSnapshot? withoutInputMagnitude =
            measurement.GetAccumulatedSpectrumSnapshot(includeInputMagnitude: false);
        LiveSpectrumSnapshot? withInputMagnitude =
            measurement.GetAccumulatedSpectrumSnapshot(includeInputMagnitude: true);
        Assert.NotNull(withoutInputMagnitude);
        Assert.Null(withoutInputMagnitude.InputMagnitude);
        Assert.NotNull(withInputMagnitude?.InputMagnitude);
        Assert.NotNull(opened);
        Assert.True(opened!.Disposed);
    }

    private sealed class ThrowingOpenFactory : IAudioSessionFactory
    {
        public IReadOnlyList<AudioBackendDescriptor> Backends { get; } =
            Array.Empty<AudioBackendDescriptor>();

        public AudioBackendDescriptor GetDescriptor(AudioBackend backend) =>
            new(backend, backend.ToString(), AudioBackendCapabilities.None);

        public ValueTask<IAudioDuplexSession> OpenDuplexAsync(
            AudioSessionRequest request, AudioPlaybackSignal signal, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cannot open device");

        public ValueTask<IAudioStreamingSession> OpenStreamingAsync(
            AudioSessionRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cannot open device");

        public ValueTask<IAudioPlaybackSession> OpenPlaybackAsync(
            AudioSessionRequest request, AudioPlaybackSignal signal, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cannot open device");

        public Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
