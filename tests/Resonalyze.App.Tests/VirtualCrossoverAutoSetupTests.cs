using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>What the crossover wizard writes back: one electrical filter per block, on every side the block has.</summary>
public sealed class VirtualCrossoverAutoSetupTests
{
    private static readonly CrossoverEdge HighPass = new(CrossoverFilterFamily.LinkwitzRiley, 300, 24);
    private static readonly CrossoverEdge LowPass = new(CrossoverFilterFamily.Butterworth, 3_000, 18);

    [Fact]
    public void Write_PutsTheProposalOnBothSides_AndClearsEveryRotation()
    {
        var channel = new VirtualCrossoverChannel("B");
        channel.SideSettings(false).PhaseRotationDegrees = 45;
        channel.SideSettings(true).PhaseRotationDegrees = -30;

        int cleared = VirtualCrossoverAutoSetup.Write(
            [channel], [new CrossoverProposal(CrossoverKind.BandPass, HighPass, LowPass, -2.5, InvertPolarity: true)]);

        Assert.Equal(2, cleared);
        foreach (bool rightSide in new[] { false, true })
        {
            VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
            Assert.Equal(CrossoverKind.BandPass, settings.CrossoverKind);
            Assert.Equal(HighPass, settings.HighPassEdge);
            Assert.Equal(LowPass, settings.LowPassEdge);
            Assert.Equal(-2.5, settings.GainDb);
            Assert.True(settings.InvertPolarity);
            Assert.Equal(0, settings.PhaseRotationDegrees);
        }
    }

    [Fact]
    public void Write_TouchesAMonoBlocksHiddenSideNot_AndKeepsAnEdgeTheProposalLeavesOpen()
    {
        var channel = new VirtualCrossoverChannel("A");
        channel.Pair.Mono = true;
        CrossoverEdge keptLowPass = channel.SideSettings(false).LowPassEdge;
        VirtualCrossoverChannelSettings hidden = channel.Pair.Right;
        hidden.PhaseRotationDegrees = 90;

        int cleared = VirtualCrossoverAutoSetup.Write(
            [channel], [new CrossoverProposal(CrossoverKind.HighPass, HighPass, LowPassEdge: null, 0)]);

        Assert.Equal(0, cleared);
        Assert.Equal(HighPass, channel.SideSettings(false).HighPassEdge);
        Assert.Equal(keptLowPass, channel.SideSettings(false).LowPassEdge);
        Assert.Equal(CrossoverKind.Off, hidden.CrossoverKind);
        Assert.Equal(90, hidden.PhaseRotationDegrees);
    }

    [Fact]
    public void Reorder_MovesOnlyTheWizardsBlocks()
    {
        List<VirtualCrossoverChannel> channels =
            [.. new[] { "A", "B", "C", "D" }.Select(name => new VirtualCrossoverChannel(name))];
        // B and D took part; the wizard found D plays below B. A and C keep their slots.
        List<VirtualCrossoverChannel> participating = [channels[1], channels[3]];

        List<int> order = VirtualCrossoverAutoSetup.Reorder(channels, participating, chainOrder: [1, 0]);

        Assert.Equal([0, 3, 2, 1], order);
    }
}
