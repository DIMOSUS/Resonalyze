namespace Resonalyze;

/// <summary>Which array microphones a run records. The settings, Record Settings and the array dialog all ask here, so a
/// probed rate never meets a channel the device then refuses.</summary>
internal static class ArrayChannelRules
{
    /// <summary>The measurement records these itself; an array microphone set there is dropped.</summary>
    public static bool IsMeasurementInput(int channel, int microphoneChannel, int? loopbackChannel) =>
        channel == microphoneChannel || channel == loopbackChannel;

    /// <summary>In list order: real inputs the measurement does not use, reachable on the device, the first microphone
    /// on each input (a channel used twice would weigh double in the spatial average).</summary>
    public static IReadOnlyList<int> Recorded(
        IEnumerable<ArrayMicrophoneDefinition> microphones,
        int microphoneChannel,
        int? loopbackChannel,
        Func<int, bool> reachable)
    {
        var channels = new List<int>();
        foreach (ArrayMicrophoneDefinition microphone in microphones)
        {
            int channel = microphone.ChannelOffset;
            if (channel >= 0 &&
                !IsMeasurementInput(channel, microphoneChannel, loopbackChannel) &&
                !channels.Contains(channel) &&
                reachable(channel))
            {
                channels.Add(channel);
            }
        }

        return channels;
    }
}
