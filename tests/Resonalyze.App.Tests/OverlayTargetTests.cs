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

    /// <summary>
    /// The third-octave in-car target the Car preset is fitted to, and the
    /// reference of record for the car presets: a bass shelf that has reached
    /// its full ≈+9 dB by 31.5 Hz, a flat 400 Hz…5 kHz band, and a gentle
    /// rolloff of 3 dB from there to 20 kHz. The preset builds that from two
    /// tanh shelves, so it follows the table closely rather than exactly — the
    /// tolerance below is the fit error, not measurement slack.
    /// </summary>
    public static TheoryData<double, double> CarTargetTable => new()
    {
        { 20, 9.0 }, { 25, 9.0 }, { 31.5, 9.0 }, { 40, 8.8 }, { 50, 8.5 },
        { 63, 7.4 }, { 80, 6.0 }, { 100, 4.5 }, { 125, 3.0 }, { 160, 1.8 },
        { 200, 1.0 }, { 250, 0.5 }, { 315, 0.2 }, { 400, 0.0 }, { 500, 0.0 },
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
        // The midrange must not inherit a downslope: the whole point of the car
        // shape is that the bass shelf sits on top of a flat 400 Hz…5 kHz band.
        foreach (TargetPreset preset in
                 new[] { TargetPreset.Car, TargetPreset.CarMild, TargetPreset.CarBass })
        {
            TargetCurveSpec spec = TargetCurveSpec.FromPreset(preset);

            Assert.Equal(0.0, spec.TiltDbPerOctave, precision: 9);
            foreach (double frequencyHz in new[] { 400.0, 1_000.0, 2_500.0, 5_000.0 })
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

        // The corner and width are shared, so the variants must not drag the
        // lower midrange with them — that is where cabin boom lives.
        Assert.Equal(car.BassShelfFrequencyHz, mild.BassShelfFrequencyHz);
        Assert.Equal(car.BassShelfFrequencyHz, bass.BassShelfFrequencyHz);
        Assert.Equal(car.BassShelfWidthOctaves, mild.BassShelfWidthOctaves);
        Assert.Equal(car.BassShelfWidthOctaves, bass.BassShelfWidthOctaves);

        // All three keep the identical treble shelf; only the bass shelf differs,
        // and its residue at 20 kHz is far below a tenth of a dB.
        Assert.Equal(car.Evaluate(20_000), mild.Evaluate(20_000), tolerance: 1e-6);
        Assert.Equal(car.Evaluate(20_000), bass.Evaluate(20_000), tolerance: 1e-6);
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
