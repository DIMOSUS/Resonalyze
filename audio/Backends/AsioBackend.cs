namespace Resonalyze.Audio;

/// <summary>
/// The ASIO backend. Microphone/loopback are absolute driver input channels;
/// playback targets an output channel pair. Sessions keep the driver open across
/// runs. Warm-up pre-rolls the driver with a low-amplitude signal.
/// </summary>
public sealed class AsioBackend : IAudioBackend
{
    private const double WarmupAmplitude = 0.00025;
    private static readonly TimeSpan PrerollDuration = TimeSpan.FromMilliseconds(2200);

    public AudioBackendDescriptor Descriptor { get; } = new(
        AudioBackend.Asio,
        "ASIO",
        AudioBackendCapabilities.StableEndpointIds |
        AudioBackendCapabilities.MultiChannelInput |
        AudioBackendCapabilities.ExclusiveAccess |
        AudioBackendCapabilities.DriverControlPanel |
        AudioBackendCapabilities.PersistentSession);

    public ValueTask<IAudioDuplexSession> OpenDuplexAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IAudioDuplexSession>(new AsioDuplexSession(request));

    public ValueTask<IAudioStreamingSession> OpenStreamingAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IAudioStreamingSession>(new AsioStreamingSession(request));

    public ValueTask<IAudioPlaybackSession> OpenPlaybackAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IAudioPlaybackSession>(new AsioPlaybackSession(request, signal));

    public async Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken)
    {
        int mic = request.Routing.MicrophoneChannel;
        int? loopback = request.Routing.LoopbackChannel;
        int firstInputOffset = CaptureChannelLayout.AsioFirstInputOffset(mic, loopback);
        int inputChannelCount = CaptureChannelLayout.AsioInputChannelCount(mic, loopback);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(5));

        using var session = new AsioFullDuplexSession(
            request.AsioDriverName ?? string.Empty,
            firstInputOffset,
            request.AsioOutputChannelOffset,
            inputChannelCount);
        using FloatArrayWaveStream warmupSignal = FloatArrayWaveStream.FromMonoSamples(
            CreateWarmupSignal(request.SampleRate),
            request.SampleRate,
            request.PlaybackChannel);
        var loopingWarmup = new LoopingWaveProvider(warmupSignal);

        int prerollSamples = Math.Max(
            1,
            (int)Math.Round(request.SampleRate * PrerollDuration.TotalSeconds));
        await session.StartAsync(
            loopingWarmup,
            request.SampleRate,
            autoStop: false,
            linked.Token,
            expectedTotalSamples: prerollSamples + request.SampleRate).ConfigureAwait(false);
        await session.WaitForSamplesAsync(prerollSamples, linked.Token).ConfigureAwait(false);
        await session.StopAsync().ConfigureAwait(false);
    }

    private static float[] CreateWarmupSignal(int sampleRate)
    {
        int sampleCount = Math.Max(256, sampleRate / 2);
        var samples = new float[sampleCount];
        var random = new Random(42);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)((random.NextDouble() * 2.0 - 1.0) * WarmupAmplitude);
        }

        return samples;
    }
}
