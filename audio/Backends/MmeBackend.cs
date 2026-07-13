using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>
/// The MME (WaveIn/WaveOut) compatibility backend. Microphone and loopback are
/// two channels of one numbered capture device; playback goes to a numbered
/// render device.
/// </summary>
public sealed class MmeBackend : IAudioBackend
{
    public AudioBackendDescriptor Descriptor { get; } = new(
        AudioBackend.Wave,
        "MME Compatibility",
        AudioBackendCapabilities.None);

    public async ValueTask<IAudioDuplexSession> OpenDuplexAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken)
    {
        IAudioCaptureDevice? capture = null;
        IAudioPlaybackDevice? playback = null;
        try
        {
            capture = CreateCapture(request);
            playback = new MmePlaybackDevice(request.WaveOutputDeviceNumber);
            return new PcmDuplexSession(
                capture,
                playback,
                signal,
                request.Routing,
                request.ExpectedCaptureSamples,
                Descriptor.Id.ToString(),
                request.BufferMilliseconds);
        }
        catch
        {
            await DisposeQuietlyAsync(playback, capture).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<IAudioStreamingSession> OpenStreamingAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken)
    {
        IAudioCaptureDevice? capture = null;
        IAudioPlaybackDevice? playback = null;
        try
        {
            capture = CreateCapture(request);
            playback = new MmePlaybackDevice(request.WaveOutputDeviceNumber);
            return new PcmStreamingSession(capture, playback, request.Routing);
        }
        catch
        {
            await DisposeQuietlyAsync(playback, capture).ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask<IAudioPlaybackSession> OpenPlaybackAsync(
        AudioSessionRequest request,
        AudioPlaybackSignal signal,
        CancellationToken cancellationToken)
    {
        var playback = new MmePlaybackDevice(request.WaveOutputDeviceNumber);
        return ValueTask.FromResult<IAudioPlaybackSession>(
            new PcmPlaybackSession(playback, signal));
    }

    public Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken) =>
        PcmWarmup.WarmUpAsync(this, request, cancellationToken);

    private static MmeCaptureDevice CreateCapture(AudioSessionRequest request)
    {
        int channelCount = CaptureChannelLayout.RequiredWaveInputChannelCount(
            request.Routing.MicrophoneChannel,
            request.Routing.LoopbackChannel);
        return new MmeCaptureDevice(
            request.WaveInputDeviceNumber,
            new WaveFormat(request.SampleRate, request.BitsPerSample, channelCount));
    }

    // Best-effort rollback of a partially-opened session: every resource is
    // released even if an earlier release throws, and cleanup failures are
    // swallowed so they never mask the primary open/validation exception that
    // the caller is about to rethrow.
    private static ValueTask DisposeQuietlyAsync(
        IAudioPlaybackDevice? playback,
        IAudioCaptureDevice? capture) =>
        AudioBackendCleanup.DisposeQuietlyAsync(playback, capture);
}
