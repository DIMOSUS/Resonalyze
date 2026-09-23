using Resonalyze.Dsp;

namespace Resonalyze;

internal readonly record struct AuditionCalibrationNote(string Text, bool Refused);

/// <summary>The calibration a render carries and what the report says about it. See
/// docs/tech/spatial-average.md#audition-render.</summary>
internal static class VirtualCrossoverAuditionCalibration
{
    /// <summary>Only "Own (as measured)" has anything to say; channels read through different curves refuse the render.</summary>
    public static AuditionCalibrationNote? Note(VirtualCrossoverAuditionSession session)
    {
        if (!VirtualCrossoverCalibrationSelection.IsOwn(session.CalibrationId))
        {
            return null;
        }

        VirtualCrossoverAuditionOwnCalibration own = session.Context.OwnCalibration;
        if (own.Conflict is { } conflict)
        {
            return new AuditionCalibrationNote(
                $"REFUSED: {conflict}. Choose one of the calibrations above, or Off.", true);
        }

        return new AuditionCalibrationNote(
            own.Name is { } name
                ? $"Own (as measured): every channel was read through '{name}', and the " +
                    "render carries it."
                : "Own (as measured): the measurements recorded no calibration, so the " +
                    "render carries none.",
            false);
    }

    /// <summary>The curve the render carries and how the result names it; a configured but unreadable file degrades to
    /// Off, and the name says so.</summary>
    public static (CalibrationFile? Curve, string Label) ForRender(VirtualCrossoverAuditionSession session)
    {
        VirtualCrossoverAuditionContext context = session.Context;
        string? calibrationId = session.CalibrationId;
        // "Own" is a rule the calibration list cannot resolve; the panel already resolved it.
        bool own = VirtualCrossoverCalibrationSelection.IsOwn(calibrationId);
        CalibrationFile? calibration = own
            ? context.OwnCalibration.Curve
            : context.CalibrationResolver?.Invoke(calibrationId);
        string label = own
            ? context.OwnCalibration.Name is { } ownName
                ? $"own (as measured): {ownName}"
                : "own (as measured): the measurements recorded none"
            : MicrophoneCalibrationIds.IsOff(calibrationId)
                ? "off"
                : calibration is { HasData: true }
                    ? session.CalibrationName
                    : "off (the calibration file could not be read)";
        return (calibration is { HasData: true } ? calibration : null, label);
    }
}
