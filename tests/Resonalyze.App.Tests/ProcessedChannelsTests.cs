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

    // The reference installation: a two-subwoofer front three-way with a rear
    // fill and a centre, both high-passed at 290 Hz with no upper corner.
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
    public void GetAdjacentPairs_RefusesNeighboursWithAHoleBetweenThem()
    {
        // The Rear + Sub view of the reference car: subwoofers stopping at 110 Hz
        // beside a rear fill high-passed at 290. They are neighbours in the
        // ordering and nothing hands a band from one to the other — the octave
        // around the would-be junction (55-220 Hz) is a band the rear does not
        // play at all. Reported as a junction it produced a summation loss and a
        // phase recommendation for a crossover that is not in the car.
        ProcessedChannel sub = Channel("Sub", LowPass(110));
        ProcessedChannel rear = Channel("Rear", HighPass(290));

        Assert.Empty(ProcessedChannels.GetAdjacentPairs(
            ProcessedChannels.OrderByBand([sub, rear])));
    }

    [Fact]
    public void HasJunction_IsFalseForAChainWithNothingCrossing()
    {
        // Rear + Sub on the reference car. Dropping the invented Sub/Rear row is
        // only half the fix: the loss CURVE and its total are computed over the
        // whole window regardless, and a total summation loss for a chain with no
        // handover is still a figure about a crossover that is not in the car.
        Assert.False(ProcessedChannels.HasJunction(
            [Channel("Sub", LowPass(110)), Channel("Rear", HighPass(290))]));

        // A lone channel has nothing to cross with either.
        Assert.False(ProcessedChannels.HasJunction([Channel("Sub", LowPass(110))]));

        // And a real chain still has one, so the rule costs the ordinary case
        // nothing.
        Assert.True(ProcessedChannels.HasJunction(
            [Channel("Sub", LowPass(110)), Channel("Mid", HighPass(110))]));
    }

    [Fact]
    public void IsContinuousChain_SeparatesOneChainFromAChainWithAHoleInIt()
    {
        // The reference car's Rear + Sub view, which the two-channel case above is
        // too simple to represent: TWO subwoofers that genuinely cross (below
        // 50 Hz into 50-110) and then a rear fill from 290 with a hole in front of
        // it. "Has a junction" is true here — Sub1/Sub2 is real — so that
        // predicate alone still let a total summation loss through for a set that
        // is not one chain.
        ProcessedChannel deep = Channel("Sub1", LowPass(50));
        ProcessedChannel sub = Channel("Sub2", BandPass(50, 110));
        ProcessedChannel rear = Channel("Rear", HighPass(290));

        Assert.True(ProcessedChannels.HasJunction([deep, sub, rear]));
        Assert.False(ProcessedChannels.IsContinuousChain([deep, sub, rear]));
        // The real junction inside it survives — losing the two subwoofers'
        // handover would cost information the tuner wants.
        AdjacentPair pair = Assert.Single(ProcessedChannels.GetAdjacentPairs(
            ProcessedChannels.OrderByBand([deep, sub, rear])));
        Assert.Equal(("Sub1", "Sub2"), (pair.Lower.Channel.Name, pair.Upper.Channel.Name));

        // Drop the rear fill and the same two subwoofers ARE one chain.
        Assert.True(ProcessedChannels.IsContinuousChain([deep, sub]));
        // As is the front stage they normally sit under.
        Assert.True(ProcessedChannels.IsContinuousChain(
            [deep, sub, Channel("MB", BandPass(110, 290)), Channel("Mid", HighPass(290))]));
    }

    [Fact]
    public void GetAdjacentPairs_KeepsDriversCrossedALittleApart()
    {
        // The gap test must not be stricter than the measurement it guards: two
        // drivers deliberately crossed a third of an octave apart still hand over,
        // and both reach well into the octave-each-way window the junction is read
        // across. Only a hole wide enough that one of them is silent there counts.
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
        // A clean channel fronting at ~41.7 ms beside a noise channel whose
        // chain delay manufactured a 25 ms silent prefix. Blind, the prefix
        // inflates the noise record's SNR and the start estimator "finds" a
        // front in the middle of the noise, ~22.5 ms — EARLIER than the clean
        // channel's, so it would take over the shared display anchor. With
        // the channels' valid ranges honored the noise record is refused,
        // falls back to its (late) peak, and the clean front anchors.
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

        // What band order over the WHOLE installation does, and why the view has
        // to narrow it first: a rear fill and a centre high-passed at 290 with no
        // upper corner have a band centre of 2.4 kHz, which lands between the
        // midrange's (1 kHz) and the tweeter's (8.4 kHz). They wedge themselves
        // into the middle of the front chain — inventing a midrange/rear-fill
        // handover at 3.5 kHz and a rear-fill/centre one at 290 — and the front's
        // own midrange/tweeter pair stops being adjacent and is never built.
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

        // Rear + Sub holds the subwoofers' junction and no other: the rear fill
        // starts an octave and a half above where they stop.
        Assert.Equal(
            ["RSub-FSub"],
            Names(ProcessedChannels.JunctionsInView(
                car, VirtualCrossoverGroupView.RearAndSub)));

        // Front + Center draws the centre and sums nothing of it, so it neither
        // pairs with the front stage nor splits it; the subwoofers are not in this
        // view at all, so the chain starts at the midbass.
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
        // The condition the loss read-out already goes silent under: there is no
        // one chain to order, and a merged ordering of two is the defect above.
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

        // The rear fill is tuned inside Rear + Sub, and nothing crosses into it
        // there — the subwoofers stop at 110 Hz. It travels alone rather than with
        // a subwoofer whose phase says nothing about it.
        Assert.Equal(
            ["Rear"],
            ProcessedChannels.PhaseNeighbourhood(car, car[5].Channel)
                .Select(item => item.Channel.Name));

        // And a centre sums with nothing at all, in any view.
        Assert.Equal(
            ["Centre"],
            ProcessedChannels.PhaseNeighbourhood(car, car[6].Channel)
                .Select(item => item.Channel.Name));
    }

    /// <summary>
    /// A subwoofer sits under BOTH stages, so a car that crosses it into a rear fill
    /// as well as a front one has two junctions on it. Tuning it saw only the front
    /// one while the rear block, tuned from its own card, saw the subwoofer — an
    /// asymmetry in which one of the two views of one junction was missing a driver.
    /// </summary>
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

        // And the chains stay apart: neither stage gains the other as a neighbour,
        // which is the invented handover this whole rule exists to refuse.
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
