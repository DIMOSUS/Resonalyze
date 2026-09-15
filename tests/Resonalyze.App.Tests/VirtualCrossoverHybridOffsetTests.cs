using System.Reflection;
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

        double offset = ResolveOffset([hybrid], [reference]);

        Assert.Equal(7, offset, 6);
    }

    /// <summary>One offset for the set: captures from one session at fixed gain have honest relative levels.</summary>
    [Fact]
    public void TheSetsOffset_IsTheMedianAcrossChannelsAndDoesNotLevelThemSeparately()
    {
        List<SignalPoint> reference = Flat(0);
        List<List<SignalPoint>> hybrids = [Flat(-4), Flat(-5), Flat(-30)];

        double offset = ResolveOffset(
            hybrids.Cast<IReadOnlyList<SignalPoint>>().ToList(),
            [reference, reference, reference]);

        Assert.Equal(5, offset, 6);
    }

    [Fact]
    public void AChannelWithNothingToCompare_IsSkipped()
    {
        List<SignalPoint> reference = Flat(0);
        List<SignalPoint> missing = Flat(double.NaN);

        (List<double> perChannel, double offset) = Resolve(
            [missing, Flat(-9)], [reference, reference]);

        Assert.Equal(9, offset, 6);
        Assert.Equal([9.0], perChannel);
    }

    [Fact]
    public void TheSpread_IsTheDisagreementBetweenChannelsAndNotTheirDistanceFromTheIrs()
    {
        List<SignalPoint> reference = Flat(0);

        (List<double> agreeing, _) = Resolve(
            [Flat(-90), Flat(-90), Flat(-90)], [reference, reference, reference]);
        Assert.Equal(0.0, Spread(agreeing), 6);

        (List<double> mixed, _) = Resolve(
            [Flat(-90), Flat(-90), Flat(-82)], [reference, reference, reference]);
        Assert.Equal(8.0, Spread(mixed), 6);
    }

    /// <summary>Offsets stay in channel order with holes; packed, the spread read-out blamed the wrong driver.</summary>
    [Fact]
    public void AChannelWithNothingToCompare_LeavesAHoleInPlaceAndDoesNotShiftTheRest()
    {
        List<SignalPoint> reference = Flat(0);
        List<SignalPoint> nothing = Flat(double.NaN);

        (double?[] offsets, _) = ResolvePositional(
            [Flat(-90), nothing, Flat(-82)],
            [reference, reference, reference]);

        Assert.Equal(3, offsets.Length);
        Assert.Equal(90.0, offsets[0]!.Value, 6);
        Assert.Null(offsets[1]);
        Assert.Equal(82.0, offsets[2]!.Value, 6);
    }

    private static double Spread(List<double> offsets) =>
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

    private static double ResolveOffset(
        IReadOnlyList<IReadOnlyList<SignalPoint>> hybrids,
        IReadOnlyList<IReadOnlyList<SignalPoint>> references) =>
        Resolve(hybrids, references).SetOffsetDb;

    private static (List<double> PerChannel, double SetOffsetDb) Resolve(
        IReadOnlyList<IReadOnlyList<SignalPoint>> hybrids,
        IReadOnlyList<IReadOnlyList<SignalPoint>> references)
    {
        MethodInfo method = typeof(VirtualCrossoverPanel).GetMethod(
            "ResolveHybridOffsetsDb",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolveHybridOffsetsDb is gone.");
        object? result = method.Invoke(
            null,
            [
                hybrids,
                references
                    .Select(points => new AnalysisCurve("channel", points))
                    .ToList()
            ]);
        (double?[] positional, double setOffset) = ((double?[], double))result!;
        return (
            positional.Where(offset => offset.HasValue)
                .Select(offset => offset!.Value)
                .ToList(),
            setOffset);
    }

    private static (double?[] PerChannel, double SetOffsetDb) ResolvePositional(
        IReadOnlyList<IReadOnlyList<SignalPoint>> hybrids,
        IReadOnlyList<IReadOnlyList<SignalPoint>> references)
    {
        MethodInfo method = typeof(VirtualCrossoverPanel).GetMethod(
            "ResolveHybridOffsetsDb",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolveHybridOffsetsDb is gone.");
        object? result = method.Invoke(
            null,
            [
                hybrids,
                references
                    .Select(points => new AnalysisCurve("channel", points))
                    .ToList()
            ]);
        return ((double?[], double))result!;
    }
}
