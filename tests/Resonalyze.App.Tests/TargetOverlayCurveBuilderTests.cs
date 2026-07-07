using OxyPlot;

namespace Resonalyze.App.Tests;

public sealed class TargetOverlayCurveBuilderTests
{
    private static readonly TargetCurveSpec FlatSpec =
        TargetCurveSpec.FromPreset(TargetPreset.Flat);

    [Fact]
    public void BuildShape_ReturnsTargetOnTheFullGrid()
    {
        var builder = new TargetOverlayCurveBuilder();

        TargetOverlayShape shape = builder.BuildShape(FlatSpec, offsetDb: 0, toleranceDb: 0);

        Assert.Equal(TargetOverlayCurveBuilder.DefaultTargetGrid.Length, shape.Target.Length);
        Assert.Equal(20.0, shape.Target[0].X, precision: 6);
        Assert.Equal(20_000.0, shape.Target[^1].X, precision: 6);
        Assert.Empty(shape.ToleranceUpper);
        Assert.Empty(shape.ToleranceLower);
    }

    [Fact]
    public void BuildShape_AppliesOffsetAndToleranceBand()
    {
        var builder = new TargetOverlayCurveBuilder();

        TargetOverlayShape shape = builder.BuildShape(FlatSpec, offsetDb: -3, toleranceDb: 2);

        Assert.All(shape.Target, point => Assert.Equal(-3, point.Y, precision: 9));
        Assert.Equal(shape.Target.Length, shape.ToleranceUpper.Length);
        Assert.Equal(shape.Target.Length, shape.ToleranceLower.Length);
        Assert.All(shape.ToleranceUpper, point => Assert.Equal(-1, point.Y, precision: 9));
        Assert.All(shape.ToleranceLower, point => Assert.Equal(-5, point.Y, precision: 9));
    }

    [Fact]
    public void BuildShape_ReusesTheCachedShapeForUnchangedInputs()
    {
        var builder = new TargetOverlayCurveBuilder();

        TargetOverlayShape first = builder.BuildShape(FlatSpec, offsetDb: 1, toleranceDb: 2);
        TargetOverlayShape second = builder.BuildShape(FlatSpec, offsetDb: 1, toleranceDb: 2);
        // Records compare by value; the equal spec instance must also hit.
        TargetOverlayShape third = builder.BuildShape(
            TargetCurveSpec.FromPreset(TargetPreset.Flat),
            offsetDb: 1,
            toleranceDb: 2);

        Assert.Same(first, second);
        Assert.Same(first, third);
    }

    [Theory]
    [InlineData(1.5, 2.0, 2.0)]
    [InlineData(1.0, 2.5, 2.0)]
    [InlineData(1.0, 2.0, 3.0)]
    public void BuildShape_RebuildsWhenAnyInputChanges(
        double offsetDb,
        double toleranceDb,
        double tiltDbPerOctave)
    {
        var builder = new TargetOverlayCurveBuilder();
        TargetOverlayShape baseline = builder.BuildShape(
            FlatSpec with { TiltDbPerOctave = 2.0 },
            offsetDb: 1.0,
            toleranceDb: 2.0);

        TargetOverlayShape changed = builder.BuildShape(
            FlatSpec with { TiltDbPerOctave = tiltDbPerOctave },
            offsetDb,
            toleranceDb);

        Assert.NotSame(baseline, changed);
    }

    [Fact]
    public void BuildDeviation_FollowsTheSelectedMode()
    {
        // Source sits 4 dB above a flat 0 dB target everywhere.
        OverlayPoint[] source =
        [
            new OverlayPoint(100, 4),
            new OverlayPoint(1_000, 4),
            new OverlayPoint(10_000, 4)
        ];

        DataPoint[] deviation = TargetOverlayCurveBuilder.BuildDeviation(
            source,
            FlatSpec,
            offsetDb: 0,
            smoothingInverseOctaves: 0,
            TargetDeviationMode.Deviation);
        DataPoint[] correction = TargetOverlayCurveBuilder.BuildDeviation(
            source,
            FlatSpec,
            offsetDb: 0,
            smoothingInverseOctaves: 0,
            TargetDeviationMode.Correction);

        Assert.All(deviation, point => Assert.Equal(4, point.Y, precision: 9));
        Assert.All(correction, point => Assert.Equal(-4, point.Y, precision: 9));
    }

    [Fact]
    public void BuildDeviation_IsEmptyForNoneModeOrDegenerateSource()
    {
        OverlayPoint[] source = [new OverlayPoint(100, 4), new OverlayPoint(1_000, 4)];

        Assert.Empty(TargetOverlayCurveBuilder.BuildDeviation(
            source,
            FlatSpec,
            0,
            0,
            TargetDeviationMode.None));
        Assert.Empty(TargetOverlayCurveBuilder.BuildDeviation(
            [new OverlayPoint(100, 4)],
            FlatSpec,
            0,
            0,
            TargetDeviationMode.Deviation));
    }
}
