namespace Resonalyze.Audio;

/// <summary>
/// The shared arithmetic of which input channels a capture needs: how many Wave
/// channels to record and where the ASIO capture window starts and how wide it
/// is, given the routing's microphone, its optional loopback and any array
/// microphones recorded beside them.
/// </summary>
/// <remarks>
/// Every method takes the whole routing rather than the individual channels.
/// The window has to span EVERY channel the session will read, and passing the
/// parts separately is how one of them gets forgotten at a call site: an array
/// microphone left out of the count is not a crash but a session that records
/// too few channels and reports the array as missing.
/// </remarks>
internal static class CaptureChannelLayout
{
    public static int RequiredWaveInputChannelCount(AudioCaptureRouting routing)
    {
        ArgumentNullException.ThrowIfNull(routing);
        int last = routing.MicrophoneChannel;
        if (routing.LoopbackChannel.HasValue)
        {
            last = Math.Max(last, routing.LoopbackChannel.Value);
        }
        foreach (int channel in routing.ArrayChannels)
        {
            last = Math.Max(last, channel);
        }

        return last + 1;
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

    /// <summary>
    /// The same routing with every channel expressed relative to the ASIO capture
    /// window, which starts at <see cref="AsioFirstInputOffset"/> rather than at
    /// the driver's channel zero.
    /// </summary>
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
