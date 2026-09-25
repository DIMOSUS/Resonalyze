using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqTargetLevelCheckTests
{
    [Fact]
    public void AWindowWithNoMeasuredPoint_RefusesTheFit_RatherThanReplacingTheBankWithNothing()
    {
        // The fit returns an empty bank for a window it can read nothing in, and Auto Tune applies what it returns.
        var session = new EqWizardSession();
        session.SetAutoTuneWindow(5_000, 8_000);
        List<SignalPoint> source = EqualizationCurve.LogFrequencyGrid(20, 1_000, 200)
            .Select(hz => new SignalPoint(hz, -40))
            .ToList();
        List<SignalPoint> target = source.Select(point => new SignalPoint(point.X, -41)).ToList();

        string refusal = EqWizardFit.NoMeasuredDataRefusal(session, source, target)!;

        Assert.Contains("5000 Hz - 8000 Hz", refusal);
        Assert.Contains("20 Hz - 1000 Hz", refusal);
        Assert.Contains("Crossover in target", refusal);

        // An overlap of even one point is a fit worth making.
        session.SetAutoTuneWindow(900, 8_000);
        Assert.Null(EqWizardFit.NoMeasuredDataRefusal(session, source, target));
    }

    [Fact]
    public void TheWarningsStatement_KeepsTheLevelsDecimals_AndDropsTheQuestion()
    {
        string warning = "The target sits 3.5 dB above the source over 100–1000 Hz (median). The fit will boost." +
            Environment.NewLine + Environment.NewLine + "Lower the Target Level. Tune anyway?";

        Assert.Equal(
            "The target sits 3.5 dB above the source over 100–1000 Hz (median)",
            EqTargetLevelCheck.Statement(warning));
        Assert.Equal("A single statement", EqTargetLevelCheck.Statement("A single statement."));
        Assert.Contains("(median)", EqTargetLevelCheck.Statement(EqTargetLevelCheck.Warning(3.5, false, 100, 1_000)!));
    }

    [Fact]
    public void AGapInTheSourceInsideTheWindow_IsNotARefusal()
    {
        // Unmeasured octaves inside the window are normal; the fit reads around them.
        var session = new EqWizardSession();
        session.SetAutoTuneWindow(100, 1_000);
        List<SignalPoint> source = EqualizationCurve.LogFrequencyGrid(20, 2_000, 200)
            .Select(hz => new SignalPoint(hz, hz is > 200 and < 400 ? double.NaN : -40))
            .ToList();
        List<SignalPoint> target = source.Select(point => new SignalPoint(point.X, -41)).ToList();

        Assert.Null(EqWizardFit.NoMeasuredDataRefusal(session, source, target));
    }

    [Fact]
    public void TheReadingIsTheMedianOverTheWindow_AndADipDoesNotMoveIt()
    {
        List<SignalPoint> source = Grid(frequency => -80);
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
        target[11] = new SignalPoint(target[11].X * 1.5, 0);

        Assert.Equal(2, EqTargetLevelCheck.TargetAboveSourceDb(source, target, 20, 20_000)!.Value, 6);
    }

    [Theory]
    [InlineData(2.9, false, null)]
    [InlineData(3.0, false, "above")]
    [InlineData(2.9, true, null)]
    [InlineData(3.0, true, "above")]
    [InlineData(-9.9, false, null)]
    [InlineData(-10.0, false, "below")]
    [InlineData(-10.0, true, "below")]
    public void TheWarningFollowsTheThresholds_InBothModes(
        double targetAboveSourceDb, bool bankCannotLift, string? expected)
    {
        string? warning = EqTargetLevelCheck.Warning(targetAboveSourceDb, bankCannotLift, 80, 3_000);

        if (expected == null)
        {
            Assert.Null(warning);
        }
        else
        {
            Assert.Contains($"dB {expected} the source over 80–3000 Hz", warning);
            Assert.EndsWith("Tune anyway?", warning);
        }

        Assert.Null(EqTargetLevelCheck.Warning(null, bankCannotLift, 80, 3_000));
    }

    [Fact]
    public void ATargetAboveTheSource_IsExplainedDifferentlyWhenTheBankCannotLift()
    {
        // Off or refilling caps the preamp at 0 dB and never lifts, so a target above the source is unreachable, not a headroom cost.
        string boosting = EqTargetLevelCheck.Warning(4, bankCannotLift: false, 80, 3_000)!;
        string cutting = EqTargetLevelCheck.Warning(4, bankCannotLift: true, 80, 3_000)!;

        Assert.Contains("boost across the whole window", boosting);
        Assert.Contains("the fit cannot raise the curve", cutting);
        Assert.Contains("Lower the Target Level to the curve", cutting);
        Assert.DoesNotContain("tick Cuts only", boosting);
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
