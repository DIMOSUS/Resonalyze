using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The opposite side's dashed sum asks for a non-active side; reading the active one would pass for a real L/R difference.</summary>
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

        IReadOnlyList<SignalPoint> left = Build(channel, rightSide: false, reference);
        IReadOnlyList<SignalPoint> right = Build(channel, rightSide: true, reference);

        Assert.Equal(-20, left[left.Count / 2].Y, 3);
        Assert.Equal(-32, right[right.Count / 2].Y, 3);
    }

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

    [Fact]
    public void ThePanelsSmoothing_ReachesTheHybridCurve()
    {
        VirtualCrossoverChannel channel = new("A");
        channel.PhysicalSideState(false).SampleRate = 48_000;
        LiveCaptureDocument document = Capture(-20);
        for (int i = 0; i < document.CurveDb.Length; i++)
        {
            document.CurveDb[i] = -20 + (i % 2 == 0 ? 6 : -6);
        }

        channel.PhysicalSideState(false).SpatialAverage = document;
        IReadOnlyList<SignalPoint> reference = Grid();

        IReadOnlyList<SignalPoint> raw = Build(channel, false, reference);
        IReadOnlyList<SignalPoint> smoothed = Build(channel, false, reference, 6);

        Assert.True(Scatter(raw) > 4, $"the unsmoothed curve scattered only {Scatter(raw):0.0} dB");
        Assert.True(
            Scatter(smoothed) < Scatter(raw) / 2,
            $"smoothing left {Scatter(smoothed):0.0} dB of {Scatter(raw):0.0}");
    }

    private static double Scatter(IReadOnlyList<SignalPoint> points)
    {
        double total = 0;
        int count = 0;
        for (int i = 1; i < points.Count; i++)
        {
            if (double.IsFinite(points[i].Y) && double.IsFinite(points[i - 1].Y))
            {
                total += Math.Abs(points[i].Y - points[i - 1].Y);
                count++;
            }
        }

        return count == 0 ? 0 : total / count;
    }

    private static IReadOnlyList<SignalPoint> Build(
        VirtualCrossoverChannel channel,
        bool rightSide,
        IReadOnlyList<SignalPoint> reference,
        int smoothingCode = 0)
    {
        var reader = new VirtualCrossoverHybrid(new VirtualCrossoverSession
        {
            Project = new VirtualCrossoverProjectFile
            {
                SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MovingMic
            }
        });
        IReadOnlyList<SignalPoint>? result =
            reader.ChannelCurve(channel, rightSide, reference, smoothingCode);
        return Assert.IsAssignableFrom<IReadOnlyList<SignalPoint>>(result);
    }

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
