using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>
/// The shared continuous play-and-capture session for the PCM backends. A
/// looping excitation plays while fixed-length capture frames and input levels
/// are raised, until cancellation (normal stop) or a device failure (thrown).
/// </summary>
internal sealed class PcmStreamingSession : IAudioStreamingSession
{
    private readonly IAudioCaptureDevice capture;
    private readonly IAudioPlaybackDevice playback;
    private readonly PcmCaptureSession captureSession;
    private readonly AudioCaptureRouting routing;
    private PcmPlaybackStream? playbackStream;
    private bool disposed;

    public PcmStreamingSession(
        IAudioCaptureDevice capture,
        IAudioPlaybackDevice playback,
        AudioCaptureRouting routing)
    {
        this.capture = capture;
        this.playback = playback;
        this.routing = routing;
        captureSession = new PcmCaptureSession(capture);
        captureSession.LevelsAvailable += HandleLevels;
        captureSession.SequenceChannelsReady += HandleSequence;
        captureSession.CaptureDiscontinuity += HandleDiscontinuity;
    }

    public event Action<AudioCaptureFrame>? FrameAvailable;
    public event Action<AudioInputLevels>? InputLevelsAvailable;
    public event Action? CaptureDiscontinuity;

    public async Task RunAsync(
        AudioPlaybackSignal loopingSignal,
        int sequenceLength,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(loopingSignal);

        captureSession.Sequence = sequenceLength;
        playbackStream = AudioPlaybackStreamFactory.CreatePcm(loopingSignal);
        playbackStream.Rewind();
        var loopingProvider = new LoopingWaveProvider(playbackStream.Stream);
        try
        {
            await captureSession.StartAsync(cancellationToken).ConfigureAwait(false);
            await playback.StartAsync(loopingProvider, cancellationToken).ConfigureAwait(false);
            Task playbackEnded = playback.WaitForPlaybackEndAsync(cancellationToken);
            Task captureStopped = captureSession.WaitForStopAsync(cancellationToken);
            Task completed = await Task.WhenAny(playbackEnded, captureStopped)
                .ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            throw new InvalidOperationException("Live playback stopped unexpectedly.");
        }
        finally
        {
            await playback.StopAsync().ConfigureAwait(false);
            await captureSession.StopAsync().ConfigureAwait(false);
        }
    }

    private void HandleSequence(float[][] sequence)
    {
        FrameAvailable?.Invoke(new AudioCaptureFrame(
            sequence,
            routing.MicrophoneChannel,
            routing.LoopbackChannel));
    }

    private void HandleLevels(AudioChannelLevel[] channels)
    {
        InputLevelsAvailable?.Invoke(AudioLevelResolver.Resolve(channels, routing));
    }

    private void HandleDiscontinuity() => CaptureDiscontinuity?.Invoke();

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        captureSession.LevelsAvailable -= HandleLevels;
        captureSession.SequenceChannelsReady -= HandleSequence;
        captureSession.CaptureDiscontinuity -= HandleDiscontinuity;
        try
        {
            await playback.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            playbackStream?.Dispose();
            await captureSession.DisposeAsync().ConfigureAwait(false);
        }
    }
}
