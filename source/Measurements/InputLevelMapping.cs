namespace Resonalyze;

/// <summary>
/// Maps recorded per-channel levels onto the input meter snapshot (microphone +
/// loopback entries) — the one place that decides how a full-scale channel is
/// flagged: the microphone counts as clipped, the loopback as the full-scale
/// reference. Shared by the sweep and noise measurements.
/// </summary>
internal static class InputLevelMapping
{
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
