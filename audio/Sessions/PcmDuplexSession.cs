namespace Resonalyze.Audio;

/// <summary>Finite session for Wave/MME and WASAPI: devices open once and replay across averaged runs.</summary>
internal sealed class PcmDuplexSession : IAudioDuplexSession
{
    private readonly IAudioCaptureDevice capture;
    private readonly IAudioPlaybackDevice playback;
    private readonly PcmCaptureSession captureSession;
    private readonly SweepRunAudioOrchestrator orchestrator;
    private readonly AudioCaptureRouting routing;
    private readonly string backendName;
    private readonly int requestedBufferMilliseconds;
    private readonly int signalSampleCount;
    // The render device rejects a different source after the first run.
    private readonly PcmPlaybackStream playbackStream;
    private bool disposed;

    public PcmDuplexSession(
        IAudioCaptureDevice capture,
        IAudioPlaybackDevice playback,
        AudioPlaybackSignal signal,
        AudioCaptureRouting routing,
        int expectedCaptureSamples,
        string backendName,
        int requestedBufferMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(signal);
        this.capture = capture;
        this.playback = playback;
        this.routing = routing;
        this.backendName = backendName;
        this.requestedBufferMilliseconds = requestedBufferMilliseconds;
        signalSampleCount = signal.SampleCount;
        playbackStream = AudioPlaybackStreamFactory.CreatePcm(signal);
        captureSession = new PcmCaptureSession(capture, expectedSamples: expectedCaptureSamples);
        captureSession.LevelsAvailable += HandleLevels;
        orchestrator = new SweepRunAudioOrchestrator(captureSession, playback);
    }

    public event Action<AudioInputLevels>? InputLevelsAvailable;

    public async Task<AudioCaptureResult> PlayAndCaptureAsync(
        int captureTailSamples,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var captureSource = capture as ICaptureDiagnosticsSource;
        var renderSource = playback as IRenderDiagnosticsSource;
        long discontinuitiesBefore = captureSource?.Discontinuities ?? 0;
        long timestampErrorsBefore = captureSource?.TimestampErrors ?? 0;
        long renderUnderrunsBefore = renderSource?.RenderUnderruns ?? 0;

        playbackStream.Rewind();
        float[][] channels = await orchestrator.CaptureAsync(
            playbackStream.Stream,
            signalSampleCount,
            captureTailSamples,
            cancellationToken).ConfigureAwait(false);
        captureSession.Pause();

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
            diagnostics)
        {
            ArrayChannels = routing.ArrayChannels
        };
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
            playbackStream.Dispose();
            await captureSession.DisposeAsync().ConfigureAwait(false);
        }
    }
}
