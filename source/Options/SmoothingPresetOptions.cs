using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>The smoothing lists of the settings panels, their labels, and where a stored width lands on them.</summary>
internal static class SmoothingPresetOptions
{
    public static IReadOnlyList<int> SupportedInverseOctaves { get; } =
    [
        0,
        1,
        2,
        3,
        6,
        12,
        24,
        48
    ];

    public static string GetLabel(int inverseOctaves) =>
        inverseOctaves == 0
            ? "Off"
            : SpectrumSmoothing.IsPsychoacoustic(inverseOctaves)
                ? "Psycho"
                : $"1/{inverseOctaves}";

    /// <summary>Phase and GD lists stay width-only: cubic averaging is defined for amplitudes, not signed values.</summary>
    public static IReadOnlyList<int> Offered(bool includePsychoacoustic)
    {
        var offered = new List<int>();
        foreach (int value in SupportedInverseOctaves)
        {
            offered.Add(value);
            if (value == SpectrumSmoothing.PsychoacousticBaseInverseOctaves && includePsychoacoustic)
            {
                offered.Add(SpectrumSmoothing.PsychoacousticCode);
            }
        }

        return offered;
    }

    /// <summary>A list with nothing selected reads as its first width.</summary>
    public static int ReadBack(object? selectedItem) =>
        selectedItem is int inverseOctaves ? inverseOctaves : SupportedInverseOctaves[0];

    /// <summary><paramref name="includePsychoacoustic"/> must match the list: with the item the code is kept (nearest
    /// width would be Off); without it the code decodes to its base width.</summary>
    public static int Normalize(
        double inverseOctaves, bool includePsychoacoustic = true)
    {
        if (SpectrumSmoothing.IsPsychoacoustic(inverseOctaves))
        {
            return includePsychoacoustic
                ? SpectrumSmoothing.PsychoacousticCode
                : SpectrumSmoothing.PsychoacousticBaseInverseOctaves;
        }

        int rounded = (int)Math.Round(inverseOctaves);
        int best = SupportedInverseOctaves[0];
        int bestDistance = Math.Abs(best - rounded);
        foreach (int candidate in SupportedInverseOctaves)
        {
            int distance = Math.Abs(candidate - rounded);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }
}
