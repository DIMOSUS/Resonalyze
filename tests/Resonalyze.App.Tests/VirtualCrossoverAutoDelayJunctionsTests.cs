using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The junctions Auto delay walks: one between every two neighbours, by the panel's own rule for a handover.</summary>
public sealed class VirtualCrossoverAutoDelayJunctionsTests
{
    private static AlignmentSnapshot Snapshot(string name, CrossoverKind kind, double cornerHz, int slope = 24)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = 48_000 };
        var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, cornerHz, slope);
        channel.Pair.Left.CrossoverKind = kind;
        if (kind == CrossoverKind.LowPass)
        {
            channel.Pair.Left.LowPassEdge = edge;
        }
        else
        {
            channel.Pair.Left.HighPassEdge = edge;
        }
        return new AlignmentSnapshot(
            new VirtualCrossoverSideAlignmentChannel(channel, false), new Complex[64], 0);
    }

    [Fact]
    public void AdjacentJunctions_PairsNeighboursThatHandOver()
    {
        List<AlignmentJunction> junctions = VirtualCrossoverAutoDelay.AdjacentJunctions(
            [Snapshot("A", CrossoverKind.LowPass, 80), Snapshot("B", CrossoverKind.HighPass, 80)]);

        AlignmentJunction junction = Assert.Single(junctions);
        Assert.Equal(80, junction.CrossoverHz);
    }

    [Fact]
    public void AdjacentJunctions_RefusesTheRunAcrossAHole()
    {
        // Fourth-order slopes at 110 and 290 Hz meet 18 dB down: the panel lists no junction there, and the walk, a
        // chain of junctions, must not align the two filters' tails at the lower corner instead.
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            VirtualCrossoverAutoDelay.AdjacentJunctions(
                [Snapshot("A", CrossoverKind.LowPass, 110), Snapshot("B", CrossoverKind.HighPass, 290)]));

        Assert.Contains("A L and B L do not hand over to each other", refusal.Message);
        Assert.Contains("18 dB down", refusal.Message);
        Assert.Contains("cannot cross a hole", refusal.Message);
    }
}
