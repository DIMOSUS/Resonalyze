using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>The Live Spectrum settings' pick lists with their labels, and where a stored value lands on them.</summary>
internal static class LiveSpectrumSettingsChoices
{
    // Shared with the settings schema, so a newly offered length is not floored out of the saved file.
    public static IReadOnlyList<int> SequenceLengths => LiveSequenceLengths.Supported;

    public static IReadOnlyList<int> OverlapPercents { get; } = [0, 50, 75];

    public static IReadOnlyList<int> CoherenceLimits { get; } = [0, 10, 20, 25, 30, 40, 50];

    public static IReadOnlyList<(WindowType Value, string Label)> Windows { get; } =
    [
        (WindowType.Hann, "Hann"),
        (WindowType.FlatTop, "Flat Top"),
        (WindowType.BlackmanHarris, "Blackman-Harris"),
        (WindowType.Rectangular, "Rectangular")
    ];

    public static IReadOnlyList<(AveragingSpeed Value, string Label)> Averagings { get; } =
    [
        (AveragingSpeed.Fast, "Fast"),
        (AveragingSpeed.Medium, "Medium"),
        (AveragingSpeed.Slow, "Slow"),
        (AveragingSpeed.Infinite, "Infinite")
    ];

    /// <summary>Silent only without a reference, since a transfer function needs an excitation; MMM offers periodic pink
    /// only, whose exact 1/√f spectrum, unlike the Kellett bank, does not move with the sample rate.</summary>
    public static IReadOnlyList<NoiseColor> Signals(bool referenceFree, bool mmm) =>
        mmm
            ? [NoiseColor.PinkPeriodic]
            : referenceFree
                ? [NoiseColor.Silent, NoiseColor.PinkPeriodic, NoiseColor.Pink, NoiseColor.Brown, NoiseColor.White]
                : [NoiseColor.PinkPeriodic, NoiseColor.Pink, NoiseColor.Brown, NoiseColor.White];

    public static string SignalLabel(NoiseColor signal) => signal switch
    {
        NoiseColor.Silent => "Silent",
        NoiseColor.PinkPeriodic => "Pink noise (periodic)",
        NoiseColor.Pink => "Pink noise",
        NoiseColor.Brown => "Brown / red noise",
        _ => "White noise"
    };

    // Shown with its duration: resolution is 2/T (rect) or 4/T (Hann), so 32768 is 341 ms at 96 kHz but 683 ms at 48 kHz.
    public static string SequenceLengthLabel(int length, int sampleRateHz) =>
        sampleRateHz > 0
            ? $"{length} — {1000.0 * length / sampleRateHz:0} ms"
            : $"{length}";

    /// <summary>An overlap or a coherence limit; zero is Off.</summary>
    public static string PercentLabel(int percent) => percent == 0 ? "Off" : $"{percent}%";

    public static T Offered<T>(IReadOnlyList<T> values, T value) =>
        values.Contains(value) ? value : values[0];

    public static T Offered<T>(IReadOnlyList<(T Value, string Label)> choices, T value) =>
        Offered(choices.Select(choice => choice.Value).ToArray(), value);

    /// <summary>The largest entry not above the target, or the first; the list is ascending.</summary>
    public static int Floor(IReadOnlyList<int> ascending, int target)
    {
        int floor = ascending[0];
        foreach (int entry in ascending)
        {
            if (target >= entry)
            {
                floor = entry;
            }
        }

        return floor;
    }
}
