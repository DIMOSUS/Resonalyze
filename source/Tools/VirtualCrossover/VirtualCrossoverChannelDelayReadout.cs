namespace Resonalyze;

/// <summary>A block's delay as the distance sound covers in it, for the Delay tooltip.</summary>
internal static class VirtualCrossoverChannelDelayReadout
{
    private const double MillimetersPerInch = 25.4;

    public static string Tooltip(double delayMs)
    {
        double millimeters = delayMs * Acoustics.SpeedOfSoundAt20CMetersPerSecond;
        return $"= {millimeters:0.#} mm\r\n({millimeters / MillimetersPerInch:0.#} in)\r\nin air";
    }
}
