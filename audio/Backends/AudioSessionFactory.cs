namespace Resonalyze.Audio;

/// <summary>
/// The single entry point the measurement/application layer uses to open audio
/// sessions. Resolves the backend from each request via the registry and
/// delegates; this is the seam a fake implementation replaces in tests.
/// </summary>
public sealed class AudioSessionFactory : IAudioSessionFactory
{
    private readonly IAudioBackendRegistry registry;

    public AudioSessionFactory(IAudioBackendRegistry registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<AudioBackendDescriptor> Backends => registry.Backends;

    public AudioBackendDescriptor GetDescriptor(AudioBackend backend) =>
        registry.GetBackend(backend).Descriptor;

    public ValueTask<IAudioDuplexSession> OpenDuplexAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken) =>
        registry.GetBackend(request.Backend).OpenDuplexAsync(request, signal, cancellationToken);

    public ValueTask<IAudioStreamingSession> OpenStreamingAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken) =>
        registry.GetBackend(request.Backend).OpenStreamingAsync(request, cancellationToken);

    public ValueTask<IAudioPlaybackSession> OpenPlaybackAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken) =>
        registry.GetBackend(request.Backend).OpenPlaybackAsync(request, signal, cancellationToken);

    public Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken) =>
        registry.GetBackend(request.Backend).WarmUpAsync(request, cancellationToken);
}
