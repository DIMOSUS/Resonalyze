namespace Resonalyze.Audio;

/// <summary>
/// Continuous play-and-capture over an ASIO driver for live analysis. Waits on
/// both the user's cancellation and the driver's own stop, so an unplugged
/// device surfaces as an error instead of a frozen run.
/// </summary>
internal sealed class AsioStreamingSession : IAudioStreamingSession
{
    private readonly AsioFullDuplexSession session;
    private readonly int sampleRate;
    private readonly AudioCaptureRouting relativeRouting;
    private FloatArrayWaveStream? stream;
    private bool disposed;

    public AsioStreamingSession(AudioSessionRequest request)
    {
        int mic = request.Routing.MicrophoneChannel;
        int? loopback = request.Routing.LoopbackChannel;
        int firstInputOffset = CaptureChannelLayout.AsioFirstInputOffset(mic, loopback);
        int inputChannelCount = CaptureChannelLayout.AsioInputChannelCount(mic, loopback);
        sampleRate = request.SampleRate;
        relativeRouting = new AudioCaptureRouting(
            mic - firstInputOffset,
            loopback.HasValue ? loopback.Value - firstInputOffset : null);
        session = new AsioFullDuplexSession(
            request.AsioDriverName ?? string.Empty,
            firstInputOffset,
            request.AsioOutputChannelOffset,
            inputChannelCount);
        session.SequenceChannelsReady += HandleSequence;
        session.LevelsAvailable += HandleLevels;
    }

    public event Action<AudioCaptureFrame>? FrameAvailable;
    public event Action<AudioInputLevels>? InputLevelsAvailable;
    // ASIO does not report packet discontinuities; this event never fires.
    public event Action? CaptureDiscontinuity;

    public async Task RunAsync(
        AudioPlaybackSignal loopingSignal,
        int sequenceLength,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(loopingSignal);
        _ = CaptureDiscontinuity;

        session.Sequence = sequenceLength;
        stream = AudioPlaybackStreamFactory.CreateFloat(loopingSignal);
        var loopingProvider = new LoopingWaveProvider(stream);
        try
        {
            await session.StartAsync(
                loopingProvider,
                sampleRate,
                autoStop: false,
                cancellationToken).ConfigureAwait(false);
            Task stopped = session.StoppedAsync();
            Task cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            Task finished = await Task.WhenAny(stopped, cancelled).ConfigureAwait(false);
            await finished.ConfigureAwait(false);
            if (finished == stopped)
            {
                throw new InvalidOperationException(
                    "The ASIO driver stopped unexpectedly (device removed or driver error).");
            }
        }
        finally
        {
            await session.StopAsync().ConfigureAwait(false);
        }
    }

    private void HandleSequence(float[][] sequence)
    {
        FrameAvailable?.Invoke(new AudioCaptureFrame(
            sequence,
            relativeRouting.MicrophoneChannel,
            relativeRouting.LoopbackChannel));
    }

    private void HandleLevels(AudioChannelLevel[] channels)
    {
        InputLevelsAvailable?.Invoke(AudioLevelResolver.Resolve(channels, relativeRouting));
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }
        disposed = true;
        session.SequenceChannelsReady -= HandleSequence;
        session.LevelsAvailable -= HandleLevels;
        session.Dispose();
        stream?.Dispose();
        return ValueTask.CompletedTask;
    }
}
