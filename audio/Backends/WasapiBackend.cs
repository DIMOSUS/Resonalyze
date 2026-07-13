using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>
/// The WASAPI backend, shared between the Shared and Exclusive modes. Owns
/// endpoint opening, exclusive-format negotiation, capture/render channel
/// validation and diagnostics; the Shared/Exclusive difference is only the
/// <see cref="AudioClientShareMode"/> and whether an explicit device format is
/// required.
/// </summary>
public sealed class WasapiBackend : IAudioBackend
{
    private readonly AudioClientShareMode shareMode;

    private WasapiBackend(AudioBackend id, string displayName, AudioClientShareMode shareMode)
    {
        Descriptor = new AudioBackendDescriptor(
            id,
            displayName,
            AudioBackendCapabilities.StableEndpointIds |
            AudioBackendCapabilities.MultiChannelInput |
            AudioBackendCapabilities.PersistentSession |
            (shareMode == AudioClientShareMode.Exclusive
                ? AudioBackendCapabilities.ExclusiveAccess
                : AudioBackendCapabilities.None));
        this.shareMode = shareMode;
    }

    public static WasapiBackend CreateShared() =>
        new(AudioBackend.WasapiShared, "WASAPI Shared", AudioClientShareMode.Shared);

    public static WasapiBackend CreateExclusive() =>
        new(AudioBackend.WasapiExclusive, "WASAPI Exclusive", AudioClientShareMode.Exclusive);

    public AudioBackendDescriptor Descriptor { get; }

    public async ValueTask<IAudioDuplexSession> OpenDuplexAsync(
        AudioSessionRequest request,
        CancellationToken cancellationToken)
    {
        (WasapiCaptureDevice capture, WasapiPlaybackDevice playback) =
            await BuildDevicesAsync(request).ConfigureAwait(false);
        try
        {
            return new PcmDuplexSession(
                capture,
                playback,
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
        (WasapiCaptureDevice capture, WasapiPlaybackDevice playback) =
            await BuildDevicesAsync(request).ConfigureAwait(false);
        try
        {
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
        string renderEndpointId = request.WasapiRenderEndpointId ??
            throw new InvalidOperationException("WASAPI output endpoint is not selected.");
        int renderChannels = signal.PlaybackChannel == PlaybackChannel.Mono ? 1 : 2;
        WaveFormat? renderFormat = shareMode == AudioClientShareMode.Exclusive
            ? WasapiFormatSupport.CreateDeviceFormat(
                signal.SampleRate,
                signal.BitsPerSample,
                renderChannels)
            : null;
        var playback = new WasapiPlaybackDevice(
            renderEndpointId,
            request.BufferMilliseconds,
            shareMode,
            renderFormat);
        return ValueTask.FromResult<IAudioPlaybackSession>(
            new PcmPlaybackSession(playback, signal));
    }

    public Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken) =>
        PcmWarmup.WarmUpAsync(this, request, cancellationToken);

    private async ValueTask<(WasapiCaptureDevice Capture, WasapiPlaybackDevice Playback)>
        BuildDevicesAsync(AudioSessionRequest request)
    {
        string captureEndpointId = request.WasapiCaptureEndpointId ??
            throw new InvalidOperationException("Select a WASAPI capture endpoint.");
        string renderEndpointId = request.WasapiRenderEndpointId ??
            throw new InvalidOperationException("Select a WASAPI render endpoint.");
        int requiredChannels = CaptureChannelLayout.RequiredWaveInputChannelCount(
            request.Routing.MicrophoneChannel,
            request.Routing.LoopbackChannel);
        int renderChannels = request.PlaybackChannel == PlaybackChannel.Mono ? 1 : 2;

        WaveFormat? captureFormat = null;
        WaveFormat? renderFormat = null;
        if (shareMode == AudioClientShareMode.Exclusive)
        {
            DuplexFormatSupport support = WasapiFormatSupport.CheckExclusive(
                captureEndpointId,
                renderEndpointId,
                request.SampleRate,
                request.BitsPerSample,
                requiredChannels,
                renderChannels);
            if (!support.Supported)
            {
                throw new InvalidOperationException(
                    $"WASAPI Exclusive format {request.SampleRate} Hz / {request.BitsPerSample}-bit " +
                    $"is not supported by both endpoints (capture: {support.CaptureSupported}, " +
                    $"render: {support.RenderSupported}).");
            }
            captureFormat = WasapiFormatSupport.CreateDeviceFormat(
                request.SampleRate, request.BitsPerSample, requiredChannels);
            renderFormat = WasapiFormatSupport.CreateDeviceFormat(
                request.SampleRate, request.BitsPerSample, renderChannels);
        }

        WasapiCaptureDevice? createdCapture = null;
        WasapiPlaybackDevice? createdPlayback = null;
        try
        {
            createdCapture = new WasapiCaptureDevice(
                captureEndpointId, request.BufferMilliseconds, shareMode, captureFormat);
            createdPlayback = new WasapiPlaybackDevice(
                renderEndpointId, request.BufferMilliseconds, shareMode, renderFormat);
            ValidateDevices(createdCapture, createdPlayback, requiredChannels, request.SampleRate);
            return (createdCapture, createdPlayback);
        }
        catch
        {
            await DisposeQuietlyAsync(createdPlayback, createdCapture).ConfigureAwait(false);
            throw;
        }
    }

    private void ValidateDevices(
        WasapiCaptureDevice capture,
        WasapiPlaybackDevice playback,
        int requiredChannels,
        int requestedSampleRate)
    {
        if (shareMode == AudioClientShareMode.Shared &&
            capture.CaptureFormat.SampleRate != playback.PlaybackFormat.SampleRate)
        {
            throw new InvalidOperationException(
                "WASAPI Shared capture and render endpoints must use the same mix sample rate. " +
                $"Capture is {capture.CaptureFormat.SampleRate} Hz and render is " +
                $"{playback.PlaybackFormat.SampleRate} Hz.");
        }
        if (shareMode == AudioClientShareMode.Shared &&
            requestedSampleRate != capture.CaptureFormat.SampleRate)
        {
            throw new InvalidOperationException(
                $"WASAPI Shared uses the endpoint mix rate ({capture.CaptureFormat.SampleRate} Hz). " +
                $"Update the measurement sample rate from {requestedSampleRate} Hz.");
        }
        if (capture.ChannelCount < requiredChannels)
        {
            throw new InvalidOperationException(
                $"The WASAPI capture endpoint exposes {capture.ChannelCount} channel(s), " +
                $"but the selected microphone and loopback routing requires {requiredChannels}.");
        }
    }

    private static async ValueTask DisposeQuietlyAsync(
        WasapiPlaybackDevice? playback,
        WasapiCaptureDevice? capture)
    {
        if (playback != null)
        {
            await playback.DisposeAsync().ConfigureAwait(false);
        }
        if (capture != null)
        {
            await capture.DisposeAsync().ConfigureAwait(false);
        }
    }
}
