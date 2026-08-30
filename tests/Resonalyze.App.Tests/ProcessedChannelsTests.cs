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

}
