namespace Resonalyze.Audio;

/// <summary>Takes the whole routing so no call site forgets an array microphone when sizing the capture window.</summary>
internal static class CaptureChannelLayout
{
    public static int RequiredWaveInputChannelCount(AudioCaptureRouting routing)
    {
        ArgumentNullException.ThrowIfNull(routing);
        return routing.RequiredInputChannelCount;
    }

    public static int AsioFirstInputOffset(AudioCaptureRouting routing)
    {
        ArgumentNullException.ThrowIfNull(routing);
        int first = routing.MicrophoneChannel;
        if (routing.LoopbackChannel.HasValue)
        {
            first = Math.Min(first, routing.LoopbackChannel.Value);
        }
        foreach (int channel in routing.ArrayChannels)
        {
            first = Math.Min(first, channel);
        }

        return first;
    }

    public static int AsioInputChannelCount(AudioCaptureRouting routing) =>
        RequiredWaveInputChannelCount(routing) - AsioFirstInputOffset(routing);

    public static AudioCaptureRouting ToAsioRelative(AudioCaptureRouting routing)
    {
        int first = AsioFirstInputOffset(routing);
        return new AudioCaptureRouting(
            routing.MicrophoneChannel - first,
            routing.LoopbackChannel.HasValue ? routing.LoopbackChannel.Value - first : null)
        {
            ArrayChannels = routing.ArrayChannels.Select(channel => channel - first).ToArray()
        };
    }
}
