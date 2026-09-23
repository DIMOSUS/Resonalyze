namespace Resonalyze.Options;

internal static class SmoothingPresetCombo
{
    /// <summary>Fills the list with <see cref="SmoothingPresetOptions.Offered"/>, labelled.</summary>
    public static void FillSmoothingPresets(this ThemedComboBox comboBox, bool includePsychoacoustic = false)
    {
        comboBox.Items.Clear();
        comboBox.FormattingEnabled = true;
        foreach (int value in SmoothingPresetOptions.Offered(includePsychoacoustic))
        {
            comboBox.Items.Add(value);
        }

        comboBox.Format -= ComboBoxFormat;
        comboBox.Format += ComboBoxFormat;
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    private static void ComboBoxFormat(object? sender, ListControlConvertEventArgs args)
    {
        if (args.ListItem is int value)
        {
            args.Value = SmoothingPresetOptions.GetLabel(value);
        }
    }
}
