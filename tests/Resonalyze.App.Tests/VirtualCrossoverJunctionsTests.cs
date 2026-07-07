using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverJunctionsTests
{
    [Fact]
    public void GetChannelBand_UsesCrossoverCorners()
    {
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_500, 24)
        };

        Assert.Equal((80, 2_500), VirtualCrossoverJunctions.GetChannelBand(settings));
    }

    [Fact]
    public void GetChannelBand_FullRangeWithoutCrossover()
    {
        var settings = new VirtualCrossoverChannelSettings();

        Assert.Equal((20, 20_000), VirtualCrossoverJunctions.GetChannelBand(settings));
    }

    [Fact]
    public void GetChannelBand_InvertedCornersFallBackToFullRange()
    {
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_000, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 100, 24)
        };

        Assert.Equal((20, 20_000), VirtualCrossoverJunctions.GetChannelBand(settings));
    }

    [Fact]
    public void GetPairCrossoverHz_PrefersLowerChannelsLowPass()
    {
        var lower = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_800, 24)
        };
        var upper = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_200, 24)
        };

        Assert.Equal(1_800, VirtualCrossoverJunctions.GetPairCrossoverHz(lower, upper));
    }

    [Fact]
    public void GetPairCrossoverHz_FallsBackToUppersHighPass()
    {
        var lower = new VirtualCrossoverChannelSettings();
        var upper = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_200, 24)
        };

        Assert.Equal(2_200, VirtualCrossoverJunctions.GetPairCrossoverHz(lower, upper));
    }

    [Fact]
    public void GetPairCrossoverHz_FilterlessFallbackIsGeometricMeanOfBandCenters()
    {
        var lower = new VirtualCrossoverChannelSettings();
        var upper = new VirtualCrossoverChannelSettings();

        double expected = Math.Sqrt(20.0 * 20_000);
        Assert.Equal(
            expected,
            VirtualCrossoverJunctions.GetPairCrossoverHz(lower, upper),
            precision: 6);
    }
}
