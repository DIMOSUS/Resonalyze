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
/// measurement, and a Virtual DSP channel is pinned to the curve its panel renders with,
/// which need not be in the wizard's list at all.
/// </summary>
internal readonly record struct EqWizardCalibrationChoice
{
    private EqWizardCalibrationChoice(bool own, bool pinned, string? calibrationId)
    {
        Own = own;
        Pinned = pinned;
        CalibrationId = calibrationId;
    }

    public static EqWizardCalibrationChoice Off => default;

    /// <summary>The correction the curve was captured with, stored alongside it.</summary>
    public static EqWizardCalibrationChoice OwnCapture => new(true, false, null);

    /// <summary>
    /// The correction the source arrived with (<see cref="EqWizardCurveSource.PinnedCalibration"/>),
    /// applied as it is and not selectable away from.
    /// </summary>
    public static EqWizardCalibrationChoice PinnedToSource => new(false, true, null);

    public static EqWizardCalibrationChoice Microphone(string? calibrationId) =>
        new(false, false, MicrophoneCalibrationIds.Normalize(calibrationId));

    public bool Own { get; }

    public bool Pinned { get; }

    /// <summary>
    /// The configured calibration to apply, or null for Off and for Own — whose
    /// correction comes from the curve, not from the calibration list.
    /// </summary>
    public string? CalibrationId { get; }

    /// <summary>
    /// The measurement-layer selection this choice maps to: Own and Pinned correct
    /// with no configured profile (their correction comes with the source and is
    /// applied separately).
    /// </summary>
    public string? MicrophoneCalibrationId => Own || Pinned ? null : CalibrationId;

    public bool IsOff => !Own && !Pinned && MicrophoneCalibrationIds.IsOff(CalibrationId);
}
