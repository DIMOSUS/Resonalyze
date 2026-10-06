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

    [Theory]
    // Second-order corners 1.4 octaves apart are each 9 dB down halfway between them: both drivers still play there.
    [InlineData(CrossoverFilterFamily.Butterworth, 12, 2_500, 6_800, 4_123)]
    // The same spread on fourth-order slopes meets 18 dB down: a hole, and the junction stays named by the corner.
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 24, 110, 290, 110)]
    public void GetPairCrossoverHz_CornersAnOctaveOrMoreApart_HandOverWhereTheSlopesCrossAboveTheFloor(
        CrossoverFilterFamily family, int slope, double lowPassHz, double highPassHz, double expectedHz)
    {
        var lower = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(family, lowPassHz, slope)
        };
        var upper = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(family, highPassHz, slope)
        };

        Assert.Equal(expectedHz, VirtualCrossoverJunctions.GetPairCrossoverHz(lower, upper), 0);
    }

    [Theory]
    // Matched corners, and a gap whose slopes cross above the floor, hand over; fourth-order slopes 1.4 octaves apart do not.
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 24, 80, 80, true)]
    [InlineData(CrossoverFilterFamily.Butterworth, 12, 2_500, 6_800, true)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 24, 110, 290, false)]
    // First-order slopes three octaves apart cross at 283 Hz, 9.5 dB down: a handover, although the upper driver's
    // nominal band starts well above the octave around that crossing.
    [InlineData(CrossoverFilterFamily.Butterworth, 6, 100, 800, true)]
    public void HandsOver_IsTheOneRuleForAJunctionOrAHole(
        CrossoverFilterFamily family, int slope, double lowPassHz, double highPassHz, bool expected)
    {
        var lower = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(family, lowPassHz, slope)
        };
        var upper = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(family, highPassHz, slope)
        };

        Assert.Equal(expected, VirtualCrossoverJunctions.HandsOver(lower, upper, 96_000));
    }

    [Theory]
    // The gap near Nyquist of GetPairCrossoverHz_AGapNearNyquist: a wide gap is decided by its crossing alone. On a
    // 48 kHz processor the slopes meet 14 dB down, and the octave test must not revive the pair — a high-pass at
    // 20 kHz has no band above its corner, which reads as full range there.
    [InlineData(96_000, true)]
    [InlineData(48_000, false)]
    public void HandsOver_AWideGapIsDecidedByItsCrossingAlone(int processorSampleRateHz, bool expected)
    {
        var lower = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 10_000, 12)
        };
        var upper = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 20_000, 12)
        };

        Assert.Equal(expected, VirtualCrossoverJunctions.HandsOver(lower, upper, processorSampleRateHz));
    }

    [Theory]
    // Second-order corners at 10 and 20 kHz meet 8 dB down on a 96 kHz processor, and 14 dB down on a 48 kHz one,
    // whose high-pass the bilinear warp steepens towards Nyquist: there the gap is a hole.
    [InlineData(96_000, 14_348)]
    [InlineData(48_000, 10_000)]
    public void GetPairCrossoverHz_AGapNearNyquist_IsReadAsTheProcessorRealizesTheSlopes(
        int processorSampleRateHz, double expectedHz)
    {
        var lower = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 10_000, 12)
        };
        var upper = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 20_000, 12)
        };

        Assert.Equal(
            expectedHz,
            VirtualCrossoverJunctions.GetPairCrossoverHz(lower, upper, processorSampleRateHz),
            0);
    }

    private static VirtualCrossoverChannelSettings FirOnly(
        CrossoverKind kind, double cornerHz, FirCrossoverMethod method, int slope)
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, cornerHz, slope);
        var design = new FirCrossoverDesign(kind, edge, edge, method, FirWindow.Blackman, 8, 4_095, 48_000);
        return new VirtualCrossoverChannelSettings { Fir = design.Build(), FirDesign = design };
    }

    [Theory]
    // A FIR's slope is its kernel's. A magnitude design 72 dB/octave steep is no IIR section, and a windowed sinc
    // is a brick wall whatever its edges are labelled: both leave a hole between corners 1.4 octaves apart.
    [InlineData(FirCrossoverMethod.IirMagnitude, 72, 2_500, 2_500)]
    [InlineData(FirCrossoverMethod.WindowedSinc, 12, 2_500, 2_500)]
    // A second-order magnitude over the same corners hands over where the IIR pair does.
    [InlineData(FirCrossoverMethod.IirMagnitude, 12, 4_100, 4_300)]
    public void GetPairCrossoverHz_AcrossAGapBetweenFirCrossovers_ReadsTheKernels(
        FirCrossoverMethod method, int slope, double lowestHz, double highestHz)
    {
        VirtualCrossoverChannelSettings lower = FirOnly(CrossoverKind.LowPass, 2_500, method, slope);
        VirtualCrossoverChannelSettings upper = FirOnly(CrossoverKind.HighPass, 6_800, method, slope);

        Assert.InRange(
            VirtualCrossoverJunctions.GetPairCrossoverHz(lower, upper, 48_000), lowestHz, highestHz);
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
        Assert.Equal((20, 60), VirtualCrossoverJunctions.OverlapBand(30));
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

        Assert.Equal((100, 8_000), VirtualCrossoverJunctions.GetCrossoverWindow([low, high]));
    }
}
