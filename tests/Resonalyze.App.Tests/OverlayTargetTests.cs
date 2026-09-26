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

        Assert.Equal(3.0, spec.Evaluate(100), precision: 6);
        Assert.True(spec.Evaluate(10) > 5.5);
        Assert.True(spec.Evaluate(2_000) < 0.5);
    }

    [Fact]
    public void Evaluate_TrebleShelfIsHalfGainAtCornerAndSaturatesAbove()
    {
        TargetCurveSpec spec = Spec(trebleGain: -10, trebleFreq: 4_000, trebleWidth: 1.0);

        Assert.Equal(-5.0, spec.Evaluate(4_000), precision: 6);
        Assert.True(spec.Evaluate(16_000) < -9.0);
        Assert.True(spec.Evaluate(500) > -1.0);
    }

    [Fact]
    public void Evaluate_PresenceIsPeakAtCenterAndFadesAway()
    {
        TargetCurveSpec spec = Spec(presenceGain: 4, presenceFreq: 3_000, presenceWidth: 0.5);

        Assert.Equal(4.0, spec.Evaluate(3_000), precision: 6);
        Assert.True(spec.Evaluate(1_000) < 1.0);
        Assert.True(spec.Evaluate(9_000) < 1.0);
    }

    /// <summary>Third-octave in-car target: full ≈+9 dB shelf by 40 Hz, flat 630 Hz…5 kHz, -3 dB to 20 kHz. Tolerance is the tanh-shelf fit error.</summary>
    public static TheoryData<double, double> CarTargetTable => new()
    {
        { 20, 9.2 }, { 25, 9.2 }, { 31.5, 9.1 }, { 40, 9.1 }, { 50, 8.9 },
        { 63, 8.7 }, { 80, 8.1 }, { 100, 7.2 }, { 125, 5.9 }, { 160, 4.1 },
        { 200, 2.6 }, { 250, 1.5 }, { 315, 0.8 }, { 400, 0.4 }, { 500, 0.2 },
        { 630, 0.0 }, { 800, 0.0 }, { 1_000, 0.0 }, { 1_250, 0.0 },
        { 1_600, 0.0 }, { 2_000, 0.0 }, { 2_500, 0.0 }, { 3_150, 0.0 },
        { 4_000, 0.0 }, { 5_000, 0.0 }, { 6_300, -0.5 }, { 8_000, -1.0 },
        { 10_000, -1.5 }, { 12_500, -2.0 }, { 16_000, -2.5 }, { 20_000, -3.0 }
    };

    [Theory]
    [MemberData(nameof(CarTargetTable))]
    public void Evaluate_CarPresetFollowsTheInCarTable(
        double frequencyHz,
        double expectedDb)
    {
        TargetCurveSpec spec = TargetCurveSpec.FromPreset(TargetPreset.Car);

        Assert.Equal(expectedDb, spec.Evaluate(frequencyHz), tolerance: 0.25);
    }

    [Fact]
    public void Evaluate_CarPresetsKeepTheMidrangeFlat()
    {
        foreach (TargetPreset preset in
                 new[] { TargetPreset.Car, TargetPreset.CarMild, TargetPreset.CarBass })
        {
            TargetCurveSpec spec = TargetCurveSpec.FromPreset(preset);

            Assert.Equal(0.0, spec.TiltDbPerOctave, precision: 9);
            foreach (double frequencyHz in new[] { 630.0, 1_000.0, 2_500.0, 5_000.0 })
            {
                Assert.Equal(0.0, spec.Evaluate(frequencyHz), tolerance: 0.2);
            }
        }
    }

    [Fact]
    public void Evaluate_CarVariantsMoveOnlyTheBassShelf()
    {
        TargetCurveSpec car = TargetCurveSpec.FromPreset(TargetPreset.Car);
        TargetCurveSpec mild = TargetCurveSpec.FromPreset(TargetPreset.CarMild);
        TargetCurveSpec bass = TargetCurveSpec.FromPreset(TargetPreset.CarBass);

        Assert.True(mild.Evaluate(20) < car.Evaluate(20) - 2.0);
        Assert.True(bass.Evaluate(20) > car.Evaluate(20) + 2.0);

        Assert.Equal(car.BassShelfFrequencyHz, mild.BassShelfFrequencyHz);
        Assert.Equal(car.BassShelfFrequencyHz, bass.BassShelfFrequencyHz);
        Assert.Equal(car.BassShelfWidthOctaves, mild.BassShelfWidthOctaves);
        Assert.Equal(car.BassShelfWidthOctaves, bass.BassShelfWidthOctaves);

        Assert.Equal(car.Evaluate(20_000), mild.Evaluate(20_000), tolerance: 1e-6);
        Assert.Equal(car.Evaluate(20_000), bass.Evaluate(20_000), tolerance: 1e-6);
    }

    /// <summary>ISO 2969 X-curve: flat to 2 kHz, then -3 dB/oct. Tolerance is the tanh-shelf fit error; guards the knee position.</summary>
    public static TheoryData<double, double> XCurveTable => new()
    {
        { 200, 0.0 }, { 500, 0.0 }, { 1_000, 0.0 }, { 2_000, 0.0 },
        { 2_500, -0.97 }, { 3_150, -1.97 }, { 4_000, -3.0 }, { 5_000, -3.97 },
        { 6_300, -4.97 }, { 8_000, -6.0 }, { 10_000, -6.97 }, { 12_500, -7.93 },
        { 16_000, -9.0 }, { 20_000, -9.97 }
    };

    [Theory]
    [MemberData(nameof(XCurveTable))]
    public void Evaluate_XCurveFollowsTheIsoLine(double frequencyHz, double expectedDb)
    {
        TargetCurveSpec spec = TargetCurveSpec.FromPreset(TargetPreset.XCurve);

        Assert.Equal(expectedDb, spec.Evaluate(frequencyHz), tolerance: 0.7);
    }

    [Fact]
    public void Evaluate_XCurveStaysFlatBelowTheKnee()
    {
        TargetCurveSpec spec = TargetCurveSpec.FromPreset(TargetPreset.XCurve);

        Assert.Equal(0.0, spec.Evaluate(1_000), tolerance: 0.2);
        Assert.Equal(0.0, spec.Evaluate(500), tolerance: 0.1);
        Assert.Equal(0.0, spec.Evaluate(100), tolerance: 0.1);
    }

    [Fact]
    public void DefaultPreset_IsTheInCarShape()
    {
        TargetCurveSpec spec = TargetCurveSpec.FromPreset(OverlayTargets.DefaultPreset);

        Assert.Equal(TargetPreset.Car, OverlayTargets.DefaultPreset);
        Assert.Equal(0.0, spec.TiltDbPerOctave, precision: 9);
    }

    [Fact]
    public void ResolvePreset_KeepsAPresetWhoseParametersStillMatch()
    {
        foreach (TargetPreset preset in Enum.GetValues<TargetPreset>())
        {
            Assert.Equal(
                preset,
                OverlayTargets.ResolvePreset(preset, TargetCurveSpec.FromPreset(preset)));
        }
    }

    [Theory]
    // Pre-refit shapes: a stored preset name must not advertise the new shape over old numbers.
    [InlineData(TargetPreset.XCurve, 0, 0, 100, 1.5, -10, 2_500, 2.0)]
    [InlineData(TargetPreset.Car, -1.0, 8, 80, 1.5, 0, 5_000, 1.5)]
    [InlineData(TargetPreset.Car, 0, 9.2, 100, 0.9, -3, 10_000, 0.7)]
    public void ResolvePreset_FallsBackToCustomWhenTheStoredShapeMovedOn(
        TargetPreset preset,
        double tilt,
        double bassGain, double bassFreq, double bassWidth,
        double trebleGain, double trebleFreq, double trebleWidth)
    {
        var stored = new TargetCurveSpec(
            tilt,
            bassGain, bassFreq, bassWidth,
            trebleGain, trebleFreq, trebleWidth,
            0, 3_000, 1.0);

        Assert.Equal(TargetPreset.Custom, OverlayTargets.ResolvePreset(preset, stored));
    }

    [Fact]
    public void ResolvePreset_LeavesCustomAlone()
    {
        TargetCurveSpec spec = Spec(tilt: -3, bassGain: 11, presenceGain: 2);

        Assert.Equal(
            TargetPreset.Custom,
            OverlayTargets.ResolvePreset(TargetPreset.Custom, spec));
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
