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
        return new AudioInputLevels(microphone, loopback);
    }

    private static AudioChannelLevel? TryGet(AudioChannelLevel[] channels, int index) =>
        (uint)index < (uint)channels.Length ? channels[index] : null;
}
