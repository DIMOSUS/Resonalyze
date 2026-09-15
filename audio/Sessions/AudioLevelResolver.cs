namespace Resonalyze.Audio;

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
            // A missing channel meters as explicit silence (keeps positions); default AudioChannelLevel is 0 dBFS, not silence.
            array[i] = TryGet(channels, routing.ArrayChannels[i])
                ?? new AudioChannelLevel(
                    double.NegativeInfinity, double.NegativeInfinity, FullScale: false);
        }

        return new AudioInputLevels(microphone, loopback) { Array = array };
    }

    private static AudioChannelLevel? TryGet(AudioChannelLevel[] channels, int index) =>
        (uint)index < (uint)channels.Length ? channels[index] : null;
}
