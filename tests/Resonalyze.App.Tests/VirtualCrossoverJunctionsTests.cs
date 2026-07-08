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

    [Fact]
    public void BandCenterHz_IsTheGeometricMeanOfTheBand()
    {
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 100, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24)
        };

        Assert.Equal(
            Math.Sqrt(100.0 * 1_000),
            VirtualCrossoverJunctions.BandCenterHz(settings),
            precision: 6);
    }

    [Fact]
    public void OverlapBand_IsAnOctaveEachSideClampedToTheAudioBand()
    {
        Assert.Equal((500, 2_000), VirtualCrossoverJunctions.OverlapBand(1_000));
        // Low side clamps to the 20 Hz floor: 30/2 = 15 -> 20; high side 30*2 = 60.
        Assert.Equal((20, 60), VirtualCrossoverJunctions.OverlapBand(30));
        // High side clamps to the 20 kHz ceiling: 15_000*2 = 30_000 -> 20_000;
        // low side 15_000/2 = 7_500.
        Assert.Equal((7_500, 20_000), VirtualCrossoverJunctions.OverlapBand(15_000));
    }

    [Fact]
    public void GetCrossoverWindow_DefaultsWhenNoChannelIsFiltered()
    {
        Assert.Equal(
            (100, 10_000),
            VirtualCrossoverJunctions.GetCrossoverWindow(
                [new VirtualCrossoverChannelSettings(), new VirtualCrossoverChannelSettings()]));
    }

    [Fact]
    public void GetCrossoverWindow_SpansAnOctaveAroundTheExtremeCorners()
    {
        var low = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 200, 24)
        };
        var high = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 4_000, 24)
        };

        // Lowest corner 200 -> /2 = 100; highest corner 4000 -> *2 = 8000.
        Assert.Equal((100, 8_000), VirtualCrossoverJunctions.GetCrossoverWindow([low, high]));
    }
}
