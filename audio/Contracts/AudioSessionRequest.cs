namespace Resonalyze.Audio;

/// <summary>Backend-relative input channels: Wave/MME 0/1, WASAPI mix-format indices, ASIO absolute driver inputs.</summary>
public sealed record AudioCaptureRouting(
    int MicrophoneChannel,
    int? LoopbackChannel)
{
    private readonly IReadOnlyList<int> arrayChannels = [];

    /// <summary>Extra array microphones on the SAME device: one clock keeps them sample-synchronous with the loopback, so each reads as a transfer function.</summary>
    public IReadOnlyList<int> ArrayChannels
    {
        get => arrayChannels;
        init => arrayChannels = Validate(value);
    }

    /// <summary>Input width needed for mic, loopback and array; the settings panel must probe WASAPI support with this same count.</summary>
    public int RequiredInputChannelCount
    {
        get
        {
            int last = MicrophoneChannel;
            if (LoopbackChannel.HasValue)
            {
                last = Math.Max(last, LoopbackChannel.Value);
            }
            foreach (int channel in ArrayChannels)
            {
                last = Math.Max(last, channel);
            }

            return last + 1;
        }
    }

    /// <summary>Value equality over the channel list: the generated record compares array references.</summary>
    public bool Equals(AudioCaptureRouting? other) =>
        other is not null &&
        MicrophoneChannel == other.MicrophoneChannel &&
        LoopbackChannel == other.LoopbackChannel &&
        ArrayChannels.SequenceEqual(other.ArrayChannels);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MicrophoneChannel);
        hash.Add(LoopbackChannel);
        foreach (int channel in ArrayChannels)
        {
            hash.Add(channel);
        }

        return hash.ToHashCode();
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
            // A duplicate would weigh double in the spatial average and still look plausible.
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

public sealed record AudioSessionRequest(
    AudioBackend Backend,
    int SampleRate,
    int BitsPerSample,
    PlaybackChannel PlaybackChannel,
    AudioCaptureRouting Routing,
    int BufferMilliseconds = 100,
    // Preallocation hint for the capture buffer (sweep + tail), not a stop condition; 0 sizes it from the sample rate.
    int ExpectedCaptureSamples = 0,
    int WaveOutputDeviceNumber = -1,
    int WaveInputDeviceNumber = -1,
    string? WasapiCaptureEndpointId = null,
    string? WasapiRenderEndpointId = null,
    string? AsioDriverName = null,
    int AsioOutputChannelOffset = 0);

public sealed record AudioPlaybackSignal(
    float[] MonoSamples,
    int SampleRate,
    int BitsPerSample,
    PlaybackChannel PlaybackChannel,
    bool Loop = false)
{
    public int SampleCount => MonoSamples.Length;
}
