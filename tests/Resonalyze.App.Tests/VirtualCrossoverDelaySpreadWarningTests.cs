namespace Resonalyze.App.Tests;

/// <summary>Measured only over drivers Auto delay aligns to each other, since the warning reads delays spent on co-arrival.</summary>
public sealed class VirtualCrossoverDelaySpreadWarningTests
{
    private static VirtualCrossoverChannel Block(
        string name, VirtualCrossoverZone zone, double delayMs)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = 48_000 };
        channel.Pair.Zone = zone;
        channel.Pair.Left.DelayMs = delayMs;
        channel.Pair.Right.DelayMs = delayMs;
        return channel;
    }

    [Fact]
    public void ARearFillHeldBackOnPurposeIsNotADriverThatLags()
    {
        // Reference car: front chain within 5 ms, rear fill 15 ms behind by request; across the set it looked like a 17 ms lag.
        VirtualCrossoverChannel[] channels =
        [
            Block("A", VirtualCrossoverZone.Sub, 1.25),
            Block("B", VirtualCrossoverZone.Sub, 0.59),
            Block("C", VirtualCrossoverZone.Front, 1.46),
            Block("D", VirtualCrossoverZone.Front, 4.41),
            Block("E", VirtualCrossoverZone.Front, 4.63),
            Block("F", VirtualCrossoverZone.Rear, 18.01),
            Block("G", VirtualCrossoverZone.Center, 4.19)
        ];

        Assert.Null(VirtualCrossoverPanel.CrossoverSpreadWarning(channels));
    }

    [Fact]
    public void AFrontChainThatReallyIsStretchedStillWarns()
    {
        VirtualCrossoverChannel[] channels =
        [
            Block("A", VirtualCrossoverZone.Sub, 0.0),
            Block("B", VirtualCrossoverZone.Front, 18.0),
            Block("C", VirtualCrossoverZone.Front, 17.5),
            Block("D", VirtualCrossoverZone.Rear, 30.0)
        ];

        (string Name, double SpreadMs, IReadOnlyList<VirtualCrossoverZone> Placed)? warning =
            VirtualCrossoverPanel.CrossoverSpreadWarning(channels);

        Assert.NotNull(warning);
        string note = VirtualCrossoverPanel.ExcludedGroupsNote(warning!.Value.Placed);
        Assert.Equal("A", warning.Value.Name);
        Assert.Equal(18.0, warning.Value.SpreadMs, 3);
        Assert.Equal([VirtualCrossoverZone.Rear], warning.Value.Placed);
        Assert.Contains("The rear fill is not counted", note);
        Assert.DoesNotContain("centre", note);
    }

    [Fact]
    public void TheNoteNamesOnlyTheGroupsTheProjectHas()
    {
        VirtualCrossoverChannel[] channels =
        [
            Block("A", VirtualCrossoverZone.Front, 0.0),
            Block("B", VirtualCrossoverZone.Front, 18.0),
            Block("C", VirtualCrossoverZone.Center, 4.0)
        ];

        (string Name, double SpreadMs, IReadOnlyList<VirtualCrossoverZone> Placed)? warning =
            VirtualCrossoverPanel.CrossoverSpreadWarning(channels);

        Assert.NotNull(warning);
        string note = VirtualCrossoverPanel.ExcludedGroupsNote(warning!.Value.Placed);
        Assert.Contains("The centre is not counted", note);
        Assert.DoesNotContain("rear", note);
        Assert.DoesNotContain("fill offset", note);
    }

    [Fact]
    public void ARearOnlyProjectIsItsOwnChainAndKeepsTheWarning()
    {
        // No front to place against, so the rear is walked as the chain; the warning follows the run's split, not the zone name.
        VirtualCrossoverChannel[] channels =
        [
            Block("A", VirtualCrossoverZone.Rear, 0.0),
            Block("B", VirtualCrossoverZone.Rear, 20.0)
        ];

        (string Name, double SpreadMs, IReadOnlyList<VirtualCrossoverZone> Placed)? warning =
            VirtualCrossoverPanel.CrossoverSpreadWarning(channels);

        Assert.NotNull(warning);
        Assert.Equal("A", warning!.Value.Name);
        Assert.Empty(warning.Value.Placed);
        Assert.Equal(
            string.Empty,
            VirtualCrossoverPanel.ExcludedGroupsNote(warning.Value.Placed));
    }

    [Fact]
    public void ACentreIsPlacedAgainstTheFrontAndCannotStretchIt()
    {
        // A centre is placed against a settled front, so its lateness moves only itself.
        VirtualCrossoverChannel[] channels =
        [
            Block("A", VirtualCrossoverZone.Front, 0.5),
            Block("B", VirtualCrossoverZone.Front, 1.0),
            Block("C", VirtualCrossoverZone.Center, 25.0)
        ];

        Assert.Null(VirtualCrossoverPanel.CrossoverSpreadWarning(channels));
    }

    [Fact]
    public void BypassedChannelsStayOutOfTheSpread()
    {
        VirtualCrossoverChannel[] channels =
        [
            Block("A", VirtualCrossoverZone.Front, 0.0),
            Block("B", VirtualCrossoverZone.Front, 1.0),
            Block("C", VirtualCrossoverZone.Front, 40.0)
        ];
        channels[2].Pair.Bypass = true;

        Assert.Null(VirtualCrossoverPanel.CrossoverSpreadWarning(channels));
    }
}
