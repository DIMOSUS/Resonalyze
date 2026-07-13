namespace Resonalyze.Audio;

/// <summary>
/// Which hardware input channels carry the microphone and (optional) loopback
/// reference. The channel indices are backend-relative: Wave/MME use 0/1, WASAPI
/// uses mix-format channel indices, ASIO uses absolute driver input channels.
/// The backend maps these to hardware and reports back where each role landed
/// in the captured channel array.
/// </summary>
public sealed record AudioCaptureRouting(
    int MicrophoneChannel,
    int? LoopbackChannel);

/// <summary>
/// Everything a backend needs to open a capture/render session, expressed
/// without any NAudio type. Backend-specific selection fields are all present;
/// each backend reads only the ones that apply to it (chosen by
/// <see cref="Backend"/> in the registry).
/// </summary>
public sealed record AudioSessionRequest(
    AudioBackend Backend,
    int SampleRate,
    int BitsPerSample,
    PlaybackChannel PlaybackChannel,
    AudioCaptureRouting Routing,
    int BufferMilliseconds = 100,
    // Hint for pre-allocating the capture buffer (sweep length + tail); 0 lets
    // the backend size from the sample rate.
    int ExpectedCaptureSamples = 0,
    // Wave / MME
    int WaveOutputDeviceNumber = -1,
    int WaveInputDeviceNumber = -1,
    // WASAPI
    string? WasapiCaptureEndpointId = null,
    string? WasapiRenderEndpointId = null,
    // ASIO
    string? AsioDriverName = null,
    int AsioOutputChannelOffset = 0);

/// <summary>
/// A prepared mono excitation signal handed to a backend for playback. The
/// backend builds whatever concrete stream (PCM or IEEE float) its device
/// needs from these samples; the caller never sees a wave provider.
/// </summary>
public sealed record AudioPlaybackSignal(
    float[] MonoSamples,
    int SampleRate,
    int BitsPerSample,
    PlaybackChannel PlaybackChannel,
    bool Loop = false)
{
    public int SampleCount => MonoSamples.Length;
}
