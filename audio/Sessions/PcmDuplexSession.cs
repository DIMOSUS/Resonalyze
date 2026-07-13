using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>
/// The shared finite play-and-capture session for the PCM backends (Wave/MME
/// and WASAPI Shared/Exclusive). Opens the capture device + playback device
/// once and replays across the runs of an averaged sweep; the accumulator
/// resets between runs instead of reopening the endpoint. WASAPI counters are
/// folded into an <see cref="AudioSessionDiagnostics"/> snapshot; MME produces
/// none.
/// </summary>
internal sealed class PcmDuplexSession : IAudioDuplexSession
{
    private readonly IAudioCaptureDevice capture;
    private readonly IAudioPlaybackDevice playback;
    private readonly PcmCaptureSession captureSession;
    private readonly SweepRunAudioOrchestrator orchestrator;
    private readonly AudioCaptureRouting routing;
    private readonly string backendName;
    private readonly int requestedBufferMilliseconds;
    private PcmPlaybackStream? playbackStream;
    private AudioPlaybackSignal? cachedSignal;
    private bool disposed;

    public PcmDuplexSession(
        IAudioCaptureDevice capture,
        IAudioPlaybackDevice playback,
        AudioCaptureRouting routing,
        int expectedCaptureSamples,
        string backendName,
        int requestedBufferMilliseconds)
    {
        this.capture = capture;
        this.playback = playback;
        this.routing = routing;
        this.backendName = backendName;
        this.requestedBufferMilliseconds = requestedBufferMilliseconds;
        captureSession = new PcmCaptureSession(capture, expectedSamples: expectedCaptureSamples);
        captureSession.LevelsAvailable += HandleLevels;
        orchestrator = new SweepRunAudioOrchestrator(captureSession, playback);
    }

    public event Action<AudioInputLevels>? InputLevelsAvailable;

    public async Task<AudioCaptureResult> PlayAndCaptureAsync(
        AudioPlaybackSignal signal,
        int captureTailSamples,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(signal);

        var captureSource = capture as ICaptureDiagnosticsSource;
        var renderSource = playback as IRenderDiagnosticsSource;
        long discontinuitiesBefore = captureSource?.Discontinuities ?? 0;
        long timestampErrorsBefore = captureSource?.TimestampErrors ?? 0;
        long renderUnderrunsBefore = renderSource?.RenderUnderruns ?? 0;

        WaveStream stream = EnsurePlaybackStream(signal);
        stream.Position = 0;
        float[][] channels = await orchestrator.CaptureAsync(
            stream,
            signal.SampleCount,
            captureTailSamples,
            cancellationToken).ConfigureAwait(false);

        AudioCaptureAnomalies anomalies = AudioCaptureAnomalies.None;
        AudioSessionDiagnostics? diagnostics = null;
        if (captureSource != null && renderSource != null)
        {
            if (captureSource.Discontinuities > discontinuitiesBefore)
            {
                anomalies |= AudioCaptureAnomalies.CaptureDiscontinuity;
            }
            if (captureSource.TimestampErrors > timestampErrorsBefore)
            {
                anomalies |= AudioCaptureAnomalies.CaptureTimestampError;
            }
            if (renderSource.RenderUnderruns > renderUnderrunsBefore)
            {
                anomalies |= AudioCaptureAnomalies.RenderUnderrun;
            }
            diagnostics = new AudioSessionDiagnostics(
                backendName,
                captureSource.EndpointId,
                renderSource.EndpointId,
                AudioFormatConversions.ToAudioFormat(capture.CaptureFormat),
                AudioFormatConversions.ToAudioFormat(playback.PlaybackFormat),
                requestedBufferMilliseconds,
                renderSource.ActualBufferFrames,
                captureSource.CapturePackets,
                renderSource.RenderCallbacks,
                captureSource.Discontinuities,
                captureSource.SilentPackets,
                captureSource.TimestampErrors,
                0,
                renderSource.RenderUnderruns);
        }

        return new AudioCaptureResult(
            channels,
            routing.MicrophoneChannel,
            routing.LoopbackChannel,
            StereoSeparationExpected: true,
            anomalies,
            diagnostics);
    }

    private WaveStream EnsurePlaybackStream(AudioPlaybackSignal signal)
    {
        if (playbackStream != null && ReferenceEquals(cachedSignal, signal))
        {
            return playbackStream.Stream;
        }

        // A different signal (or the first run): build the reusable PCM stream.
        // The same stream instance is replayed across runs so the playback
        // device's single-source guard is satisfied.
        playbackStream?.Dispose();
        playbackStream = AudioPlaybackStreamFactory.CreatePcm(signal);
        cachedSignal = signal;
        return playbackStream.Stream;
    }

    private void HandleLevels(AudioChannelLevel[] channels)
    {
        Action<AudioInputLevels>? handler = InputLevelsAvailable;
        if (handler == null)
        {
            return;
        }
        handler(AudioLevelResolver.Resolve(channels, routing));
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        captureSession.LevelsAvailable -= HandleLevels;
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
