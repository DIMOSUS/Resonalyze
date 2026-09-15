namespace Resonalyze.App.Tests;

public sealed class ImportedTargetCurveTests
{
    [Fact]
    public void TheShapeIsAnchoredAtOneKilohertz()
    {
        // The target's level belongs to the wizard's Target Level, not the file.
        ImportedTargetCurve absolute = Build(
            (100, 81.0), (1_000, 75.0), (10_000, 72.0));
        ImportedTargetCurve relative = Build(
            (100, 6.0), (1_000, 0.0), (10_000, -3.0));

        Assert.Equal(0, absolute.Evaluate(1_000), 12);
        Assert.Equal(6, absolute.Evaluate(100), 12);
        Assert.Equal(-3, absolute.Evaluate(10_000), 12);
        for (double frequency = 20; frequency <= 20_000; frequency *= 1.3)
        {
            Assert.Equal(relative.Evaluate(frequency), absolute.Evaluate(frequency), 12);
        }
    }

    [Fact]
    public void BetweenPointsTheCurveIsStraightInLogFrequency()
    {
        // 316 Hz is the geometric midpoint of the decade on the log plot.
        ImportedTargetCurve curve = Build((100, 6.0), (1_000, 0.0));

        Assert.Equal(3, curve.Evaluate(Math.Sqrt(100 * 1_000)), 9);
        Assert.NotEqual(3, curve.Evaluate(550), 1);
    }

    [Fact]
    public void OutsideItsRangeTheCurveHoldsItsEnds()
    {
        // No extrapolation beyond the last point: flat above, anchor on the held value (+8 over +2 becomes +6).
        ImportedTargetCurve curve = Build((50, 8.0), (200, 2.0));

        Assert.Equal(6, curve.Evaluate(20), 12);
        Assert.Equal(6, curve.Evaluate(1), 12);
        Assert.Equal(0, curve.Evaluate(200), 12);
        Assert.Equal(0, curve.Evaluate(20_000), 12);
        Assert.Equal(0, curve.Evaluate(0), 12);
        Assert.Equal(0, curve.Evaluate(-5), 12);
    }

    [Fact]
    public void UnusablePairsAreDroppedAndTheRestIsOrdered()
    {
        ImportedTargetCurve curve = Build(
            (10_000, -3.0),
            (double.NaN, 1.0),
            (100, 6.0),
            (-40, 2.0),
            (0, 5.0),
            (1_000, double.PositiveInfinity),
            (1_000, 0.0));

        Assert.Equal(3, curve.PointCount);
        Assert.Equal(100, curve.LowFrequencyHz);
        Assert.Equal(10_000, curve.HighFrequencyHz);
        Assert.Equal(6, curve.Evaluate(100), 12);
    }

    [Fact]
    public void TwoValuesAtOneFrequencyAreAveraged()
    {
        ImportedTargetCurve curve = Build((100, 4.0), (100, 8.0), (1_000, 0.0));

        Assert.Equal(2, curve.PointCount);
        Assert.Equal(6, curve.Evaluate(100), 12);
    }

    [Fact]
    public void FewerThanTwoUsablePointsIsNotAShape()
    {
        Assert.Null(ImportedTargetCurve.FromPoints("one.txt", [new OverlayPoint(1_000, 3)]));
        Assert.Null(ImportedTargetCurve.FromPoints("none.txt", []));
        Assert.Null(ImportedTargetCurve.FromPoints(
            "unusable.txt",
            [new OverlayPoint(0, 3), new OverlayPoint(double.NaN, 1)]));
    }

    [Fact]
    public void LevelsThatOverflowTheAnchoringAreRefused()
    {
        // Finite levels whose difference overflows: anchoring is a subtraction.
        Assert.Null(ImportedTargetCurve.FromPoints(
            "overflow.txt",
            [new OverlayPoint(100, 1e308), new OverlayPoint(1_000, -1e308)]));
        Assert.Null(ImportedTargetCurve.FromStorage(
            "overflow.json",
            [100, 1e308, 1_000, -1e308]));
        Assert.NotNull(ImportedTargetCurve.FromPoints(
            "loud.txt",
            [new OverlayPoint(100, 1e30), new OverlayPoint(1_000, -1e30)]));
    }

    [Fact]
    public void ADenseFileIsThinnedButStillReadsTheSame()
    {
        var points = new List<OverlayPoint>();
        for (int index = 0; index < 40_000; index++)
        {
            double frequency = 20 * Math.Pow(1_000, index / 39_999.0);
            points.Add(new OverlayPoint(frequency, -2 * Math.Log2(frequency / 1_000)));
        }

        ImportedTargetCurve curve = ImportedTargetCurve.FromPoints("dense.txt", points)!;

        Assert.Equal(ImportedTargetCurve.MaximumPoints, curve.PointCount);
        Assert.Equal(20, curve.LowFrequencyHz, 6);
        Assert.Equal(20_000, curve.HighFrequencyHz, 6);
        for (double frequency = 20; frequency <= 20_000; frequency *= 1.1)
        {
            Assert.Equal(-2 * Math.Log2(frequency / 1_000), curve.Evaluate(frequency), 3);
        }
    }

    [Fact]
    public void StoredAndReadBackItIsTheSameCurve()
    {
        ImportedTargetCurve curve = Build(
            (30, 9.0), (100, 6.0), (1_000, 0.0), (10_000, -3.0));

        ImportedTargetCurve? restored =
            ImportedTargetCurve.FromStorage(curve.Name, curve.ToStorage());

        Assert.Equal(curve, restored);
    }

    [Fact]
    public void AStoredCurveIsCleanedAgainOnTheWayIn()
    {
        // Stored forms can be hand-edited, so they go through the same reading as an import.
        ImportedTargetCurve? restored = ImportedTargetCurve.FromStorage(
            "hand-edited.json",
            [10_000, -3, double.NaN, 4, 100, 6, 1_000, 0, 250]);

        Assert.NotNull(restored);
        Assert.Equal(3, restored!.PointCount);
        Assert.Equal(6, restored.Evaluate(100), 12);
        Assert.Equal(0, restored.Evaluate(1_000), 12);
    }

    [Fact]
    public void AnAbsentOrUnusableStoredCurveIsNoCurve()
    {
        Assert.Null(ImportedTargetCurve.FromStorage("none", null));
        Assert.Null(ImportedTargetCurve.FromStorage("half a pair", [1_000]));
        Assert.Null(ImportedTargetCurve.FromStorage("one point", [1_000, 0]));
    }

    [Fact]
    public void CurvesAreComparedByWhatTheyHold()
    {
        // Plot cache keys compare the target, so equality reads the points.
        ImportedTargetCurve curve = Build((100, 6.0), (1_000, 0.0));
        ImportedTargetCurve same = Build((100, 6.0), (1_000, 0.0));
        ImportedTargetCurve other = Build((100, 5.0), (1_000, 0.0));

        Assert.Equal(curve, same);
        Assert.Equal(curve.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(curve, other);
        Assert.NotEqual(curve, ImportedTargetCurve.FromPoints(
            "another-name.txt",
            [new OverlayPoint(100, 6), new OverlayPoint(1_000, 0)])!);
    }

    [Fact]
    public void AnImportedShapeReplacesTheParametricTerms()
    {
        TargetCurveSpec car = TargetCurveSpec.FromPreset(TargetPreset.Car);
        TargetCurveSpec imported = car with
        {
            Imported = Build((100, 6.0), (1_000, 0.0), (10_000, -3.0))
        };

        Assert.Equal(6, imported.Evaluate(100), 12);
        Assert.Equal(0, imported.Evaluate(1_000), 12);
        Assert.Equal(-3, imported.Evaluate(10_000), 12);
        Assert.Equal(car.BassShelfGainDb, imported.BassShelfGainDb);
        Assert.Equal(car.Evaluate(100), (imported with { Imported = null }).Evaluate(100));
    }

    [Fact]
    public void NormalizingATargetKeepsTheImportedShape()
    {
        // Normalizing rebuilds the spec: dropping the shape would revert a house curve to a preset on launch.
        var curve = new EqTargetCurve(
            TargetPreset.Custom,
            TargetCurveSpec.FromPreset(TargetPreset.Custom) with
            {
                Imported = Build((100, 6.0), (1_000, 0.0))
            },
            ToleranceDb: double.NaN,
            TargetDeviationMode.Deviation,
            System.Drawing.Color.FromArgb(255, 55, 200, 160),
            StrokeThickness: 2,
            OverlayLineStyle.Dash,
            SmoothingInverseOctaves: 0);

        EqTargetCurve clean = curve.Normalized();

        Assert.Equal(curve.Spec.Imported, clean.Spec.Imported);
        Assert.Equal(3, clean.ToleranceDb);
    }

    private static ImportedTargetCurve Build(params (double Hz, double Db)[] points) =>
        ImportedTargetCurve.FromPoints(
            "house.txt",
            points.Select(point => new OverlayPoint(point.Hz, point.Db)))!;
}
