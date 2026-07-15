using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Characterization tests for the shared <see cref="ProcessedChannels"/> ordering
/// and junction helpers — the band ordering and adjacent-pair junctions the
/// metric read-out and the Auto delay search both read.
/// </summary>
public sealed class ProcessedChannelsTests
{
    private static ProcessedChannel Channel(string name, VirtualCrossoverChannelSettings settings)
    {
        var channel = new VirtualCrossoverChannel(name) { Pair = { Left = settings } };
        return new ProcessedChannel(channel, [Complex.One], 0, OxyColors.White);
    }

    private static VirtualCrossoverChannelSettings LowPass(double hz) => new()
    {
        CrossoverKind = CrossoverKind.LowPass,
        LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, hz, 24)
    };

    private static VirtualCrossoverChannelSettings HighPass(double hz) => new()
    {
        CrossoverKind = CrossoverKind.HighPass,
        HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, hz, 24)
    };

    private static VirtualCrossoverChannelSettings BandPass(double lowHz, double highHz) => new()
    {
        CrossoverKind = CrossoverKind.BandPass,
        HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, lowHz, 24),
        LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, highHz, 24)
    };

    [Fact]
    public void OrderByBand_SortsByBandCenter()
    {
        ProcessedChannel sub = Channel("Sub", LowPass(100));
        ProcessedChannel mid = Channel("Mid", BandPass(100, 2_000));
        ProcessedChannel tweeter = Channel("Tw", HighPass(2_000));

        List<ProcessedChannel> ordered = ProcessedChannels.OrderByBand([tweeter, sub, mid]);

        Assert.Equal(["Sub", "Mid", "Tw"], ordered.Select(item => item.Channel.Name));
    }

    [Fact]
    public void GetAdjacentPairs_PairsNeighboursWithTheirSharedJunction()
    {
        ProcessedChannel sub = Channel("Sub", LowPass(100));
        ProcessedChannel mid = Channel("Mid", BandPass(100, 2_000));
        ProcessedChannel tweeter = Channel("Tw", HighPass(2_000));
        List<ProcessedChannel> byBand = ProcessedChannels.OrderByBand([sub, mid, tweeter]);

        List<AdjacentPair> pairs = ProcessedChannels.GetAdjacentPairs(byBand);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(("Sub", "Mid"), (pairs[0].Lower.Channel.Name, pairs[0].Upper.Channel.Name));
        Assert.Equal(("Mid", "Tw"), (pairs[1].Lower.Channel.Name, pairs[1].Upper.Channel.Name));
        // The junction frequency and band are exactly what the junction helper
        // reports for the pair — the two never disagree.
        double crossover = VirtualCrossoverJunctions.GetPairCrossoverHz(
            sub.Channel.Settings, mid.Channel.Settings);
        Assert.Equal(crossover, pairs[0].CrossoverHz);
        Assert.Equal(
            VirtualCrossoverJunctions.OverlapBand(crossover),
            (pairs[0].BandLowHz, pairs[0].BandHighHz));
    }

    [Fact]
    public void GetAdjacentPairs_IsEmptyForFewerThanTwoChannels()
    {
        Assert.Empty(ProcessedChannels.GetAdjacentPairs([Channel("Sub", LowPass(100))]));
    }

    [Fact]
    public void GetCrossoverWindow_DelegatesToJunctionsOverTheChannelSettings()
    {
        ProcessedChannel low = Channel("Sub", LowPass(200));
        ProcessedChannel high = Channel("Tw", HighPass(4_000));

        Assert.Equal(
            VirtualCrossoverJunctions.GetCrossoverWindow(
                [low.Channel.Settings, high.Channel.Settings]),
            ProcessedChannels.GetCrossoverWindow([low, high]));
    }
}
