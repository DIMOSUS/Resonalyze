namespace Resonalyze;

/// <summary>A block's spatial-average button: the method the project reads, and whether this channel's capture is
/// attached and readable, which gates the hybrid view.</summary>
internal sealed record VirtualCrossoverChannelAverageReadout(string Text, Color Color, string Tooltip)
{
    /// <param name="resolved">False for a capture the session refers to but could not read.</param>
    public static VirtualCrossoverChannelAverageReadout Read(
        string? title,
        double? integratedSeconds,
        bool resolved,
        VirtualCrossoverSpatialAverageMode mode,
        DateTimeOffset? measuredAtUtc)
    {
        bool present = !string.IsNullOrWhiteSpace(title);
        string label = mode switch
        {
            VirtualCrossoverSpatialAverageMode.MicArray => "Array",
            VirtualCrossoverSpatialAverageMode.MovingMic => "MMM",
            _ => "Avg off"
        };
        string text = mode == VirtualCrossoverSpatialAverageMode.Off
            ? label
            : !present ? label : resolved ? $"{label} ✓" : $"{label} ⚠";
        Color color = !present
            ? UiPalette.TextPrimary
            : resolved ? UiPalette.Success : UiPalette.Warning;
        string newLine = Environment.NewLine;
        string tooltip = !present
            ? mode switch
            {
                VirtualCrossoverSpatialAverageMode.MicArray =>
                    "This channel was measured with one microphone, so the hybrid " +
                    "draws it from that POINT measurement." + newLine + newLine +
                    "Legitimate where a point and an average are the same thing — " +
                    "below the cabin's first mode they are — but its dips are this " +
                    "one spot's, and an equalizer fitted to them is fitted to a " +
                    "place nobody's head occupies." + newLine + newLine +
                    "Click to change the method the project reads.",
                VirtualCrossoverSpatialAverageMode.Off =>
                    "The project draws no spatial average." + newLine + newLine +
                    "Click to change the method it reads.",
                _ =>
                    "No spatial average for this channel." + newLine + newLine +
                    "Click to attach a moving-microphone capture. The hybrid view " +
                    "needs one on every channel that plays."
            }
            : resolved
            ? $"Spatial average: {title}" +
                (integratedSeconds is { } seconds
                    ? $"{newLine}{seconds:0} s integrated"
                    : string.Empty) +
                (measuredAtUtc is { } measured
                    ? $"{newLine}measured {measured.ToLocalTime():g}"
                    : string.Empty) +
                newLine + newLine +
                "Click to replace it, or to detach it."
            : $"Missing spatial average: {title}" + newLine +
                "The session still refers to it, but the file could not be read." +
                newLine + newLine +
                "Click to attach it again, or to detach it.";
        return new VirtualCrossoverChannelAverageReadout(text, color, tooltip);
    }
}
