using Resonalyze.Dsp;

namespace Resonalyze.Options;

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

    /// <summary>Phase and GD combos stay width-only: cubic averaging is defined for amplitudes, not signed values.</summary>
    public static void Configure(
        DarkComboBox comboBox, bool includePsychoacoustic = false)
    {
        comboBox.Items.Clear();
        comboBox.FormattingEnabled = true;
        foreach (int value in SupportedInverseOctaves)
        {
            comboBox.Items.Add(value);
            if (value == SpectrumSmoothing.PsychoacousticBaseInverseOctaves &&
                includePsychoacoustic)
            {
                comboBox.Items.Add(SpectrumSmoothing.PsychoacousticCode);
            }
        }

        comboBox.Format -= ComboBoxFormat;
        comboBox.Format += ComboBoxFormat;
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    /// <summary><paramref name="includePsychoacoustic"/> must match <see cref="Configure"/>: with the item the code is kept
    /// (nearest width would be Off); without it the code decodes to its base width.</summary>
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

    private static void ComboBoxFormat(object? sender, ListControlConvertEventArgs args)
    {
        if (args.ListItem is int value)
        {
            args.Value = GetLabel(value);
        }
    }
}
