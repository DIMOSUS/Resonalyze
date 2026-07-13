namespace Resonalyze.Audio;

/// <summary>
/// One concrete audio backend (WASAPI Shared/Exclusive, ASIO, MME). Hides every
/// backend-specific detail — device creation, format negotiation, alignment
/// retries, render/capture threads, hardware error handling — behind the
/// session contracts. Selected by <see cref="AudioBackend"/> in the registry;
/// callers never construct one directly.
/// </summary>
public interface IAudioBackend
{
    AudioBackendDescriptor Descriptor { get; }

    /// <summary>
    /// Opens a finite play-and-capture session. Construction that can fail (opening
    /// devices, negotiating an exclusive format) happens here so resource rollback
    /// is fully asynchronous.
    /// </summary>
    ValueTask<IAudioDuplexSession> OpenDuplexAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Opens a continuous play-and-capture session for live analysis.</summary>
    ValueTask<IAudioStreamingSession> OpenStreamingAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Opens a playback-only session for a prepared signal.</summary>
    ValueTask<IAudioPlaybackSession> OpenPlaybackAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken);

    /// <summary>
    /// Briefly opens and tears down a session to page in the driver, so the first
    /// real measurement does not pay cold-start latency. Best-effort.
    /// </summary>
    Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken);
}

/// <summary>Immutable set of the available backends, keyed by identifier.</summary>
public interface IAudioBackendRegistry
{
    IReadOnlyList<AudioBackendDescriptor> Backends { get; }

    IAudioBackend GetBackend(AudioBackend id);
}

/// <summary>
/// The single entry point the measurement/application layer uses to open audio
/// sessions. It resolves the backend from the request and delegates — the one
/// place a fake implementation is swapped in for tests.
/// </summary>
public interface IAudioSessionFactory
{
    IReadOnlyList<AudioBackendDescriptor> Backends { get; }

    AudioBackendDescriptor GetDescriptor(AudioBackend backend);

    ValueTask<IAudioDuplexSession> OpenDuplexAsync(
        AudioSessionRequest request,
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
