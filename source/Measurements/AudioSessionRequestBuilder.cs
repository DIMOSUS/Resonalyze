namespace Resonalyze;

/// <summary>Maps mic/loopback roles to backend routing (ASIO absolute driver channels; Wave/WASAPI own indices).</summary>
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
