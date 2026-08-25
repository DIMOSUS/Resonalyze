using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Which SIDE a hybrid curve is built from. The opposite side's dashed sum is drawn
/// by the same method as the shown side's, so the builder is asked for a side that
/// is not the channel's active one — and a builder that quietly read the active side
/// there would draw one side's tuning under the other's label, at levels close
/// enough to pass for a real L/R difference.
/// </summary>
public sealed class VirtualCrossoverHybridSideTests
{
    [Fact]
    public void TheCurve_ComesFromTheSideAskedForAndNotTheActiveOne()
    {
        VirtualCrossoverChannel channel = new("A");
        channel.PhysicalSideState(false).SampleRate = 48_000;
        channel.PhysicalSideState(true).SampleRate = 48_000;
        channel.PhysicalSideState(false).SpatialAverage = Capture(-20);
        channel.PhysicalSideState(true).SpatialAverage = Capture(-32);
        channel.ActiveRight = false;

        IReadOnlyList<SignalPoint> reference = Grid();

        // No chain on either side, so the curve is the stored average itself and the
        // two sides are told apart by their level alone.
        IReadOnlyList<SignalPoint> left = Build(channel, rightSide: false, reference);
        IReadOnlyList<SignalPoint> right = Build(channel, rightSide: true, reference);

        Assert.Equal(-20, left[left.Count / 2].Y, 3);
        Assert.Equal(-32, right[right.Count / 2].Y, 3);
    }

    /// <summary>
    /// A mono pair has one slot, and both sides must find the capture in it — the
    /// shared subwoofer feeds both sums, so an opposite-side build that went looking
    /// in the empty right slot would drop it out of the other side's sum entirely.
    /// </summary>
    [Fact]
    public void AMonoPair_AnswersWithItsSingleCaptureForBothSides()
    {
        VirtualCrossoverChannel channel = new("Sub");
        channel.Pair.Mono = true;
        channel.PhysicalSideState(false).SampleRate = 48_000;
        channel.PhysicalSideState(false).SpatialAverage = Capture(-14);

        IReadOnlyList<SignalPoint> reference = Grid();

        IReadOnlyList<SignalPoint> left = Build(channel, rightSide: false, reference);
        IReadOnlyList<SignalPoint> right = Build(channel, rightSide: true, reference);

        Assert.Equal(-14, left[left.Count / 2].Y, 3);
        Assert.Equal(-14, right[right.Count / 2].Y, 3);
    }

    private static IReadOnlyList<SignalPoint> Build(
        VirtualCrossoverChannel channel,
        bool rightSide,
        IReadOnlyList<SignalPoint> reference)
    {
        MethodInfo method = typeof(VirtualCrossoverPanel).GetMethod(
            "BuildHybridChannelCurve",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildHybridChannelCurve is gone.");
        object? result = method.Invoke(null, [channel, rightSide, reference]);
        return Assert.IsAssignableFrom<IReadOnlyList<SignalPoint>>(result);
    }

    // A flat capture at a known level, on the same logarithmic grid the reference
    // uses, so the level is what identifies it.
    private static LiveCaptureDocument Capture(double db)
    {
        IReadOnlyList<SignalPoint> grid = Grid();
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = $"{db:0} dB",
            CurveDb = grid.Select(_ => db).ToArray(),
            GridStartHz = grid[0].X,
            GridStopHz = grid[^1].X,
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = LiveAnalysisMode.Mmm,
                SampleRateHz = 48_000
            }
        };
    }

    private static List<SignalPoint> Grid()
    {
        var points = new List<SignalPoint>();
        for (int i = 0; i < 128; i++)
        {
            points.Add(new SignalPoint(20 * Math.Pow(10, 3.0 * i / 127), 0));
        }

        return points;
    }
}
