namespace Resonalyze;

/// <summary>
/// Builds a backend-neutral <see cref="AudioSessionRequest"/> from the
/// application's persisted audio configuration. The one place that maps the
/// microphone/loopback channel roles onto the backend-relative routing (ASIO
/// uses absolute driver channels; Wave/WASAPI use their own channel indices).
/// </summary>
internal static class AudioSessionRequestBuilder
{
    public static AudioSessionRequest Build(
        AudioBackend backend,
        int sampleRate,
        int bits,
        PlaybackChannel playbackChannel,
        int waveInputChannelOffset,
        int? waveLoopbackInputChannelOffset,
        int asioInputChannelOffset,
        int? asioLoopbackInputChannelOffset,
        int asioOutputChannelOffset,
        int outputDeviceNumber,
        int inputDeviceNumber,
        string? wasapiCaptureEndpointId,
        string? wasapiRenderEndpointId,
        string? asioDriverName,
        int bufferMilliseconds,
        int expectedCaptureSamples,
        IReadOnlyList<int>? arrayInputChannelOffsets = null)
    {
        AudioCaptureRouting routing = backend == AudioBackend.Asio
            ? new AudioCaptureRouting(asioInputChannelOffset, asioLoopbackInputChannelOffset)
            : new AudioCaptureRouting(waveInputChannelOffset, waveLoopbackInputChannelOffset);
        if (arrayInputChannelOffsets is { Count: > 0 })
        {
            routing = routing with { ArrayChannels = arrayInputChannelOffsets };
        }
        return new AudioSessionRequest(
            backend,
            sampleRate,
            bits,
            playbackChannel,
            routing,
            bufferMilliseconds,
            expectedCaptureSamples,
            outputDeviceNumber,
            inputDeviceNumber,
            wasapiCaptureEndpointId,
            wasapiRenderEndpointId,
            asioDriverName,
            asioOutputChannelOffset);
    }
}
