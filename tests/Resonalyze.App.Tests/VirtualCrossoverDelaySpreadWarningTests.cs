namespace Resonalyze.App.Tests;

/// <summary>
/// The live "one driver lags the others" warning. Its whole premise is that the
/// delays it reads are the ones Auto delay had to spend making drivers
/// co-arrive — so it may only be measured over the drivers Auto delay actually
/// aligns to each other.
/// </summary>
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
        // The reference car's own figures, from the run that raised this: the
        // front chain settles inside five milliseconds and the rear fill sits
        // fifteen behind it because that is what was asked for. Read across the
        // whole set, the two look like one driver arriving 17 ms late.
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
        // The case the warning exists for, with a rear fill in the project so
        // the fix cannot pass by silencing everything: the spread is inside the
        // chain, and the chain is what Auto delay stretches.
        VirtualCrossoverChannel[] channels =
        [
            Block("A", VirtualCrossoverZone.Sub, 0.0),
            Block("B", VirtualCrossoverZone.Front, 18.0),
            Block("C", VirtualCrossoverZone.Front, 17.5),
            Block("D", VirtualCrossoverZone.Rear, 30.0)
        ];

        (string Name, double SpreadMs, bool PlacedGroups)? warning =
            VirtualCrossoverPanel.CrossoverSpreadWarning(channels);

        Assert.NotNull(warning);
        Assert.Equal("A", warning!.Value.Name);
        // 18.0, not the 30.0 the rear block would make of it.
        Assert.Equal(18.0, warning.Value.SpreadMs, 3);
        // ...and the detail says so, because the delay table shows that 30.
        Assert.True(warning.Value.PlacedGroups);
    }

    [Fact]
    public void ARearOnlyProjectIsItsOwnChainAndKeepsTheWarning()
    {
        // Nothing to be placed against, so Auto delay walks the rear blocks as
        // the chain - and a spread between them is the same symptom it always
        // was. The warning has to follow the same split the run does rather
        // than the zone name.
        VirtualCrossoverChannel[] channels =
        [
            Block("A", VirtualCrossoverZone.Rear, 0.0),
            Block("B", VirtualCrossoverZone.Rear, 20.0)
        ];

        (string Name, double SpreadMs, bool PlacedGroups)? warning =
            VirtualCrossoverPanel.CrossoverSpreadWarning(channels);

        Assert.NotNull(warning);
        Assert.Equal("A", warning!.Value.Name);
        // Nothing was left out, so the detail keeps the wording it always had.
        Assert.False(warning.Value.PlacedGroups);
    }

    [Fact]
    public void ACentreIsPlacedAgainstTheFrontAndCannotStretchIt()
    {
        // A centre is computed against a settled front stage, so however late
        // it arrives it moves nothing but itself. Reading it as part of the
        // spread would blame the front chain for the centre's own crossover.
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
