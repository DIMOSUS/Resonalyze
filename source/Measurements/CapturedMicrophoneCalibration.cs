using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The microphone calibration a live capture is taken through, frozen when its run
/// begins: the curve that corrects it, the name a reader is shown, and the id the
/// name came from.
/// </summary>
/// <remarks>
/// One object rather than three arguments because the three must not be resolved at
/// different moments — the curve is what the capture is corrected by, and the name is
/// what the saved file will claim it was corrected by.
/// </remarks>
public sealed record CapturedMicrophoneCalibration(
    string? Id,
    string Name,
    CalibrationFile? Curve)
{
    public static CapturedMicrophoneCalibration None { get; } =
        new(null, string.Empty, null);
}
