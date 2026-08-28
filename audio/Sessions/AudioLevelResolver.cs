namespace Resonalyze.Audio;

/// <summary>
/// Resolves per-channel meter levels onto the microphone / loopback roles a
/// session was opened with, so callers receive levels already keyed by role.
/// The channel indices in <paramref name="routing"/> are relative to the level
/// array the session raises.
/// </summary>
internal static class AudioLevelResolver
{
    public static AudioInputLevels Resolve(
        AudioChannelLevel[] channels,
        AudioCaptureRouting routing)
    {
        AudioChannelLevel microphone = TryGet(channels, routing.MicrophoneChannel)
            ?? default;
        AudioChannelLevel? loopback = routing.LoopbackChannel is int index
            ? TryGet(channels, index)
            : null;
        var array = new AudioChannelLevel[routing.ArrayChannels.Count];
        for (int i = 0; i < array.Length; i++)
        {
            // A channel outside the captured set meters as SILENCE rather than
            // shortening the list: the caller pairs these with its configured
            // microphones by position, and a shorter list would slide every reading
            // onto the wrong microphone.
            //
            // Silence spelled out, not `default`: a default AudioChannelLevel is
            // 0 dBFS, which is not silence but full scale. Nothing draws these yet,
            // so it never showed — a meter wired to them would have painted a
            // missing channel as a channel pinned at the top of the scale, without
            // even the clipping flag to give it away.
            array[i] = TryGet(channels, routing.ArrayChannels[i])
                ?? new AudioChannelLevel(
                    double.NegativeInfinity, double.NegativeInfinity, FullScale: false);
        }

        return new AudioInputLevels(microphone, loopback) { Array = array };
    }

    private static AudioChannelLevel? TryGet(AudioChannelLevel[] channels, int index) =>
        (uint)index < (uint)channels.Length ? channels[index] : null;
}
