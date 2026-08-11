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

    /// <summary>
    /// Folds this entry into <paramref name="newer"/>, which supersedes it.
    /// Peak and the full-scale flags are maxima over the window they describe,
    /// so they have to survive being coalesced away — a peak that existed only
    /// in a dropped window is exactly what the meter is for. RMS is an average:
    /// only the newest one means anything. An availability change resets the
    /// fold, because levels from a channel that has since gone away say nothing
    /// about the one that replaced it.
    /// </summary>
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

    /// <summary>Folds this snapshot into the one that supersedes it.</summary>
    public InputLevelMeterSnapshot Merge(InputLevelMeterSnapshot newer) => new(
        Microphone.Merge(newer.Microphone),
        Loopback.Merge(newer.Loopback));
}
