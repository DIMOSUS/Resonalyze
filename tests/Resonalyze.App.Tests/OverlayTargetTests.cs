namespace Resonalyze.App.Tests;

public sealed class OverlayTargetTests
{
    [Fact]
    public void Evaluate_FlatPresetIsZeroEverywhere()
    {
        TargetCurveSpec spec = TargetCurveSpec.FromPreset(TargetPreset.Flat);

        Assert.Equal(0.0, spec.Evaluate(20), precision: 9);
        Assert.Equal(0.0, spec.Evaluate(1_000), precision: 9);
        Assert.Equal(0.0, spec.Evaluate(20_000), precision: 9);
    }

    [Fact]
    public void Evaluate_TiltIsZeroAtPivotAndLinearInOctaves()
    {
        var spec = new TargetCurveSpec(
            TiltDbPerOctave: -1.0,
            BassShelfGainDb: 0,
            BassShelfFrequencyHz: 100,
            BassShelfWidthOctaves: 1.5);

        Assert.Equal(0.0, spec.Evaluate(TargetCurveSpec.PivotHz), precision: 9);
        Assert.Equal(-1.0, spec.Evaluate(2_000), precision: 9);
        Assert.Equal(1.0, spec.Evaluate(500), precision: 9);
    }

    [Fact]
    public void Evaluate_ShelfIsHalfGainAtCornerAndSaturatesBelow()
    {
        var spec = new TargetCurveSpec(
            TiltDbPerOctave: 0,
            BassShelfGainDb: 6,
            BassShelfFrequencyHz: 100,
            BassShelfWidthOctaves: 1.0);

        Assert.Equal(3.0, spec.Evaluate(100), precision: 6); // half gain at corner
        Assert.True(spec.Evaluate(10) > 5.5); // approaches full gain well below
        Assert.True(spec.Evaluate(2_000) < 0.5); // approaches zero well above
    }

    [Fact]
    public void BuildTarget_DeviationIsMeasurementMinusShiftedTarget()
    {
        OverlayPoint[] source =
        [
            new OverlayPoint(100, -5),
            new OverlayPoint(1_000, -5),
            new OverlayPoint(10_000, -5)
        ];
        TargetCurveSpec spec = TargetCurveSpec.FromPreset(TargetPreset.Flat);

        TargetCurveResult result = OverlayMath.BuildTarget(
            source,
            spec,
            offsetDb: -8,
            toleranceDb: 0,
            smoothingInverseOctaves: 0);

        Assert.All(result.Target, point => Assert.Equal(-8.0, point.Y, precision: 9));
        Assert.All(result.Deviation, point => Assert.Equal(3.0, point.Y, precision: 9));
        Assert.Empty(result.ToleranceUpper);
        Assert.Empty(result.ToleranceLower);
    }

    [Fact]
    public void BuildTarget_ToleranceBandBracketsTarget()
    {
        OverlayPoint[] source =
        [
            new OverlayPoint(100, 0),
            new OverlayPoint(1_000, 0)
        ];

        TargetCurveResult result = OverlayMath.BuildTarget(
            source,
            TargetCurveSpec.FromPreset(TargetPreset.Flat),
            offsetDb: 0,
            toleranceDb: 3,
            smoothingInverseOctaves: 0);

        Assert.Equal(result.Target.Length, result.ToleranceUpper.Length);
        for (int i = 0; i < result.Target.Length; i++)
        {
            Assert.Equal(result.Target[i].Y + 3, result.ToleranceUpper[i].Y, precision: 9);
            Assert.Equal(result.Target[i].Y - 3, result.ToleranceLower[i].Y, precision: 9);
        }
    }
}
