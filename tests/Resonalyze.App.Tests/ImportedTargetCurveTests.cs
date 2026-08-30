namespace Resonalyze.App.Tests;

/// <summary>
/// A target imported from a file is an arbitrary text file becoming a shape the
/// auto-tuner corrects toward, so what the reading does to it matters as much as
/// that it reads: where it is anchored, what it says between the points, what it
/// says outside them, and what survives being stored.
/// </summary>
public sealed class ImportedTargetCurveTests
{
    [Fact]
    public void TheShapeIsAnchoredAtOneKilohertz()
    {
        // A house curve written around 75 dB SPL and the same shape written
        // around 0 dB are one target: the level a target hangs at belongs to the
        // plot, and the wizard's Target Level is where the user sets it.
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
        // The midpoint of a decade is its geometric centre, not its arithmetic
        // one: 316 Hz sits halfway between 100 Hz and 1 kHz on the plot the
        // target is drawn on, and that is where half the step belongs.
        ImportedTargetCurve curve = Build((100, 6.0), (1_000, 0.0));

        Assert.Equal(3, curve.Evaluate(Math.Sqrt(100 * 1_000)), 9);
        Assert.NotEqual(3, curve.Evaluate(550), 1);
    }

    [Fact]
    public void OutsideItsRangeTheCurveHoldsItsEnds()
    {
        // A file that stops at 200 Hz says nothing about 10 kHz. Continuing its
        // last slope would invent a target it never stated — and the auto-tuner
        // would then chase that invention with real filters. A bass-only curve is
        // therefore flat above its top point, and the anchor lands on that held
        // value: +8 over +2 at the top becomes +6 over a flat 1 kHz reference.
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
        // Two levels at one frequency have no order to interpolate along, and
        // dropping one would let the file's line order decide the target.
        ImportedTargetCurve curve = Build((100, 4.0), (100, 8.0), (1_000, 0.0));

        Assert.Equal(2, curve.PointCount);
        Assert.Equal(6, curve.Evaluate(100), 12);
    }

    [Fact]
    public void FewerThanTwoUsablePointsIsNotAShape()
    {
        // One point is a level, not a shape, and a file of prose is not a curve.
        Assert.Null(ImportedTargetCurve.FromPoints("one.txt", [new OverlayPoint(1_000, 3)]));
        Assert.Null(ImportedTargetCurve.FromPoints("none.txt", []));
        Assert.Null(ImportedTargetCurve.FromPoints(
            "unusable.txt",
            [new OverlayPoint(0, 3), new OverlayPoint(double.NaN, 1)]));
    }

    [Fact]
    public void LevelsThatOverflowTheAnchoringAreRefused()
    {
        // Both levels are finite, so the cleaning above accepts them — their
        // difference is not, and anchoring is a subtraction. An infinite target
        // would draw as a broken line, hand Auto Tune an infinite goal, and throw
        // on the way into the settings file, so there is no curve here at all.
        Assert.Null(ImportedTargetCurve.FromPoints(
            "overflow.txt",
            [new OverlayPoint(100, 1e308), new OverlayPoint(1_000, -1e308)]));
        Assert.Null(ImportedTargetCurve.FromStorage(
            "overflow.json",
            [100, 1e308, 1_000, -1e308]));
        // Large but survivable levels are still read: the refusal is about the
        // arithmetic overflowing, not about a number being unusually big.
        Assert.NotNull(ImportedTargetCurve.FromPoints(
            "loud.txt",
            [new OverlayPoint(100, 1e30), new OverlayPoint(1_000, -1e30)]));
    }

    [Fact]
    public void ADenseFileIsThinnedButStillReadsTheSame()
    {
        // A full-resolution export runs to tens of thousands of lines, and the
        // curve is carried by value into the settings file and into a session.
        var points = new List<OverlayPoint>();
        for (int index = 0; index < 40_000; index++)
        {
            double frequency = 20 * Math.Pow(1_000, index / 39_999.0);
            points.Add(new OverlayPoint(frequency, -2 * Math.Log2(frequency / 1_000)));
        }

        ImportedTargetCurve curve = ImportedTargetCurve.FromPoints("dense.txt", points)!;

        Assert.Equal(ImportedTargetCurve.MaximumPoints, curve.PointCount);
        // The band it covers is still the band the file stated, ends included.
        Assert.Equal(20, curve.LowFrequencyHz, 6);
        Assert.Equal(20_000, curve.HighFrequencyHz, 6);
        // And it is still the same curve: thinning a smooth shape onto a log grid
        // costs far less than the dB the tune is judged in.
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
        // The settings file and a session file are where a NaN or an unordered
        // pair can enter, so the stored form goes through the same reading as a
        // freshly imported file rather than being trusted.
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
        // The target rides inside a record and a plot cache key is one of the
        // things that compares it, so equality has to read the points rather
        // than the reference a fresh import happens to produce.
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
        // The one evaluation every consumer asks — the overlay math, the wizard
        // plot, the Virtual DSP plot, the dialog's preview and the auto-tuner
        // behind them — so this is what makes the import reach all of them.
        TargetCurveSpec car = TargetCurveSpec.FromPreset(TargetPreset.Car);
        TargetCurveSpec imported = car with
        {
            Imported = Build((100, 6.0), (1_000, 0.0), (10_000, -3.0))
        };

        Assert.Equal(6, imported.Evaluate(100), 12);
        Assert.Equal(0, imported.Evaluate(1_000), 12);
        Assert.Equal(-3, imported.Evaluate(10_000), 12);
        // The parametric numbers are still there, untouched, because picking a
        // preset in the settings dialog is how the user comes back to them.
        Assert.Equal(car.BassShelfGainDb, imported.BassShelfGainDb);
        Assert.Equal(car.Evaluate(100), (imported with { Imported = null }).Evaluate(100));
    }

    [Fact]
    public void NormalizingATargetKeepsTheImportedShape()
    {
        // Every target that comes off disk is normalized, and normalizing rebuilds
        // the spec: an imported shape dropped there would turn a user's house
        // curve back into a preset on the next launch.
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
