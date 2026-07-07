namespace Resonalyze;

/// <summary>
/// Combines the level callbacks of two independent recorders (the microphone
/// and a separate loopback device) into one meter snapshot: keeps the latest
/// levels of each so both meters update live whichever device reported last.
/// Shared by the sweep and noise measurements.
/// </summary>
internal sealed class DualDeviceLevelCombiner
{
    private readonly object sync = new();
    private AudioChannelLevel[] latestMicrophone = Array.Empty<AudioChannelLevel>();
    private AudioChannelLevel[] latestLoopback = Array.Empty<AudioChannelLevel>();

    public void Reset()
    {
        lock (sync)
        {
            latestMicrophone = Array.Empty<AudioChannelLevel>();
            latestLoopback = Array.Empty<AudioChannelLevel>();
        }
    }

    public void SetMicrophone(AudioChannelLevel[] channels)
    {
        lock (sync)
        {
            latestMicrophone = channels;
        }
    }

    public void SetLoopback(AudioChannelLevel[] channels)
    {
        lock (sync)
        {
            latestLoopback = channels;
        }
    }

    public InputLevelMeterSnapshot Combine(int microphoneIndex, int? loopbackIndex)
    {
        AudioChannelLevel[] microphoneChannels;
        AudioChannelLevel[] loopbackChannels;
        lock (sync)
        {
            microphoneChannels = latestMicrophone;
            loopbackChannels = latestLoopback;
        }

        InputLevelMeterEntry microphone = InputLevelMapping.CreateEntry(
            InputLevelMapping.TryGetLevel(microphoneChannels, microphoneIndex),
            fullScaleReference: false);
        InputLevelMeterEntry loopback = InputLevelMapping.CreateEntry(
            loopbackIndex.HasValue
                ? InputLevelMapping.TryGetLevel(loopbackChannels, loopbackIndex.Value)
                : null,
            fullScaleReference: true);
        return new InputLevelMeterSnapshot(microphone, loopback);
    }
}
