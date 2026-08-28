namespace Resonalyze;

/// <summary>
/// One further microphone recorded alongside the measurement one, for spatial
/// averaging.
/// </summary>
/// <remarks>
/// Kept per backend, like the measurement microphone's own channel, because a
/// channel number means a different input on each: ASIO counts the driver's
/// inputs, WASAPI counts an endpoint's mix-format channels, and the two are not
/// interchangeable. Switching backend must not silently point an array
/// microphone at whatever input happens to share its number.
/// </remarks>
internal sealed class ArrayMicrophoneDefinition
{
    /// <summary>The input this microphone is plugged into, backend-relative.</summary>
    public int ChannelOffset { get; set; }

    /// <summary>
    /// The calibration to read this microphone through, by id in the same list
    /// the measurement microphone chooses from; null for an uncalibrated one.
    /// </summary>
    /// <remarks>
    /// Uncalibrated is allowed and is not a defect to be designed out: a
    /// spatial average is dominated by WHERE the microphones stood, and a
    /// nominally flat capsule with no calibration file still says something
    /// true about its position. It is recorded as uncalibrated so the curve can
    /// be read as what it is.
    /// </remarks>
    public string? CalibrationId { get; set; }

    /// <summary>
    /// What the user calls this position — "left ear", "passenger" — shown in
    /// the table and carried into the measurement file. Optional.
    /// </summary>
    public string? Note { get; set; }

    public ArrayMicrophoneDefinition Clone() => new()
    {
        ChannelOffset = ChannelOffset,
        CalibrationId = CalibrationId,
        Note = Note
    };
}
