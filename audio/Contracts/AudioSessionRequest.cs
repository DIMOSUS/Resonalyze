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
    int? LoopbackChannel)
{
    private readonly IReadOnlyList<int> arrayChannels = [];

    /// <summary>
    /// Further microphones recorded alongside the measurement one, for spatial
    /// averaging. Empty for every measurement that does not use an array.
    /// </summary>
    /// <remarks>
    /// They are channels of the SAME device on purpose, and that is the whole
    /// reason this is a list on the existing routing rather than a second
    /// session: one device means one clock, so the array shares the sweep, the
    /// loopback, the averaging runs and the quality verdict of the measurement
    /// microphone. Nothing here is time-critical — a spatial average is a
    /// magnitude — but being sample-synchronous with the loopback is what lets
    /// each array microphone be read as an honest transfer function instead of a
    /// bare deconvolution, which is what keeps the array in the same measurement
    /// family as the impulse response.
    /// </remarks>
    public IReadOnlyList<int> ArrayChannels
    {
        get => arrayChannels;
        init => arrayChannels = Validate(value);
    }

    private int[] Validate(IReadOnlyList<int>? channels)
    {
        if (channels == null || channels.Count == 0)
        {
            return [];
        }

        var validated = new int[channels.Count];
        for (int i = 0; i < channels.Count; i++)
        {
            int channel = channels[i];
            if (channel < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ArrayChannels),
                    "An array microphone channel cannot be negative.");
            }
            // A duplicate is a configuration mistake with a quiet consequence:
            // the same position would enter the spatial average twice and weigh
            // double, which reads as a perfectly plausible curve. Refuse it here,
            // where the whole set is visible, rather than later where it is not.
            if (channel == MicrophoneChannel || channel == LoopbackChannel)
            {
                throw new ArgumentException(
                    $"Array microphone channel {channel} is already the microphone or loopback channel.",
                    nameof(ArrayChannels));
            }
            for (int j = 0; j < i; j++)
            {
                if (validated[j] == channel)
                {
                    throw new ArgumentException(
                        $"Array microphone channel {channel} is listed twice.",
                        nameof(ArrayChannels));
                }
            }

            validated[i] = channel;
        }

        return validated;
    }
}

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
