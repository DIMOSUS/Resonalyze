using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

/// <summary>
/// A fully in-memory <see cref="IAudioSessionFactory"/> for exercising the
/// measurement layer without NAudio or hardware. Sessions are recorded so tests
/// can assert reuse, disposal and error propagation.
/// </summary>
internal sealed class FakeAudioSessionFactory : IAudioSessionFactory
{
    private readonly Func<AudioSessionRequest, AudioPlaybackSignal, IAudioDuplexSession>? duplexFactory;
    private readonly Func<AudioSessionRequest, IAudioStreamingSession>? streamingFactory;
    private readonly Func<AudioSessionRequest, AudioPlaybackSignal, IAudioPlaybackSession>? playbackFactory;

    public FakeAudioSessionFactory(
        Func<AudioSessionRequest, AudioPlaybackSignal, IAudioDuplexSession>? duplexFactory = null,
        Func<AudioSessionRequest, IAudioStreamingSession>? streamingFactory = null,
        Func<AudioSessionRequest, AudioPlaybackSignal, IAudioPlaybackSession>? playbackFactory = null)
    {
        this.duplexFactory = duplexFactory;
        this.streamingFactory = streamingFactory;
        this.playbackFactory = playbackFactory;
    }

    public int DuplexOpenCount { get; private set; }
    public int StreamingOpenCount { get; private set; }
    public int WarmUpCount { get; private set; }
    public AudioSessionRequest? LastRequest { get; private set; }

    public IReadOnlyList<AudioBackendDescriptor> Backends { get; } =
        Array.Empty<AudioBackendDescriptor>();

    public AudioBackendDescriptor GetDescriptor(AudioBackend backend) =>
        new(backend, backend.ToString(), AudioBackendCapabilities.None);

    public ValueTask<IAudioDuplexSession> OpenDuplexAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken)
    {
        DuplexOpenCount++;
        LastRequest = request;
        IAudioDuplexSession session = duplexFactory?.Invoke(request, signal) ?? new NoopDuplexSession();
        return ValueTask.FromResult(session);
    }

    public ValueTask<IAudioStreamingSession> OpenStreamingAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken)
    {
        StreamingOpenCount++;
        LastRequest = request;
        IAudioStreamingSession session = streamingFactory?.Invoke(request) ?? new NoopStreamingSession();
        return ValueTask.FromResult(session);
    }

    public ValueTask<IAudioPlaybackSession> OpenPlaybackAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        IAudioPlaybackSession session = playbackFactory?.Invoke(request, signal)
            ?? new NoopPlaybackSession();
        return ValueTask.FromResult(session);
    }

    public Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken)
    {
        WarmUpCount++;
        LastRequest = request;
        return Task.CompletedTask;
    }

    private sealed class NoopDuplexSession : IAudioDuplexSession
    {
        public event Action<AudioInputLevels>? InputLevelsAvailable { add { } remove { } }

        public Task<AudioCaptureResult> PlayAndCaptureAsync(
            int captureTailSamples, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This fake session does not capture.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopStreamingSession : IAudioStreamingSession
    {
        public event Action<AudioCaptureFrame>? FrameAvailable { add { } remove { } }
        public event Action<AudioInputLevels>? InputLevelsAvailable { add { } remove { } }
        public event Action? CaptureDiscontinuity { add { } remove { } }

        public Task RunAsync(
            AudioPlaybackSignal loopingSignal, int sequenceLength, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopPlaybackSession : IAudioPlaybackSession
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WaitForCompletionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
