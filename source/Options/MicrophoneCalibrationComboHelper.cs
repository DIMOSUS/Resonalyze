using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal static class MicrophoneCalibrationComboHelper
{
    public static void Configure(
        DarkComboBox comboBox,
        string? selectedCalibrationId,
        IReadOnlyList<MicrophoneCalibrationEntry> entries)
    {
        comboBox.Items.Clear();
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        IReadOnlyList<MicrophoneCalibrationOption> options = BuildOptions(
            selectedCalibrationId,
            entries);
        foreach (MicrophoneCalibrationOption option in options)
        {
            comboBox.Items.Add(option);
        }

        comboBox.SelectedIndex = FindIndex(options, selectedCalibrationId);
        comboBox.Enabled = options.Count > 1;
    }

    public static string? GetSelectedCalibrationId(DarkComboBox comboBox) =>
        comboBox.SelectedItem is MicrophoneCalibrationOption option
            ? option.CalibrationId
            : null;

    // Off plus every configured calibration. An entry whose file is currently
    // missing stays in the list, marked: dropping it would land the selection on
    // "Off" and the next apply would silently overwrite the stored preference.
    // The same reasoning keeps a selection the list no longer holds at all — an
    // entry deleted while a saved project still points at it.
    internal static IReadOnlyList<MicrophoneCalibrationOption> BuildOptions(
        string? selectedCalibrationId,
        IReadOnlyList<MicrophoneCalibrationEntry> entries)
    {
        var options = new List<MicrophoneCalibrationOption>
        {
            new(null, "Off")
        };
        foreach (MicrophoneCalibrationEntry entry in entries)
        {
            options.Add(new MicrophoneCalibrationOption(
                entry.Id,
                entry.Available ? entry.Name : $"{entry.Name} (unavailable)"));
        }

        if (!MicrophoneCalibrationIds.IsOff(selectedCalibrationId) &&
            !entries.Any(entry => IsSame(entry.Id, selectedCalibrationId)))
        {
            options.Add(new MicrophoneCalibrationOption(
                selectedCalibrationId,
                $"{selectedCalibrationId} (missing)"));
        }

        return options;
    }

    internal static int FindIndex(
        IReadOnlyList<MicrophoneCalibrationOption> options,
        string? selectedCalibrationId)
    {
        for (int index = 0; index < options.Count; index++)
        {
            if (IsSame(options[index].CalibrationId, selectedCalibrationId))
            {
                return index;
            }
        }

        return 0;
    }

    private static bool IsSame(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    internal sealed class MicrophoneCalibrationOption
    {
        public MicrophoneCalibrationOption(string? calibrationId, string displayName)
        {
            CalibrationId = calibrationId;
            DisplayName = displayName;
        }

        public string? CalibrationId { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
