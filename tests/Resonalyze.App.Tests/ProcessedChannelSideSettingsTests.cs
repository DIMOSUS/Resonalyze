using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class ProcessedChannelSideSettingsTests
{
    // The left side splits at 300 Hz, the right at 2 kHz; the left is shown while the right's responses are read.
    [Fact]
    public void TheOtherSidesJunction_ReadsTheOtherSidesCorners()
    {
        VirtualCrossoverChannel low = Channel("A", CrossoverKind.LowPass, leftHz: 300, rightHz: 2_000);
        VirtualCrossoverChannel high = Channel("B", CrossoverKind.HighPass, leftHz: 300, rightHz: 2_000);

        List<ProcessedChannel> right =
        [
            Processed(low) with { SideSettings = low.SideSettings(true) },
            Processed(high) with { SideSettings = high.SideSettings(true) }
        ];
        List<ProcessedChannel> shown = [Processed(low), Processed(high)];

        Assert.Equal(2_000, Assert.Single(ProcessedChannels.GetAdjacentPairs(ProcessedChannels.OrderByBand(right))).CrossoverHz);
        Assert.Equal(300, Assert.Single(ProcessedChannels.GetAdjacentPairs(ProcessedChannels.OrderByBand(shown))).CrossoverHz);
    }

    private static VirtualCrossoverChannel Channel(string name, CrossoverKind kind, double leftHz, double rightHz)
    {
        var channel = new VirtualCrossoverChannel(name) { ActiveRight = false };
        foreach ((bool side, double hz) in new[] { (false, leftHz), (true, rightHz) })
        {
            VirtualCrossoverChannelSettings settings = channel.SideSettings(side);
            settings.CrossoverKind = kind;
            settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, hz, 24);
            settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, hz, 24);
        }

        return channel;
    }

    private static ProcessedChannel Processed(VirtualCrossoverChannel channel) =>
        new(channel, new System.Numerics.Complex[8], PeakIndex: 0, SampleRate: 48_000, OxyColors.White);
}
