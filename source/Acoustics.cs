namespace Resonalyze;

/// <summary>
/// Shared physical constants for the delay/distance read-outs, so every tool
/// converts with the same number.
/// </summary>
internal static class Acoustics
{
    /// <summary>
    /// Speed of sound in air at 20 °C. Meters per second and millimeters per
    /// millisecond are numerically identical, so this serves both read-outs.
    /// </summary>
    public const double SpeedOfSoundAt20CMetersPerSecond = 343.2;
}
