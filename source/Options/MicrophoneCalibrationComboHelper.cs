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
        IReadOnlyList<MicrophoneCalibrationOption> options = BuildOptions(
            selectedMode,
            hasZeroDegreeCalibration,
            hasNinetyDegreeCalibration);
        foreach (MicrophoneCalibrationOption option in options)
        {
            comboBox.Items.Add(option);
        }

        comboBox.SelectedIndex = FindIndex(options, selectedMode);
        comboBox.Enabled = options.Count > 1;
    }

    public static MicrophoneCalibrationMode GetSelectedMode(DarkComboBox comboBox) =>
        comboBox.SelectedItem is MicrophoneCalibrationOption option
            ? option.Mode
            : MicrophoneCalibrationMode.Off;

    // The persisted mode stays selectable even when its file is currently
    // missing. Dropping the entry would land the selection on "Off" and the
    // next apply would silently overwrite the stored preference.
    internal static IReadOnlyList<MicrophoneCalibrationOption> BuildOptions(
        MicrophoneCalibrationMode selectedMode,
        bool hasZeroDegreeCalibration,
        bool hasNinetyDegreeCalibration)
    {
        var options = new List<MicrophoneCalibrationOption>
        {
            new(MicrophoneCalibrationMode.Off, "Off")
        };

        if (hasZeroDegreeCalibration ||
            selectedMode == MicrophoneCalibrationMode.Degrees0)
        {
            options.Add(new MicrophoneCalibrationOption(
                MicrophoneCalibrationMode.Degrees0,
                hasZeroDegreeCalibration ? "0 degrees" : "0 degrees (file missing)"));
        }

        if (hasNinetyDegreeCalibration ||
            selectedMode == MicrophoneCalibrationMode.Degrees90)
        {
            options.Add(new MicrophoneCalibrationOption(
                MicrophoneCalibrationMode.Degrees90,
                hasNinetyDegreeCalibration ? "90 degrees" : "90 degrees (file missing)"));
        }

        return options;
    }

    internal static int FindIndex(
        IReadOnlyList<MicrophoneCalibrationOption> options,
        MicrophoneCalibrationMode selectedMode)
    {
        for (int index = 0; index < options.Count; index++)
        {
            if (options[index].Mode == selectedMode)
            {
                return index;
            }
        }

        return 0;
    }

    internal sealed class MicrophoneCalibrationOption
    {
        public MicrophoneCalibrationOption(
            MicrophoneCalibrationMode mode,
            string displayName)
        {
            Mode = mode;
            DisplayName = displayName;
        }

        public MicrophoneCalibrationMode Mode { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
