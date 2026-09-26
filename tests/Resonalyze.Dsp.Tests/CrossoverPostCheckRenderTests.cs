using System.Collections.Concurrent;
using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class CrossoverPostCheckRenderTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void SharedRenders_GiveEveryCandidateThePenaltyItsOwnRendersGive()
    {
        Complex[][] cropped = CrossoverAutoSetup.CropSharedDirectSoundWindow(
        [
            Arrival(2_000, -0.3, 300),
            Arrival(1_990, 0.25, 110),
        ]);
        // One band-pass chain as channel 1 of three candidates and channel 0 of a fourth: a key without the channel hands one
        // of them the other channel's render. Channel 0 takes two gains on one crossover and two crossovers at one gain.
        CrossoverProposal shared = new(CrossoverKind.BandPass, Edge(2_000), Edge(8_000), 0);
        var candidates = new List<CrossoverProposal[]>
        {
            new CrossoverProposal[] { shared, new(CrossoverKind.HighPass, Edge(8_000), null, 0) },
        };
        foreach ((double lowHz, double gainDb) in new[] { (2_000.0, 0.0), (2_000.0, -1.5), (2_500.0, 0.0) })
        {
            candidates.Add([new(CrossoverKind.LowPass, null, Edge(lowHz), gainDb), shared]);
        }

        var renders = new CrossoverAutoSetup.PostCheckRenders(candidates);
        // Arrivals are read off the unprocessed crops, so both paths may share them.
        var arrivals = new ConcurrentDictionary<(int Channel, long BandKey), (double Ms, bool Valid)>();
        // In order: a render let go before its last reader has it is then made twice.
        double[] sharing = [.. candidates.Select(proposals =>
            CrossoverAutoSetup.AchievabilityPenaltyDb(
                cropped, proposals, arrivals, renders, SampleRate, SampleRate))];
        double[] own = [.. candidates.AsParallel().AsOrdered().Select(proposals =>
            CrossoverAutoSetup.AchievabilityPenaltyDb(
                cropped,
                proposals,
                arrivals,
                new CrossoverAutoSetup.PostCheckRenders([proposals]),
                SampleRate,
                SampleRate))];

        Assert.Equal(own.Select(BitConverter.DoubleToInt64Bits), sharing.Select(BitConverter.DoubleToInt64Bits));
        // Eight reads of six distinct chains: channel 1's band-pass three times, each other chain once.
        Assert.Equal(6, renders.Rendered);
        Assert.Equal(0, renders.Held);
    }

    private static CrossoverEdge Edge(double frequencyHz) =>
        new(CrossoverFilterFamily.LinkwitzRiley, frequencyHz, 24);

    private static Complex[] Arrival(int front, double echo, int echoDelay)
    {
        var ir = new Complex[8_192];
        ir[front] = 1.0;
        ir[front + echoDelay] = echo;
        return ir;
    }
}
