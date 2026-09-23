using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>The entries a calibration list offers and which one a stored id selects.</summary>
internal static class MicrophoneCalibrationChoices
{
    // Missing or deleted entries stay listed and marked; dropping them would let the next apply overwrite the preference.
    public static IReadOnlyList<MicrophoneCalibrationOption> BuildOptions(
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
                "Deleted calibration (missing)"));
        }

        return options;
    }

    public static int FindIndex(
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
}

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
