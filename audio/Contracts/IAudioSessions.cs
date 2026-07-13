namespace Resonalyze.Audio;

/// <summary>
/// A finite play-and-capture session, opened once and reused across the runs of
/// an averaged measurement. The backend owns the device lifecycle, buffer
/// alignment retries, event/thread handling and stop handling; the caller only
/// supplies an excitation signal and reads the captured channels.
/// </summary>
public interface IAudioDuplexSession : IAsyncDisposable
{
    /// <summary>Fired with the microphone (and optional loopback) levels as capture runs.</summary>
    event Action<AudioInputLevels>? InputLevelsAvailable;

    /// <summary>
    /// Plays <paramref name="signal"/> to its end while capturing, then waits for
    /// <paramref name="captureTailSamples"/> further samples (the decay tail).
    /// Reusing the same session for the next run keeps the device open.
    /// </summary>
    Task<AudioCaptureResult> PlayAndCaptureAsync(
        AudioPlaybackSignal signal,
        int captureTailSamples,
        CancellationToken cancellationToken);
}

/// <summary>
/// A continuous playback-and-capture session for live analysis: a looping
/// excitation plays while fixed-length capture frames and input levels are
/// raised, until cancellation or a device failure ends the run.
/// </summary>
public interface IAudioStreamingSession : IAsyncDisposable
{
    event Action<AudioCaptureFrame>? FrameAvailable;
    event Action<AudioInputLevels>? InputLevelsAvailable;
    /// <summary>A capture packet discontinuity was reported (a processing-overload signal).</summary>
    event Action? CaptureDiscontinuity;

    /// <summary>
    /// Runs the loop until the token is cancelled (normal stop). Completes
    /// abnormally — throwing — when the device or driver fails mid-run.
    /// </summary>
    Task RunAsync(
        AudioPlaybackSignal loopingSignal,
        int sequenceLength,
        CancellationToken cancellationToken);
}

/// <summary>
/// A playback-only session (the signal generator): plays a finite or looping
/// signal until it ends or is stopped, surfacing device errors.
/// </summary>
public interface IAudioPlaybackSession : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Completes when a finite signal reaches its end or the session is stopped;
    /// throws when the device fails.
    /// </summary>
    Task WaitForCompletionAsync(CancellationToken cancellationToken);

    Task StopAsync();
}
