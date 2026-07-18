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

    /// <summary>
    /// Fills a smoothing combo with the width presets.
    /// <paramref name="includePsychoacoustic"/> adds psychoacoustic magnitude
    /// smoothing. Phase and group-delay combos must stay width-only because
    /// cubic averaging is defined for amplitudes, not signed values.
    /// </summary>
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

    /// <summary>
    /// Maps a stored smoothing value onto this preset list.
    /// <paramref name="includePsychoacoustic"/> must match the combo's
    /// <see cref="Configure"/> call: a combo WITH the psycho item keeps the
    /// code as itself (snapping it to the numerically nearest width would land
    /// on "Off"), a width-only combo decodes it to the plain base width so the
    /// returned value always resolves to an existing item.
    /// </summary>
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
