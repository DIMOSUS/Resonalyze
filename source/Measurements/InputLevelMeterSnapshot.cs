namespace Resonalyze;

internal readonly record struct InputLevelMeterEntry(
    bool Available,
    double PeakDbFs,
    double RmsDbFs,
    bool Clipped,
    bool FullScaleReference)
{
    public static InputLevelMeterEntry Unavailable => new(
        false,
        double.NegativeInfinity,
        double.NegativeInfinity,
        false,
        false);
}

internal readonly record struct InputLevelMeterSnapshot(
    InputLevelMeterEntry Microphone,
    InputLevelMeterEntry Loopback)
{
    public static InputLevelMeterSnapshot Empty => new(
        InputLevelMeterEntry.Unavailable,
        InputLevelMeterEntry.Unavailable);
}
