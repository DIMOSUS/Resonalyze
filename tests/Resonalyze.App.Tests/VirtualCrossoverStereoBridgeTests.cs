using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The L/R pair a stereo Auto delay bridges the sides on: the one playing the most of 1-2 kHz.</summary>
public sealed class VirtualCrossoverStereoBridgeTests
{
    private static VirtualCrossoverChannel Channel(string name, double? highPassHz, double? lowPassHz)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = 48_000 };
        foreach (bool rightSide in new[] { false, true })
        {
            VirtualCrossoverChannelSettings settings = rightSide ? channel.Pair.Right : channel.Pair.Left;
            settings.CrossoverKind = (highPassHz, lowPassHz) switch
            {
                (not null, not null) => CrossoverKind.BandPass,
                (not null, null) => CrossoverKind.HighPass,
                _ => CrossoverKind.LowPass
            };
            if (highPassHz is { } high)
            {
                settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, high, 24);
            }
            if (lowPassHz is { } low)
            {
                settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, low, 24);
            }
            channel.SideState(rightSide).TransferImpulseResponse = new Complex[64];
        }
        return channel;
    }

    private static string? Bridge(params VirtualCrossoverChannel[] channels)
    {
        (List<VirtualCrossoverSideAlignmentChannel> left, List<VirtualCrossoverSideAlignmentChannel> right) =
            VirtualCrossoverAutoDelay.CollectStereoSides(channels);
        VirtualCrossoverSideAlignmentChannel? bridge = VirtualCrossoverAutoDelay.PickStereoBridge(left, right);
        Assert.True(bridge?.RightSide ?? true);
        return bridge?.Runtime.Name;
    }

    [Fact]
    public void ThreeWayFront_BridgesOnTheMidrange()
    {
        Assert.Equal("Mid", Bridge(
            Channel("Midbass", null, 300),
            Channel("Mid", 300, 3_000),
            Channel("Tweeter", 3_000, null)));
    }

    [Fact]
    public void SidesCrossedApart_BridgeOnThePairPlayingMoreOfTheBandOverBoth()
    {
        // Left crossed at 3 kHz, right at 800 Hz: the woofers score the left's whole octave less the right's third of an
        // octave below 1 kHz, the tweeters the right's octave less the left's 0.58 above 2 kHz; the right side alone picks the tweeter.
        VirtualCrossoverChannel woofer = Channel("Woofer", null, 3_000);
        VirtualCrossoverChannel tweeter = Channel("Tweeter", 3_000, null);
        woofer.Pair.Right.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 800, 24);
        tweeter.Pair.Right.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 800, 24);

        Assert.Equal("Woofer", Bridge(woofer, tweeter));
    }

    [Theory]
    [InlineData(3_000, "Woofer")]
    [InlineData(1_200, "Tweeter")]
    public void TwoWayFront_BridgesOnWhicheverPlaysMoreOfTheBand(double crossoverHz, string expected)
    {
        Assert.Equal(expected, Bridge(
            Channel("Woofer", null, crossoverHz),
            Channel("Tweeter", crossoverHz, null)));
    }
}
