using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Preferred (persisted IR calibration) vs effective (per loaded source) calibration, testable without WinForms.
/// Persisting the effective one would let loading an RTA overlay erase the saved IR calibration.
/// </summary>
internal static class EqWizardCalibration
{
    /// <summary>Only a choice made with an IR (or nothing) loaded updates the IR preference.</summary>
    public static string? UpdatedIrPreference(
        string? current,
        EqWizardSourceKind? loadedKind,
        EqWizardCalibrationChoice chosen)
    {
        bool appliesToImpulseResponses =
            loadedKind is null or EqWizardSourceKind.ImpulseResponse;
        return appliesToImpulseResponses ? chosen.MicrophoneCalibrationId : current;
    }
}

internal readonly record struct EqWizardCalibrationChoice
{
    private EqWizardCalibrationChoice(bool own, bool pinned, string? calibrationId)
    {
        Own = own;
        Pinned = pinned;
        CalibrationId = calibrationId;
    }

    public static EqWizardCalibrationChoice Off => default;

    public static EqWizardCalibrationChoice OwnCapture => new(true, false, null);

    public static EqWizardCalibrationChoice PinnedToSource => new(false, true, null);

    public static EqWizardCalibrationChoice Microphone(string? calibrationId) =>
        new(false, false, MicrophoneCalibrationIds.Normalize(calibrationId));

    public bool Own { get; }

    public bool Pinned { get; }

    /// <summary>Configured calibration id; null for Off, Own and Pinned.</summary>
    public string? CalibrationId { get; }

    public string? MicrophoneCalibrationId => Own || Pinned ? null : CalibrationId;

    public bool IsOff => !Own && !Pinned && MicrophoneCalibrationIds.IsOff(CalibrationId);
}
