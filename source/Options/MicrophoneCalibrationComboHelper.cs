using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal static class MicrophoneCalibrationComboHelper
{
    public static void Configure(
        ThemedComboBox comboBox,
        string? selectedCalibrationId,
        IReadOnlyList<MicrophoneCalibrationEntry> entries)
    {
        comboBox.Items.Clear();
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        IReadOnlyList<MicrophoneCalibrationOption> options = MicrophoneCalibrationChoices.BuildOptions(
            selectedCalibrationId,
            entries);
        foreach (MicrophoneCalibrationOption option in options)
        {
            comboBox.Items.Add(option);
        }

        comboBox.SelectedIndex = MicrophoneCalibrationChoices.FindIndex(options, selectedCalibrationId);
        comboBox.Enabled = options.Count > 1;
    }

    public static string? GetSelectedCalibrationId(ThemedComboBox comboBox) =>
        comboBox.SelectedItem is MicrophoneCalibrationOption option
            ? option.CalibrationId
            : null;
}
