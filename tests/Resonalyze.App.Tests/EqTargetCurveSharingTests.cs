using System.Drawing;

namespace Resonalyze.App.Tests;

/// <summary>The host pushes the target on every settings change, so pushing the held value must not count as an edit (it would loop).</summary>
public sealed class EqTargetCurveSharingTests
{
    [Fact]
    public void ApplyTargetCurve_ReadsBackEveryFieldItWasGiven()
    {
        using var panel = new EqWizardPanel();
        var curve = new EqTargetCurve(
            TargetPreset.Car,
            new TargetCurveSpec(-0.7, 9.2, 105, 0.9, -3, 10_000, 0.7, 1.5, 2_800, 1.2),
            ToleranceDb: 2.5,
            TargetDeviationMode.Deviation,
            Color.FromArgb(255, 200, 120, 40),
            StrokeThickness: 3.5,
            OverlayLineStyle.DashDot,
            SmoothingInverseOctaves: 6);

        panel.ApplyTargetCurve(curve);

        // A field the setter forgot would reset part of the target on the first VDSP Target dialog visit.
        Assert.Equal(curve, panel.TargetCurve);
    }

    [Fact]
    public void ApplyTargetCurve_WithTheCurrentValue_IsNotAnEdit()
    {
        using var panel = new EqWizardPanel();
        int changes = 0;
        panel.SettingsChanged += () => changes++;

        panel.ApplyTargetCurve(panel.TargetCurve);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void ApplyTargetCurve_WithADifferentValue_ReportsTheChange()
    {
        using var panel = new EqWizardPanel();
        int changes = 0;
        panel.SettingsChanged += () => changes++;

        panel.ApplyTargetCurve(panel.TargetCurve with
        {
            Spec = TargetCurveSpec.FromPreset(TargetPreset.HarmanRoom),
            Preset = TargetPreset.HarmanRoom
        });

        Assert.Equal(1, changes);
        Assert.Equal(TargetPreset.HarmanRoom, panel.TargetCurve.Preset);
    }
}
