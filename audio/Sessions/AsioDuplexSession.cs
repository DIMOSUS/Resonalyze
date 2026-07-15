namespace Resonalyze.Audio;

/// <summary>
/// Finite play-and-capture over a single ASIO driver session. The driver is
/// opened once and kept running across averaging runs (re-initializing it per
/// run costs seconds on slow drivers); each run resets the capture accumulator
/// and rewinds the excitation. Capture is paused between runs so a long
/// confirmation wait does not grow the buffer while the meter stays live.
/// </summary>
internal sealed class AsioDuplexSession : IAudioDuplexSession
{
    private readonly AsioFullDuplexSession session;
    private readonly int sampleRate;
    private readonly int firstInputOffset;
    private readonly int signalSampleCount;
    private readonly AudioCaptureRouting relativeRouting;
    private readonly FloatArrayWaveStream stream;
    private bool started;
    private bool disposed;

    public AsioDuplexSession(AudioSessionRequest request, AudioPlaybackSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        int mic = request.Routing.MicrophoneChannel;
        int? loopback = request.Routing.LoopbackChannel;
        firstInputOffset = CaptureChannelLayout.AsioFirstInputOffset(mic, loopback);
        int inputChannelCount = CaptureChannelLayout.AsioInputChannelCount(mic, loopback);
        sampleRate = request.SampleRate;
        signalSampleCount = signal.SampleCount;
        relativeRouting = new AudioCaptureRouting(
            mic - firstInputOffset,
            loopback.HasValue ? loopback.Value - firstInputOffset : null);
        session = new AsioFullDuplexSession(
            request.AsioDriverName ?? string.Empty,
            firstInputOffset,
            request.AsioOutputChannelOffset,
            inputChannelCount);
        session.LevelsAvailable += HandleLevels;
        stream = AudioPlaybackStreamFactory.CreateFloat(signal);
    }

    public event Action<AudioInputLevels>? InputLevelsAvailable;

    public async Task<AudioCaptureResult> PlayAndCaptureAsync(
        int captureTailSamples,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        int expectedTotalSamples = signalSampleCount + sampleRate * 2;
        if (!started)
        {
            stream.Position = 0;
            await session.StartAsync(
                stream,
                sampleRate,
                autoStop: false,
                cancellationToken,
                expectedTotalSamples).ConfigureAwait(false);
            started = true;
        }
        else
        {
            // The stream ran out (the driver is playing silence); a fresh
            // accumulator starts this run's capture and the rewind replays the
            // excitation from its first sample.
            session.ResetCapture(expectedTotalSamples);
            stream.Position = 0;
        }

        int requiredSamples = session.ReadSamples + signalSampleCount + captureTailSamples;
        await session.WaitForSamplesAsync(requiredSamples, cancellationToken)
            .ConfigureAwait(false);
        float[][] channels = session.CompleteCaptureSnapshot();

        return new AudioCaptureResult(
            channels,
            relativeRouting.MicrophoneChannel,
            relativeRouting.LoopbackChannel,
            StereoSeparationExpected: false,
            AudioCaptureAnomalies.None,
            Diagnostics: null);
    }

    private void HandleLevels(AudioChannelLevel[] channels)
    {
        InputLevelsAvailable?.Invoke(AudioLevelResolver.Resolve(channels, relativeRouting));
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        session.LevelsAvailable -= HandleLevels;
        try
        {
            await session.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            session.Dispose();
            stream.Dispose();
        }
    }
}
