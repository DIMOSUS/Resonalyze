using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class ProcessedChannelsTests
{
    private static ProcessedChannel Channel(string name, VirtualCrossoverChannelSettings settings)
    {
        var channel = new VirtualCrossoverChannel(name) { Pair = { Left = settings } };
        return new ProcessedChannel(channel, [Complex.One], 0, 48_000, OxyColors.White);
    }

    private static ProcessedChannel Channel(
        string name,
        VirtualCrossoverChannelSettings settings,
        VirtualCrossoverZone zone,
        bool shown = true)
    {
        var channel = new VirtualCrossoverChannel(name)
        {
            Pair = { Left = settings, Zone = zone, ShowProcessedCurve = shown }
        };
        return new ProcessedChannel(channel, [Complex.One], 0, 48_000, OxyColors.White);
    }

    // Two-subwoofer front three-way with rear fill and centre, both high-passed at 290 Hz with no upper corner.
    private static List<ProcessedChannel> ReferenceCar(
        bool tweeterShown = true,
        bool midShown = true) =>
    [
        Channel("RSub", LowPass(50), VirtualCrossoverZone.Sub),
        Channel("FSub", BandPass(50, 110), VirtualCrossoverZone.Sub),
        Channel("MB", BandPass(110, 290), VirtualCrossoverZone.Front),
        Channel("Mid", BandPass(290, 3_500), VirtualCrossoverZone.Front, midShown),
        Channel("Tw", HighPass(3_500), VirtualCrossoverZone.Front, tweeterShown),
        Channel("Rear", HighPass(290), VirtualCrossoverZone.Rear),
        Channel("Centre", HighPass(290), VirtualCrossoverZone.Center)
    ];

    private static IEnumerable<string> Names(IEnumerable<AdjacentPair> pairs) =>
        pairs.Select(pair => $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}");

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
    public void GetAdjacentPairs_RefusesNeighboursWithAHoleBetweenThem()
    {
        // Subs stopping at 110 Hz beside a rear fill from 290 Hz are neighbours in order but hand nothing over.
        ProcessedChannel sub = Channel("Sub", LowPass(110));
        ProcessedChannel rear = Channel("Rear", HighPass(290));

        Assert.Empty(ProcessedChannels.GetAdjacentPairs(
            ProcessedChannels.OrderByBand([sub, rear])));
    }

    [Fact]
    public void HasJunction_IsFalseForAChainWithNothingCrossing()
    {
        // The loss curve and total are computed over the whole window, so dropping the Sub/Rear row alone is not enough.
        Assert.False(ProcessedChannels.HasJunction(
            [Channel("Sub", LowPass(110)), Channel("Rear", HighPass(290))]));

        Assert.False(ProcessedChannels.HasJunction([Channel("Sub", LowPass(110))]));

        Assert.True(ProcessedChannels.HasJunction(
            [Channel("Sub", LowPass(110)), Channel("Mid", HighPass(110))]));
    }

    [Fact]
    public void IsContinuousChain_SeparatesOneChainFromAChainWithAHoleInIt()
    {
        // Sub1/Sub2 is a real junction, so HasJunction alone let a total through for a set that is not one chain.
        ProcessedChannel deep = Channel("Sub1", LowPass(50));
        ProcessedChannel sub = Channel("Sub2", BandPass(50, 110));
        ProcessedChannel rear = Channel("Rear", HighPass(290));

        Assert.True(ProcessedChannels.HasJunction([deep, sub, rear]));
        Assert.False(ProcessedChannels.IsContinuousChain([deep, sub, rear]));
        AdjacentPair pair = Assert.Single(ProcessedChannels.GetAdjacentPairs(
            ProcessedChannels.OrderByBand([deep, sub, rear])));
        Assert.Equal(("Sub1", "Sub2"), (pair.Lower.Channel.Name, pair.Upper.Channel.Name));

        Assert.True(ProcessedChannels.IsContinuousChain([deep, sub]));
        Assert.True(ProcessedChannels.IsContinuousChain(
            [deep, sub, Channel("MB", BandPass(110, 290)), Channel("Mid", HighPass(290))]));
    }

    [Fact]
    public void GetAdjacentPairs_KeepsDriversCrossedALittleApart()
    {
        // Drivers crossed a third of an octave apart still hand over; only a hole where one is silent counts.
        ProcessedChannel woofer = Channel("W", LowPass(250));
        ProcessedChannel mid = Channel("M", HighPass(315));

        AdjacentPair pair = Assert.Single(ProcessedChannels.GetAdjacentPairs(
            ProcessedChannels.OrderByBand([woofer, mid])));

        Assert.Equal(("W", "M"), (pair.Lower.Channel.Name, pair.Upper.Channel.Name));
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
    [Fact]
    public void SharedStartAnchorIndex_ReadsEachChannelWithinItsValidRange()
    {
        // The noise channel's 25 ms silent prefix inflates SNR and fakes a front (~22.5 ms) earlier than the clean one (~41.7 ms);
        // honoring valid ranges refuses it.
        var cleanIr = new Complex[8_192];
        cleanIr[2_000] = Complex.One;
        var random = new Random(20_260_724);
        var noise = new Complex[4_096];
        for (int i = 0; i < noise.Length; i++)
        {
            noise[i] = new Complex(random.NextDouble() * 2.0 - 1.0, 0.0);
        }
        Complex[] noisy = VirtualCrossoverAnalysis.ApplyChain(
            noise, new DspChannelChain(DelayMs: 25), 48_000, 48_000,
            out ValidSampleRange noisyRange);

        ProcessedChannel Item(Complex[] ir, ValidSampleRange range)
        {
            var channel = new VirtualCrossoverChannel("x") { SampleRate = 48_000 };
            return new ProcessedChannel(
                channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir),
                48_000, OxyColors.White, range);
        }

        int anchor = ProcessedChannels.SharedStartAnchorIndex(
            [Item(cleanIr, default), Item(noisy, noisyRange)]);

        Assert.InRange(anchor, 1_900, 2_005);
    }

    [Fact]
    public void JunctionsInView_OrdersTheChainWithoutTheOtherGroupsInIt()
    {
        List<ProcessedChannel> car = ReferenceCar();

        // Rear and centre high-passed at 290 Hz with no upper corner have a 2.4 kHz band centre, wedging between Mid and Tw.
        Assert.Equal(
            ["RSub-FSub", "FSub-MB", "MB-Mid", "Mid-Rear", "Rear-Centre", "Centre-Tw"],
            Names(ProcessedChannels.GetAdjacentPairs(ProcessedChannels.OrderByBand(car))));

        Assert.Equal(
            ["RSub-FSub", "FSub-MB", "MB-Mid", "Mid-Tw"],
            Names(ProcessedChannels.JunctionsInView(
                car, VirtualCrossoverGroupView.FrontAndSub)));
    }

    [Fact]
    public void JunctionsInView_ListsEachGroupsOwnChain()
    {
        List<ProcessedChannel> car = ReferenceCar();

        Assert.Equal(
            ["RSub-FSub"],
            Names(ProcessedChannels.JunctionsInView(
                car, VirtualCrossoverGroupView.RearAndSub)));

        Assert.Equal(
            ["MB-Mid", "Mid-Tw"],
            Names(ProcessedChannels.JunctionsInView(
                car, VirtualCrossoverGroupView.FrontAndCenter)));
    }

    [Theory]
    [InlineData(VirtualCrossoverGroupView.Everything)]
    [InlineData(VirtualCrossoverGroupView.GroupsCompared)]
    public void JunctionsInView_IsEmptyWhereTheViewSpansGroups(
        VirtualCrossoverGroupView view)
    {
        Assert.Empty(ProcessedChannels.JunctionsInView(ReferenceCar(), view));
    }

    [Fact]
    public void PhaseNeighbourhood_KeepsOnlyTheDriversTheChannelCrossesWith()
    {
        List<ProcessedChannel> car = ReferenceCar();
        VirtualCrossoverChannel mid = car[3].Channel;

        Assert.Equal(
            ["MB", "Mid", "Tw"],
            ProcessedChannels.PhaseNeighbourhood(car, mid)
                .Select(item => item.Channel.Name));
    }

    [Fact]
    public void PhaseNeighbourhood_TakesTheChannelsOwnZoneChain()
    {
        List<ProcessedChannel> car = ReferenceCar();

        Assert.Equal(
            ["Rear"],
            ProcessedChannels.PhaseNeighbourhood(car, car[5].Channel)
                .Select(item => item.Channel.Name));

        Assert.Equal(
            ["Centre"],
            ProcessedChannels.PhaseNeighbourhood(car, car[6].Channel)
                .Select(item => item.Channel.Name));
    }

    /// <summary>A subwoofer sits under both stages, so it must see both junctions.</summary>
    [Fact]
    public void PhaseNeighbourhood_SeesBothStagesFromASubwoofer()
    {
        List<ProcessedChannel> car =
        [
            Channel("Sub", LowPass(100), VirtualCrossoverZone.Sub),
            Channel("Front", HighPass(100), VirtualCrossoverZone.Front),
            Channel("Rear", HighPass(100), VirtualCrossoverZone.Rear)
        ];

        Assert.Equal(
            ["Sub", "Front", "Rear"],
            ProcessedChannels.PhaseNeighbourhood(car, car[0].Channel)
                .Select(item => item.Channel.Name));

        Assert.Equal(
            ["Sub", "Front"],
            ProcessedChannels.PhaseNeighbourhood(car, car[1].Channel)
                .Select(item => item.Channel.Name));
        Assert.Equal(
            ["Sub", "Rear"],
            ProcessedChannels.PhaseNeighbourhood(car, car[2].Channel)
                .Select(item => item.Channel.Name));
    }

    [Fact]
    public void PhaseNeighbourhood_DropsAHiddenNeighbourAndKeepsAHiddenSelf()
    {
        List<ProcessedChannel> car = ReferenceCar(tweeterShown: false, midShown: false);

        Assert.Equal(
            ["MB", "Mid"],
            ProcessedChannels.PhaseNeighbourhood(car, car[3].Channel)
                .Select(item => item.Channel.Name));
    }
}
