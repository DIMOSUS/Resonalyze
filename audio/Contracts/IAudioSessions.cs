namespace Resonalyze.Audio;

/// <summary>One open session = one sweep, replayed across averaged runs; render devices reject a different source once initialized.</summary>
public interface IAudioDuplexSession : IAsyncDisposable
{
    event Action<AudioInputLevels>? InputLevelsAvailable;

    /// <summary>Plays the signal, then waits <paramref name="captureTailSamples"/> more samples; the device stays open for the next run.</summary>
    Task<AudioCaptureResult> PlayAndCaptureAsync(
        int captureTailSamples,
        CancellationToken cancellationToken);
}

public interface IAudioStreamingSession : IAsyncDisposable
{
    event Action<AudioCaptureFrame>? FrameAvailable;
    event Action<AudioInputLevels>? InputLevelsAvailable;
    event Action? CaptureDiscontinuity;

    /// <summary>Returns on cancellation; throws when the device fails mid-run.</summary>
    Task RunAsync(
        AudioPlaybackSignal loopingSignal,
        int sequenceLength,
        CancellationToken cancellationToken);
}

public interface IAudioPlaybackSession : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    Task WaitForCompletionAsync(CancellationToken cancellationToken);

    Task StopAsync();
}
