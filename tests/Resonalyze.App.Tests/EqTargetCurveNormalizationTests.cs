using System.Drawing;

namespace Resonalyze.App.Tests;

/// <summary>Stored targets fill a dialog that casts to decimal and reads enums from combos; files allow NaN literals and numeric enums, so targets are normalized.</summary>
public sealed class EqTargetCurveNormalizationTests
{
    [Fact]
    public void Normalized_ReplacesWhatTheUiCannotTake()
    {
        var corrupt = new EqTargetCurve(
            (TargetPreset)999,
            new TargetCurveSpec(
                double.NaN,
                double.PositiveInfinity,
                double.NaN,
                1.5,
                0,
                5_000,
                double.NegativeInfinity,
                0,
                3_000,
                1.0),
            ToleranceDb: double.NaN,
            (TargetDeviationMode)42,
            Color.FromArgb(255, 240, 120, 40),
            StrokeThickness: double.NaN,
            (OverlayLineStyle)7,
            SmoothingInverseOctaves: 6);

        EqTargetCurve clean = corrupt.Normalized();

        TargetCurveSpec flat = TargetCurveSpec.FromPreset(TargetPreset.Flat);
        Assert.Equal(TargetPreset.Flat, clean.Preset);
        Assert.Equal(flat.TiltDbPerOctave, clean.Spec.TiltDbPerOctave);
        Assert.Equal(flat.BassShelfGainDb, clean.Spec.BassShelfGainDb);
        Assert.Equal(flat.BassShelfFrequencyHz, clean.Spec.BassShelfFrequencyHz);
        Assert.Equal(flat.TrebleShelfWidthOctaves, clean.Spec.TrebleShelfWidthOctaves);
        Assert.Equal(3, clean.ToleranceDb);
        Assert.Equal(TargetDeviationMode.Deviation, clean.DeviationMode);
        Assert.Equal(2, clean.StrokeThickness);
        Assert.Equal(OverlayLineStyle.Dash, clean.LineStyle);
        Assert.Equal(Color.FromArgb(255, 240, 120, 40), clean.Color);
        Assert.Equal(6, clean.SmoothingInverseOctaves);
        Assert.Equal(1.5, clean.Spec.BassShelfWidthOctaves);
        Assert.Equal(5_000, clean.Spec.TrebleShelfFrequencyHz);
    }

    [Fact]
    public void Normalized_LeavesAWellFormedTargetAlone()
    {
        var curve = new EqTargetCurve(
            TargetPreset.Car,
            TargetCurveSpec.FromPreset(TargetPreset.Car),
            ToleranceDb: 2.5,
            TargetDeviationMode.Deviation,
            Color.FromArgb(255, 55, 200, 160),
            StrokeThickness: 3.5,
            OverlayLineStyle.DashDot,
            SmoothingInverseOctaves: 0);

        Assert.Equal(curve, curve.Normalized());
    }

    [Fact]
    public void ASessionTargetThatCameBackCorrupt_StillOpensTheSettingsDialog()
    {
        var stored = new VirtualCrossoverTargetSettings
        {
            Preset = (TargetPreset)999,
            TiltDbPerOctave = double.NaN,
            BassShelfGainDb = double.PositiveInfinity,
            ToleranceDb = double.NaN,
            DeviationMode = (TargetDeviationMode)42,
            StrokeThickness = double.NaN,
            LineStyle = (OverlayLineStyle)7
        };

        EqTargetCurve curve = stored.ToCurve();

        using var dialog = new OverlayTargetSettingsDialog(
            Mode.EqWizard,
            "EQ target",
            0,
            curve.Preset,
            curve.Spec,
            curve.ToleranceDb,
            curve.DeviationMode,
            curve.Color,
            curve.StrokeThickness,
            curve.LineStyle,
            100,
            curve.SmoothingInverseOctaves,
            [],
            null,
            isolatedTarget: true);

        Assert.Equal(TargetPreset.Flat, dialog.Preset);
        Assert.Equal(OverlayLineStyle.Dash, dialog.LineStyle);
        Assert.Equal(TargetDeviationMode.Deviation, dialog.DeviationMode);
    }
}
