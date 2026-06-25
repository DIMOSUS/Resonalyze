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
        inverseOctaves == 0 ? "Off" : $"1/{inverseOctaves}";

    public static void Configure(DarkComboBox comboBox)
    {
        comboBox.Items.Clear();
        comboBox.FormattingEnabled = true;
        foreach (int value in SupportedInverseOctaves)
        {
            comboBox.Items.Add(value);
        }

        comboBox.Format -= ComboBoxFormat;
        comboBox.Format += ComboBoxFormat;
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    public static int Normalize(double inverseOctaves)
    {
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
