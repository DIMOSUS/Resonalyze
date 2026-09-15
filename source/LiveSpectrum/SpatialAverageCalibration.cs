using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Mic correction for a stored spatial average. Three modes: a nullable curve conflated "none" with "the capture's own".</summary>
internal enum SpatialAverageCalibrationMode
{
    Off,

    Own,

    Specific
}

internal readonly record struct SpatialAverageCalibration(
    SpatialAverageCalibrationMode Mode,
    CalibrationFile? Curve)
{
    public static readonly SpatialAverageCalibration Off = new(SpatialAverageCalibrationMode.Off, null);

    public static readonly SpatialAverageCalibration Own = new(SpatialAverageCalibrationMode.Own, null);

    /// <summary>A named curve, or <see cref="Off"/> when the curve is null or empty.</summary>
    public static SpatialAverageCalibration Specific(CalibrationFile? curve) =>
        curve is { HasData: true }
            ? new SpatialAverageCalibration(SpatialAverageCalibrationMode.Specific, curve)
            : Off;

    /// <summary>Same reading: compares mode, and the curve only when applied (record equality compares it by reference).</summary>
    public bool Matches(SpatialAverageCalibration other) =>
        Mode == other.Mode &&
        (Mode != SpatialAverageCalibrationMode.Specific ||
            CalibrationFile.SameCurve(Curve, other.Curve));
}
