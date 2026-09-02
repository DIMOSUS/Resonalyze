using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The question Auto Tune asks when the target level datum sits far from the
/// curve it is about to fit. Pinned: the reading is a median over the window
/// (a dip does not move it), the thresholds, and that Cuts only is warned about
/// a target below the source but not above it — there the auto preamp aligns
/// the curve and nothing is lost.
/// </summary>
public sealed class EqTargetLevelCheckTests
{
    [Fact]
    public void TheReadingIsTheMedianOverTheWindow_AndADipDoesNotMoveIt()
    {
        List<SignalPoint> source = Grid(frequency => -80);
        // A 12 dB notch at 1 kHz, the kind a junction leaves: three grid points.
        source = source
            .Select(point => Math.Abs(point.X - 1_000) < 60 ? point with { Y = -92 } : point)
            .ToList();
        List<SignalPoint> target = Grid(frequency => -76);

        double? offset = EqTargetLevelCheck.TargetAboveSourceDb(source, target, 100, 10_000);

        Assert.Equal(4, offset!.Value, 6);
    }

    [Fact]
    public void OnlyTheWindowCounts_AndAnEmptyWindowReadsAsNothing()
    {
        List<SignalPoint> source = Grid(frequency => frequency < 500 ? -60 : -90);
        List<SignalPoint> target = Grid(frequency => -80);

        Assert.Equal(-20, EqTargetLevelCheck.TargetAboveSourceDb(source, target, 20, 400)!.Value, 6);
        Assert.Equal(10, EqTargetLevelCheck.TargetAboveSourceDb(source, target, 600, 20_000)!.Value, 6);
        Assert.Null(EqTargetLevelCheck.TargetAboveSourceDb(source, target, 30_000, 40_000));
        Assert.Null(EqTargetLevelCheck.TargetAboveSourceDb([], [], 20, 20_000));
    }

    [Fact]
    public void HolesAndMismatchedFrequenciesAreSkipped()
    {
        List<SignalPoint> source = Grid(frequency => -80);
        List<SignalPoint> target = Grid(frequency => -78);
        source[10] = source[10] with { Y = double.NaN };
        // A target built on another grid is not compared point for point.
        target[11] = new SignalPoint(target[11].X * 1.5, 0);

        Assert.Equal(2, EqTargetLevelCheck.TargetAboveSourceDb(source, target, 20, 20_000)!.Value, 6);
    }

    [Theory]
    [InlineData(2.9, false, null)]
    [InlineData(3.0, false, "above")]
    [InlineData(3.0, true, null)]
    [InlineData(-9.9, false, null)]
    [InlineData(-10.0, false, "below")]
    [InlineData(-10.0, true, "below")]
    public void TheWarningFollowsTheThresholds_AndCutsOnlyIsOnlyWarnedBelow(
        double targetAboveSourceDb, bool cutsOnly, string? expected)
    {
        string? warning = EqTargetLevelCheck.Warning(targetAboveSourceDb, cutsOnly, 80, 3_000);

        if (expected == null)
        {
            Assert.Null(warning);
        }
        else
        {
            Assert.Contains($"dB {expected} the source over 80–3000 Hz", warning);
            Assert.EndsWith("Tune anyway?", warning);
        }

        Assert.Null(EqTargetLevelCheck.Warning(null, cutsOnly, 80, 3_000));
    }

    private static List<SignalPoint> Grid(Func<double, double> level)
    {
        var points = new List<SignalPoint>();
        for (double frequency = 20; frequency <= 20_000; frequency *= Math.Pow(2, 1.0 / 24))
        {
            points.Add(new SignalPoint(frequency, level(frequency)));
        }

        return points;
    }
}
