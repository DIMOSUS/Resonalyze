using System.Reflection;
using OxyPlot;

namespace Resonalyze.App.Tests;

/// <summary>
/// The house curve inside the wizard itself: the panel has to DRAW the imported
/// shape (which is the same curve the fit is handed, since Auto Tune reads the
/// target curve the plot builds), and it has to still be the target after a
/// restart.
/// </summary>
public sealed class EqWizardImportedTargetTests
{
    [Fact]
    public void TheWizardDrawsTheImportedShape()
    {
        // Not the preset the target still names underneath it: what the plot
        // builds is what Auto Tune corrects toward, so this is the whole feature.
        using var panel = new EqWizardPanel();
        panel.ApplyTargetCurve(TargetWith(House()));

        EqWizardCurve target = BuildTarget(panel, [100, 1_000, 10_000], offset: -40);

        Assert.Equal(-34, target.Points[0].Y, 9);
        Assert.Equal(-40, target.Points[1].Y, 9);
        Assert.Equal(-43, target.Points[2].Y, 9);
    }

    [Fact]
    public void ThePresetUnderneathTakesOverWhenTheImportIsDropped()
    {
        using var panel = new EqWizardPanel();
        panel.ApplyTargetCurve(TargetWith(House()));

        panel.ApplyTargetCurve(TargetWith(null));

        EqWizardCurve target = BuildTarget(panel, [100], offset: 0);
        Assert.Equal(
            TargetCurveSpec.FromPreset(TargetPreset.Car).Evaluate(100),
            target.Points[0].Y,
            9);
    }

    [Fact]
    public void TheImportedShapeSurvivesASettingsRoundTrip()
    {
        using var saved = new EqWizardPanel();
        saved.ApplyTargetCurve(TargetWith(House()));

        using var restored = new EqWizardPanel();
        restored.GetType()
            .GetMethod("ApplyPersistedSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(restored, [saved.CaptureSettings()]);

        Assert.Equal(House(), restored.TargetCurve.Spec.Imported);
        // And the parametric shape it was carrying rides back with it, because
        // picking a preset in the target dialog is what returns to it.
        Assert.Equal(TargetPreset.Car, restored.TargetCurve.Preset);
        Assert.Equal(
            TargetCurveSpec.FromPreset(TargetPreset.Car).BassShelfGainDb,
            restored.TargetCurve.Spec.BassShelfGainDb);
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

    private static EqWizardCurve BuildTarget(
        EqWizardPanel panel,
        double[] frequencies,
        double offset) =>
        (EqWizardCurve)panel.GetType()
            .GetMethod("BuildTargetCurve", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(panel, [frequencies, offset])!;
}
