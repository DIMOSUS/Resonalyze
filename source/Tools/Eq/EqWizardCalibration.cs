using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The calibration bookkeeping the EQ Wizard needs, kept out of the panel so the
/// preferred-vs-effective distinction is testable without WinForms.
/// </summary>
/// <remarks>
/// Two calibration states co-exist and must not be conflated:
/// <list type="bullet">
/// <item>the user's standing <b>preference</b> for impulse responses (a file-backed
/// 0°/90°/Off), which is what gets persisted;</item>
/// <item>the <b>effective</b> mode of the currently loaded source, which a curve import
/// can force to <see cref="EqWizardCalibrationMode.Own"/> or Off.</item>
/// </list>
/// Persisting the effective mode would let merely loading an RTA overlay (effective
/// becomes Own, serialized as Off) quietly erase the user's saved IR calibration.
/// </remarks>
internal static class EqWizardCalibration
{
    /// <summary>The measurement-layer mode an effective wizard mode maps to.</summary>
    public static MicrophoneCalibrationMode ToMicrophoneMode(EqWizardCalibrationMode mode) =>
        mode switch
        {
            EqWizardCalibrationMode.Degrees0 => MicrophoneCalibrationMode.Degrees0,
            EqWizardCalibrationMode.Degrees90 => MicrophoneCalibrationMode.Degrees90,
            // Off and Own both correct with no file-backed profile at the measurement
            // layer; Own's own correction is applied separately, on the imported curve.
            _ => MicrophoneCalibrationMode.Off
        };

    /// <summary>The wizard mode a persisted file-backed preference restores to.</summary>
    public static EqWizardCalibrationMode FromMicrophoneMode(MicrophoneCalibrationMode mode) =>
        mode switch
        {
            MicrophoneCalibrationMode.Degrees0 => EqWizardCalibrationMode.Degrees0,
            MicrophoneCalibrationMode.Degrees90 => EqWizardCalibrationMode.Degrees90,
            _ => EqWizardCalibrationMode.Off
        };

    /// <summary>
    /// The preference after the user picks <paramref name="chosen"/> in the calibration
    /// selector. A choice made while an impulse response (or nothing) is loaded is a
    /// standing preference for impulse responses and is kept; a choice that only makes
    /// sense for the current curve — Own, or an Off/0°/90° picked against an imported
    /// curve — leaves the impulse-response preference untouched, so returning to an IR
    /// restores it.
    /// </summary>
    public static MicrophoneCalibrationMode UpdatedIrPreference(
        MicrophoneCalibrationMode current,
        EqWizardSourceKind? loadedKind,
        EqWizardCalibrationMode chosen)
    {
        bool appliesToImpulseResponses =
            loadedKind is null or EqWizardSourceKind.ImpulseResponse;
        return appliesToImpulseResponses ? ToMicrophoneMode(chosen) : current;
    }
}
