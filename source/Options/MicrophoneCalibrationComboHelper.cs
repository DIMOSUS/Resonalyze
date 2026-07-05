using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal static class MicrophoneCalibrationComboHelper
{
    public static void Configure(
        DarkComboBox comboBox,
        MicrophoneCalibrationMode selectedMode,
        bool hasZeroDegreeCalibration,
        bool hasNinetyDegreeCalibration)
    {
        comboBox.Items.Clear();
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.Items.Add(new MicrophoneCalibrationOption(
            MicrophoneCalibrationMode.Off,
            "Off"));

        if (hasZeroDegreeCalibration)
        {
            comboBox.Items.Add(new MicrophoneCalibrationOption(
                MicrophoneCalibrationMode.Degrees0,
                "0 degrees"));
        }

        if (hasNinetyDegreeCalibration)
        {
            comboBox.Items.Add(new MicrophoneCalibrationOption(
                MicrophoneCalibrationMode.Degrees90,
                "90 degrees"));
        }

        comboBox.SelectedIndex = FindIndex(comboBox, selectedMode);
        comboBox.Enabled = comboBox.Items.Count > 1;
    }

    public static MicrophoneCalibrationMode GetSelectedMode(DarkComboBox comboBox) =>
        comboBox.SelectedItem is MicrophoneCalibrationOption option
            ? option.Mode
            : MicrophoneCalibrationMode.Off;

    private static int FindIndex(
        DarkComboBox comboBox,
        MicrophoneCalibrationMode selectedMode)
    {
        for (int index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is MicrophoneCalibrationOption option &&
                option.Mode == selectedMode)
            {
                return index;
            }
        }

        return 0;
    }

    private sealed class MicrophoneCalibrationOption
    {
        public MicrophoneCalibrationOption(
            MicrophoneCalibrationMode mode,
            string displayName)
        {
            Mode = mode;
            DisplayName = displayName;
        }

        public MicrophoneCalibrationMode Mode { get; }

        private string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
