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

    /// <summary>Peak and full-scale flags are maxima that survive coalescing; RMS takes the newest; an availability change resets.</summary>
    public InputLevelMeterEntry Merge(InputLevelMeterEntry newer) =>
        Available && newer.Available
            ? new InputLevelMeterEntry(
                true,
                Math.Max(PeakDbFs, newer.PeakDbFs),
                newer.RmsDbFs,
                Clipped || newer.Clipped,
                FullScaleReference || newer.FullScaleReference)
            : newer;
}

internal readonly record struct InputLevelMeterSnapshot(
    InputLevelMeterEntry Microphone,
    InputLevelMeterEntry Loopback)
{
    public static InputLevelMeterSnapshot Empty => new(
        InputLevelMeterEntry.Unavailable,
        InputLevelMeterEntry.Unavailable);

    public InputLevelMeterSnapshot Merge(InputLevelMeterSnapshot newer) => new(
        Microphone.Merge(newer.Microphone),
        Loopback.Merge(newer.Loopback));
}
