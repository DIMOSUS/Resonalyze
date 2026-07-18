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
    /// <paramref name="includePsychoacoustic"/> adds the dip-ignoring
    /// psychoacoustic mode — only combos that smooth MAGNITUDE curves may set
    /// it; phase and group-delay combos must stay width-only, because the
    /// asymmetric floor would bias a signed curve upward.
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

    public static int Normalize(double inverseOctaves)
    {
        // The psychoacoustic code passes through as itself: snapping it to the
        // numerically nearest width would land on "Off". A combo without the
        // psycho item resolves the returned code to no selection, so those
        // combos pre-decode via SpectrumSmoothing.EquivalentInverseOctaves.
        if (SpectrumSmoothing.IsPsychoacoustic(inverseOctaves))
        {
            return SpectrumSmoothing.PsychoacousticCode;
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
