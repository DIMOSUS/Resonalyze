using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Calibration frozen at run start as one object, so curve, name and id cannot be resolved at different moments.</summary>
public sealed record CapturedMicrophoneCalibration(
    string? Id,
    string Name,
    CalibrationFile? Curve)
{
    public static CapturedMicrophoneCalibration None { get; } =
        new(null, string.Empty, null);
}
