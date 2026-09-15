namespace Resonalyze;

/// <summary>Single place deciding full-scale flags: mic counts as clipped, loopback as full-scale reference.</summary>
internal static class InputLevelMapping
{
    public static InputLevelMeterSnapshot Map(AudioInputLevels levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return new InputLevelMeterSnapshot(
            CreateEntry(levels.Microphone, fullScaleReference: false),
            CreateEntry(levels.Loopback, fullScaleReference: true));
    }

    public static InputLevelMeterSnapshot Map(
        AudioChannelLevel[] channels,
        int microphoneIndex,
        int? loopbackIndex)
    {
        InputLevelMeterEntry microphone = CreateEntry(
            TryGetLevel(channels, microphoneIndex),
            fullScaleReference: false);
        InputLevelMeterEntry loopback = CreateEntry(
            loopbackIndex.HasValue
                ? TryGetLevel(channels, loopbackIndex.Value)
                : null,
            fullScaleReference: true);
        return new InputLevelMeterSnapshot(microphone, loopback);
    }

    public static InputLevelMeterEntry CreateEntry(
        AudioChannelLevel? level,
        bool fullScaleReference)
    {
        if (level == null)
        {
            return InputLevelMeterEntry.Unavailable;
        }

        AudioChannelLevel value = level.Value;
        return new InputLevelMeterEntry(
            true,
            value.PeakDbFs,
            value.RmsDbFs,
            !fullScaleReference && value.FullScale,
            fullScaleReference && value.FullScale);
    }

    public static AudioChannelLevel? TryGetLevel(
        AudioChannelLevel[] channels,
        int channelIndex)
    {
        return (uint)channelIndex < (uint)channels.Length
            ? channels[channelIndex]
            : null;
    }
}
