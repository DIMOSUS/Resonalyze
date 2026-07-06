namespace Resonalyze;

/// <summary>
/// The shared short frequency rendering for read-outs and previews:
/// "820 Hz", "2.25 kHz".
/// </summary>
internal static class FrequencyText
{
    public static string Format(double frequencyHz) =>
        frequencyHz >= 1_000
            ? $"{frequencyHz / 1_000:0.##} kHz"
            : $"{frequencyHz:0} Hz";
}
