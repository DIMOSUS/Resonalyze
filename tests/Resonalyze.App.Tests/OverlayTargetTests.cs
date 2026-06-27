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

    private static TargetCurveSpec Spec(
        double tilt = 0,
        double bassGain = 0, double bassFreq = 100, double bassWidth = 1.5,
        double trebleGain = 0, double trebleFreq = 5_000, double trebleWidth = 1.5,
        double presenceGain = 0, double presenceFreq = 3_000, double presenceWidth = 1.0)
        => new(
            tilt,
            bassGain, bassFreq, bassWidth,
            trebleGain, trebleFreq, trebleWidth,
            presenceGain, presenceFreq, presenceWidth);

    [Fact]
    public void Evaluate_TiltIsZeroAtPivotAndLinearInOctaves()
    {
        TargetCurveSpec spec = Spec(tilt: -1.0);

        Assert.Equal(0.0, spec.Evaluate(TargetCurveSpec.PivotHz), precision: 9);
        Assert.Equal(-1.0, spec.Evaluate(2_000), precision: 9);
        Assert.Equal(1.0, spec.Evaluate(500), precision: 9);
    }

    [Fact]
    public void Evaluate_BassShelfIsHalfGainAtCornerAndSaturatesBelow()
    {
        TargetCurveSpec spec = Spec(bassGain: 6, bassFreq: 100, bassWidth: 1.0);

        Assert.Equal(3.0, spec.Evaluate(100), precision: 6); // half gain at corner
        Assert.True(spec.Evaluate(10) > 5.5); // approaches full gain well below
        Assert.True(spec.Evaluate(2_000) < 0.5); // approaches zero well above
    }

    [Fact]
    public void Evaluate_TrebleShelfIsHalfGainAtCornerAndSaturatesAbove()
    {
        TargetCurveSpec spec = Spec(trebleGain: -10, trebleFreq: 4_000, trebleWidth: 1.0);

        Assert.Equal(-5.0, spec.Evaluate(4_000), precision: 6); // half gain at corner
        Assert.True(spec.Evaluate(16_000) < -9.0); // approaches full gain well above
        Assert.True(spec.Evaluate(500) > -1.0); // approaches zero well below
    }

    [Fact]
    public void Evaluate_PresenceIsPeakAtCenterAndFadesAway()
    {
        TargetCurveSpec spec = Spec(presenceGain: 4, presenceFreq: 3_000, presenceWidth: 0.5);

        Assert.Equal(4.0, spec.Evaluate(3_000), precision: 6); // peak at center
        Assert.True(spec.Evaluate(1_000) < 1.0); // fades away from center
        Assert.True(spec.Evaluate(9_000) < 1.0);
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
    public void BuildTarget_CorrectionModeNegatesDeviation()
    {
        OverlayPoint[] source =
        [
            new OverlayPoint(100, -5),
            new OverlayPoint(1_000, -5)
        ];

        TargetCurveResult result = OverlayMath.BuildTarget(
            source,
            TargetCurveSpec.FromPreset(TargetPreset.Flat),
            offsetDb: -8,
            toleranceDb: 0,
            smoothingInverseOctaves: 0,
            TargetDeviationMode.Correction);

        // Deviation would be +3; correction is the EQ gain to reach the target.
        Assert.All(result.Deviation, point => Assert.Equal(-3.0, point.Y, precision: 9));
    }

    [Fact]
    public void BuildTarget_NoneModeOmitsDeviation()
    {
        OverlayPoint[] source =
        [
            new OverlayPoint(100, -5),
            new OverlayPoint(1_000, -5)
        ];

        TargetCurveResult result = OverlayMath.BuildTarget(
            source,
            TargetCurveSpec.FromPreset(TargetPreset.Flat),
            offsetDb: 0,
            toleranceDb: 0,
            smoothingInverseOctaves: 0,
            TargetDeviationMode.None);

        Assert.NotEmpty(result.Target);
        Assert.Empty(result.Deviation);
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
