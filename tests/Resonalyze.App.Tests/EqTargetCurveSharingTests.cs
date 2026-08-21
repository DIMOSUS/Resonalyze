using System.Drawing;

namespace Resonalyze.App.Tests;

/// <summary>
/// The EQ target is one definition shared by the EQ Wizard, which owns and
/// persists it, and the Virtual DSP tool, which draws it over its predicted sum
/// and can edit it back through the host. These pin the wizard's end of that
/// contract: the round trip has to carry every field, and a push of the value
/// the wizard already holds must not look like an edit — the host pushes on
/// every settings change, so a false edit there would loop.
/// </summary>
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

        // Value equality on the record is the whole point: a field the setter
        // forgot would silently reset that part of the user's target on the
        // first visit to the Virtual DSP Target dialog.
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
