using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The calibration bookkeeping the EQ Wizard needs, kept out of the panel so the
/// preferred-vs-effective distinction is testable without WinForms.
/// </summary>
/// <remarks>
/// Two calibration states co-exist and must not be conflated:
/// <list type="bullet">
/// <item>the user's standing <b>preference</b> for impulse responses (a configured
/// calibration, by id, or none), which is what gets persisted;</item>
/// <item>the <b>effective</b> choice for the currently loaded source, which a curve
/// import can force to <see cref="EqWizardCalibrationChoice.OwnCapture"/> or Off.</item>
/// </list>
/// Persisting the effective choice would let merely loading an RTA overlay (effective
/// becomes Own, serialized as Off) quietly erase the user's saved IR calibration.
/// </remarks>
internal static class EqWizardCalibration
{
    /// <summary>
    /// The preference after the user picks <paramref name="chosen"/> in the calibration
    /// selector. A choice made while an impulse response (or nothing) is loaded is a
    /// standing preference for impulse responses and is kept; a choice that only makes
    /// sense for the current curve — Own, or a calibration picked against an imported
    /// curve — leaves the impulse-response preference untouched, so returning to an IR
    /// restores it.
    /// </summary>
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

/// <summary>
/// How the microphone correction is applied to a source curve. This is the wizard's own
/// choice rather than a plain calibration id: an imported curve can additionally re-use
/// the correction frozen into it at capture time, which has no meaning for a live
/// measurement.
/// </summary>
internal readonly record struct EqWizardCalibrationChoice
{
    private EqWizardCalibrationChoice(bool own, string? calibrationId)
    {
        Own = own;
        CalibrationId = calibrationId;
    }

    public static EqWizardCalibrationChoice Off => default;

    /// <summary>The correction the curve was captured with, stored alongside it.</summary>
    public static EqWizardCalibrationChoice OwnCapture => new(true, null);

    public static EqWizardCalibrationChoice Microphone(string? calibrationId) =>
        new(false, MicrophoneCalibrationIds.Normalize(calibrationId));

    public bool Own { get; }

    /// <summary>
    /// The configured calibration to apply, or null for Off and for Own — whose
    /// correction comes from the curve, not from the calibration list.
    /// </summary>
    public string? CalibrationId { get; }

    /// <summary>
    /// The measurement-layer selection this choice maps to: Own corrects with no
    /// configured profile (its own correction is applied separately, on the
    /// imported curve).
    /// </summary>
    public string? MicrophoneCalibrationId => Own ? null : CalibrationId;

    public bool IsOff => !Own && MicrophoneCalibrationIds.IsOff(CalibrationId);
}
