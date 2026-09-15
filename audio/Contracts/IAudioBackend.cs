namespace Resonalyze.Audio;

public interface IAudioBackend
{
    AudioBackendDescriptor Descriptor { get; }

    /// <summary>Everything that can fail (device open, exclusive format, stream build) happens here, so rollback is asynchronous.</summary>
    ValueTask<IAudioDuplexSession> OpenDuplexAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken);

    ValueTask<IAudioStreamingSession> OpenStreamingAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<IAudioPlaybackSession> OpenPlaybackAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken);

    Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken);
}

public interface IAudioBackendRegistry
{
    IReadOnlyList<AudioBackendDescriptor> Backends { get; }

    IAudioBackend GetBackend(AudioBackend id);
}

public interface IAudioSessionFactory
{
    IReadOnlyList<AudioBackendDescriptor> Backends { get; }

    AudioBackendDescriptor GetDescriptor(AudioBackend backend);

    ValueTask<IAudioDuplexSession> OpenDuplexAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken);

    ValueTask<IAudioStreamingSession> OpenStreamingAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<IAudioPlaybackSession> OpenPlaybackAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken);

    Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken);
}
