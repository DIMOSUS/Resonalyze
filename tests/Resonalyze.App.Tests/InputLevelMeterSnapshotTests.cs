namespace Resonalyze.App.Tests;

public sealed class InputLevelMeterSnapshotTests
{
    [Fact]
    public void Merge_KeepsTheLoudestPeakAndTheNewestRms()
    {
        var superseded = new InputLevelMeterEntry(true, -2, -30, false, false);
        var newest = new InputLevelMeterEntry(true, -24, -26, false, false);

        InputLevelMeterEntry merged = superseded.Merge(newest);

        // The -2 dBFS window never reached the UI on its own; losing it is what
        // makes a peak meter under-read a transient.
        Assert.Equal(-2, merged.PeakDbFs);
        Assert.Equal(-26, merged.RmsDbFs);
    }

    [Fact]
    public void Merge_CarriesFullScaleFlagsForward()
    {
        var superseded = new InputLevelMeterEntry(true, 0, -12, true, false);
        var newest = new InputLevelMeterEntry(true, -18, -24, false, true);

        InputLevelMeterEntry merged = superseded.Merge(newest);

        Assert.True(merged.Clipped);
        Assert.True(merged.FullScaleReference);
    }

    [Fact]
    public void Merge_TakesTheNewestWhenAvailabilityChanges()
    {
        var loud = new InputLevelMeterEntry(true, -1, -9, true, false);

        // A channel that has gone away, then one that has just appeared: in
        // neither direction does the older reading describe the newer channel.
        Assert.Equal(
            InputLevelMeterEntry.Unavailable,
            loud.Merge(InputLevelMeterEntry.Unavailable));
        Assert.Equal(loud, InputLevelMeterEntry.Unavailable.Merge(loud));
    }

    [Fact]
    public void Merge_FoldsBothRowsOfASnapshot()
    {
        var superseded = new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(true, -4, -30, false, false),
            new InputLevelMeterEntry(true, -20, -28, false, false));
        var newest = new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(true, -30, -34, false, false),
            new InputLevelMeterEntry(true, 0, -6, false, true));

        InputLevelMeterSnapshot merged = superseded.Merge(newest);

        Assert.Equal(-4, merged.Microphone.PeakDbFs);
        Assert.Equal(0, merged.Loopback.PeakDbFs);
        Assert.True(merged.Loopback.FullScaleReference);
    }

    [Fact]
    public void Merge_IsAssociativeOverAChainOfDroppedWindows()
    {
        var first = new InputLevelMeterEntry(true, -3, -20, false, false);
        var second = new InputLevelMeterEntry(true, -40, -44, false, false);
        var third = new InputLevelMeterEntry(true, -38, -42, false, false);

        // How the dispatcher folds: each superseded value into the next.
        InputLevelMeterEntry merged = first.Merge(second).Merge(third);

        Assert.Equal(-3, merged.PeakDbFs);
        Assert.Equal(-42, merged.RmsDbFs);
    }
}
