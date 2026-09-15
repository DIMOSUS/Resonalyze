namespace Resonalyze.Audio;

/// <summary>The driver stays open across averaging runs; each run resets the accumulator and rewinds the excitation. See docs/tech/audio-layer.md#averaged-runs-keep-the-device-open.</summary>
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
        firstInputOffset = CaptureChannelLayout.AsioFirstInputOffset(request.Routing);
        int inputChannelCount = CaptureChannelLayout.AsioInputChannelCount(request.Routing);
        sampleRate = request.SampleRate;
        signalSampleCount = signal.SampleCount;
        relativeRouting = CaptureChannelLayout.ToAsioRelative(request.Routing);
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
            session.ResetCapture(expectedTotalSamples);
            stream.Position = 0;
        }

        // AcceptedSamples includes blocks queued around the rewind; ReadSamples could finish one ASIO packet early.
        int requiredSamples = session.AcceptedSamples + signalSampleCount + captureTailSamples;
        await session.WaitForSamplesAsync(requiredSamples, cancellationToken)
            .ConfigureAwait(false);
        float[][] channels = session.CompleteCaptureSnapshot();

        return new AudioCaptureResult(
            channels,
            relativeRouting.MicrophoneChannel,
            relativeRouting.LoopbackChannel,
            StereoSeparationExpected: false,
            AudioCaptureAnomalies.None,
            Diagnostics: null)
        {
            ArrayChannels = relativeRouting.ArrayChannels
        };
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
