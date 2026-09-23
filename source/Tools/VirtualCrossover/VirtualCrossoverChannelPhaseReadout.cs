using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What a block's Phase angle builds: the crossover it is stated at, the all-pass corner the device places,
/// and the angle it delivers instead when the corner would go higher than the device places one.</summary>
internal sealed record VirtualCrossoverChannelPhaseReadout(string Text, Color Color)
{
    /// <summary>The channel's own crossover: the low-pass on a subwoofer block, the high-pass on every other one, as
    /// configured even where that filter is off.</summary>
    public static double ReferenceHz(VirtualCrossoverZone zone, double highPassHz, double lowPassHz) =>
        zone == VirtualCrossoverZone.Sub ? lowPassHz : highPassHz;

    public static VirtualCrossoverChannelPhaseReadout Read(double degrees, double referenceHz, int processorRateHz)
    {
        var rotation = new PhaseRotationSpec(degrees, referenceHz);
        if (rotation.IsTransparent)
        {
            return new VirtualCrossoverChannelPhaseReadout($"ref {FormatHz(referenceHz)}", UiPalette.TextDisabled);
        }

        AllPassSpec realized = PhaseRotationControl.Realize(rotation, processorRateHz)!;
        double delivered = PhaseRotationControl.DeliveredDegrees(rotation, processorRateHz);
        bool capped = delivered > degrees + 0.05;
        return capped
            ? new VirtualCrossoverChannelPhaseReadout(
                $"ref {FormatHz(referenceHz)} → {delivered:0.0}° min", UiPalette.Warning)
            : new VirtualCrossoverChannelPhaseReadout(
                $"ref {FormatHz(referenceHz)} → AP2 {FormatHz(realized.FrequencyHz)}", UiPalette.TextSecondary);
    }

    public static string Tooltip(double degrees, double referenceHz, int processorRateHz)
    {
        string newLine = Environment.NewLine;
        string head =
            "The processor's channel Phase control: a second-order all-pass" + newLine +
            "(Q = 1) whose corner the device places so that the phase equals" + newLine +
            "this angle at the channel's own crossover — the low-pass on a" + newLine +
            "subwoofer block, the high-pass on every other one, and the" + newLine +
            "configured value even where that filter is switched off." + newLine +
            $"Steps of {PhaseRotationControl.StepDegrees:0.###}°, up to " +
            $"{PhaseRotationControl.MaximumDegrees:0.###}°." + newLine + newLine +
            "Move the crossover and the same angle becomes a different" + newLine +
            "filter — that is the control's own rule, not a simplification." +
            newLine + newLine;
        var rotation = new PhaseRotationSpec(degrees, referenceHz);
        if (rotation.IsTransparent)
        {
            return head + $"No rotation. The reference would be {FormatHz(referenceHz)}.";
        }

        AllPassSpec realized = PhaseRotationControl.Realize(rotation, processorRateHz)!;
        double delivered = PhaseRotationControl.DeliveredDegrees(rotation, processorRateHz);
        double groupDelayMs = AllPassFilter.GroupDelaySeconds(
            realized, referenceHz, processorRateHz) * 1_000;
        string body =
            $"Reference {FormatHz(referenceHz)}, all-pass corner " +
            $"{FormatHz(realized.FrequencyHz)}," + newLine +
            $"{groupDelayMs:0.000} ms of group delay at the reference.";
        return delivered > degrees + 0.05
            ? head + body + newLine + newLine +
                $"The device will not place a corner above " +
                $"{FormatHz(PhaseRotationControl.MaximumCornerHz(processorRateHz))}," +
                newLine +
                $"so this setting delivers {delivered:0.0}° rather than {degrees:0.###}°." +
                newLine +
                "Every smaller setting delivers the same filter."
            : head + body;
    }

    private static string FormatHz(double frequencyHz) =>
        frequencyHz >= 10_000 ? $"{frequencyHz / 1_000:0.0} kHz" : $"{frequencyHz:0} Hz";
}
