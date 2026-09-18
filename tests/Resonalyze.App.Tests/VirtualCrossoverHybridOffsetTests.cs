using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Read in each channel's own band: across the stopband a real four-way set came out 73 dB apart.</summary>
public sealed class VirtualCrossoverHybridOffsetTests
{
    [Fact]
    public void TheOffset_IsReadInTheChannelsOwnBandAndNotItsStopband()
    {
        // Measured floors on room/noise, hybrid follows the filter: >100 dB apart in the stopband.
        var reference = new List<SignalPoint>();
        var hybrid = new List<SignalPoint>();
        for (int i = 0; i < 1_000; i++)
        {
            double hz = 20 * Math.Pow(10, 3.0 * i / 999);
            bool passband = i < 60;
            double measured = passband ? -3 + Math.Sin(i / 4.0) : Math.Max(-70, -3 - i);
            double analytic = passband ? measured - 7 : -3 - 7 - i * 1.5;
            reference.Add(new SignalPoint(hz, measured));
            hybrid.Add(new SignalPoint(hz, analytic));
        }

        Assert.Equal(7, SpatialAverageOffsets.ChannelDatumDb(hybrid, reference)!.Value, 6);
    }

    /// <summary>One offset for the set: captures from one session at fixed gain have honest relative levels.</summary>
    [Fact]
    public void TheSetsOffset_IsTheMedianAcrossChannelsAndDoesNotLevelThemSeparately()
    {
        List<SignalPoint> reference = Flat(0);

        List<double> datums =
        [
            .. new[] { Flat(-4), Flat(-5), Flat(-30) }
                .Select(hybrid => SpatialAverageOffsets.ChannelDatumDb(hybrid, reference)!.Value)
        ];

        Assert.Equal(5, SpatialAverageOffsets.Median(datums), 6);
    }

    [Fact]
    public void AChannelWithNothingToCompare_HasNoDatum()
    {
        Assert.Null(SpatialAverageOffsets.ChannelDatumDb(Flat(double.NaN), Flat(0)));
    }

    [Fact]
    public void TheSpread_IsTheDisagreementBetweenChannelsAndNotTheirDistanceFromTheIrs()
    {
        Assert.Equal(0.0, Spread(90, 90, 90), 6);
        Assert.Equal(8.0, Spread(90, 90, 82), 6);
    }

    /// <summary>Offsets stay in channel order with holes; packed, the spread read-out blamed the wrong driver.</summary>
    [Fact]
    public void AChannelWithNothingToCompare_LeavesAHoleInTheSpreadReadOut()
    {
        var hybrid = new HybridMagnitudes([], [], [90.0, null, 82.0], 0);
        List<ProcessedChannel> processed =
        [
            .. new[] { "A", "B", "C" }.Select(name => new ProcessedChannel(
                new VirtualCrossoverChannel(name),
                new System.Numerics.Complex[8],
                PeakIndex: 0,
                SampleRate: 48_000,
                OxyColors.White))
        ];

        VirtualCrossoverWarning? warning =
            new VirtualCrossoverWarnings(new VirtualCrossoverSession())
                .Judge(processed, hybrid, gatePlacement: null);

        Assert.NotNull(warning);
        Assert.Contains($"    A     {90.0:+0.0;-0.0} dB", warning!.Detail);
        Assert.Contains("    B     no overlap to compare", warning.Detail);
        Assert.Contains($"    C     {82.0:+0.0;-0.0} dB", warning.Detail);
    }

    private static double Spread(params double[] offsets) =>
        new HybridMagnitudes(
            [], [], offsets.Select(offset => (double?)offset).ToList(), 0).SpreadDb;

    private static List<SignalPoint> Flat(double db)
    {
        var points = new List<SignalPoint>();
        for (int i = 0; i < 200; i++)
        {
            points.Add(new SignalPoint(20 * Math.Pow(10, 3.0 * i / 199), db));
        }

        return points;
    }
}
