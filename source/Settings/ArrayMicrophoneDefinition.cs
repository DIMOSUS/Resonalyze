namespace Resonalyze;

/// <summary>Array microphone for spatial averaging; per backend, since channel numbers differ between ASIO and WASAPI.</summary>
internal sealed class ArrayMicrophoneDefinition
{
    public int ChannelOffset { get; set; }

    /// <summary>Null = uncalibrated, which is allowed: position dominates a spatial average.</summary>
    public string? CalibrationId { get; set; }

    public string? Note { get; set; }

    public ArrayMicrophoneDefinition Clone() => new()
    {
        ChannelOffset = ChannelOffset,
        CalibrationId = CalibrationId,
        Note = Note
    };
}
