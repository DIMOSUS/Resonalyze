using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Which microphone correction a stored spatial average should be read through.
/// </summary>
/// <remarks>
/// Three answers and not two, which is why a nullable curve could not carry it: a
/// null meant both "no correction" and "no curve to name", and the second of those
/// is what a capture of several capsules always looks like. Encoding the user's
/// INTENT in the same field as the curve made "read it as it was measured" collapse
/// into "read it uncalibrated" whenever the measurement beside it happened to name
/// no file.
/// </remarks>
internal enum SpatialAverageCalibrationMode
{
    /// <summary>No correction at all: the capture back at the level it was taken.</summary>
    Off,

    /// <summary>The correction the capture itself carries, whatever it is.</summary>
    Own,

    /// <summary>A named curve in place of the capture's own.</summary>
    Specific
}

/// <summary>
/// A calibration mode with the curve it needs, if it needs one.
/// </summary>
internal readonly record struct SpatialAverageCalibration(
    SpatialAverageCalibrationMode Mode,
    CalibrationFile? Curve)
{
    public static readonly SpatialAverageCalibration Off = new(SpatialAverageCalibrationMode.Off, null);

    public static readonly SpatialAverageCalibration Own = new(SpatialAverageCalibrationMode.Own, null);

    /// <summary>
    /// A named curve, or <see cref="Off"/> when there is none — a selector on "no
    /// calibration" resolves to a null curve, and that is Off rather than a Specific
    /// with nothing in it.
    /// </summary>
    public static SpatialAverageCalibration Specific(CalibrationFile? curve) =>
        curve is { HasData: true }
            ? new SpatialAverageCalibration(SpatialAverageCalibrationMode.Specific, curve)
            : Off;
}
