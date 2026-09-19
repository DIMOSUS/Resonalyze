using OxyPlot;

namespace Resonalyze.App.Tests;

public sealed class EqWizardImportedTargetTests
{
    [Fact]
    public void TheWizardDrawsTheImportedShape()
    {
        // Auto Tune corrects toward what the plot builds, so drawing the imported shape is the feature.
        var session = new EqWizardSession();
        session.SetTarget(TargetWith(House()));
        session.SetTargetOffset(-40);

        EqWizardCurve target = EqWizardRender.TargetCurve(session, [100, 1_000, 10_000]);

        Assert.Equal(-34, target.Points[0].Y, 9);
        Assert.Equal(-40, target.Points[1].Y, 9);
        Assert.Equal(-43, target.Points[2].Y, 9);
    }

    [Fact]
    public void ThePresetUnderneathTakesOverWhenTheImportIsDropped()
    {
        var session = new EqWizardSession();
        session.SetTarget(TargetWith(House()));

        session.SetTarget(TargetWith(null));

        EqWizardCurve target = EqWizardRender.TargetCurve(session, [100]);
        Assert.Equal(
            TargetCurveSpec.FromPreset(TargetPreset.Car).Evaluate(100),
            target.Points[0].Y,
            9);
    }

    [Fact]
    public void TheImportedShapeSurvivesASettingsRoundTrip()
    {
        var saved = new EqWizardSession();
        saved.SetTarget(TargetWith(House()));

        var restored = new EqWizardSession();
        restored.ApplySettings(saved.CaptureSettings());

        Assert.Equal(House(), restored.Target.Spec.Imported);
        Assert.Equal(TargetPreset.Car, restored.Target.Preset);
        Assert.Equal(
            TargetCurveSpec.FromPreset(TargetPreset.Car).BassShelfGainDb,
            restored.Target.Spec.BassShelfGainDb);
    }

    private static ImportedTargetCurve House() =>
        ImportedTargetCurve.FromPoints(
            "house.txt",
            [
                new OverlayPoint(100, 6),
                new OverlayPoint(1_000, 0),
                new OverlayPoint(10_000, -3)
            ])!;

    private static EqTargetCurve TargetWith(ImportedTargetCurve? imported) => new(
        TargetPreset.Car,
        TargetCurveSpec.FromPreset(TargetPreset.Car) with { Imported = imported },
        ToleranceDb: 3,
        TargetDeviationMode.Deviation,
        System.Drawing.Color.FromArgb(255, 55, 200, 160),
        StrokeThickness: 2,
        OverlayLineStyle.Dash,
        SmoothingInverseOctaves: 0);
}
