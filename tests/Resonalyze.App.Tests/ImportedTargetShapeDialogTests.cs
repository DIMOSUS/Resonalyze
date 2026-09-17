using System.Drawing;
using System.Reflection;

namespace Resonalyze.App.Tests;

public sealed class ImportedTargetShapeDialogTests
{
    [Fact]
    public void AnImportedShapeSurvivesAVisitToTheDialog()
    {
        ImportedTargetCurve imported = House();
        using OverlayTargetSettingsDialog dialog = Open(imported);

        Assert.Equal(imported, dialog.Spec.Imported);
        Assert.Equal(TargetPreset.Car, dialog.Preset);
        Assert.False(Input(dialog, "tiltInput").Enabled);
        Assert.False(Input(dialog, "bassGainInput").Enabled);
    }

    [Fact]
    public void PickingAPresetDropsTheImportedShape()
    {
        ImportedTargetCurve imported = House();
        using OverlayTargetSettingsDialog dialog = Open(imported);

        SelectPreset(dialog, TargetPreset.XCurve);

        Assert.Null(dialog.Spec.Imported);
        Assert.Equal(TargetPreset.XCurve, dialog.Preset);
        Assert.Equal(
            TargetCurveSpec.FromPreset(TargetPreset.XCurve).TrebleShelfGainDb,
            dialog.Spec.TrebleShelfGainDb);
        Assert.True(Input(dialog, "tiltInput").Enabled);
    }

    [Fact]
    public void TheImportedShapeStaysInTheListToComeBackTo()
    {
        ImportedTargetCurve imported = House();
        using OverlayTargetSettingsDialog dialog = Open(imported);

        SelectPreset(dialog, TargetPreset.Flat);
        SelectImported(dialog);

        Assert.Equal(imported, dialog.Spec.Imported);
        Assert.False(Input(dialog, "bassWidthInput").Enabled);
    }

    [Fact]
    public void WithoutAnImportedShapeTheListIsPresetsOnly()
    {
        using OverlayTargetSettingsDialog dialog = Open(imported: null);

        Assert.Null(dialog.Spec.Imported);
        Assert.All(
            Combo(dialog).Items.Cast<object>(),
            item => Assert.IsType<TargetPreset>(item));
        Assert.True(Input(dialog, "tiltInput").Enabled);
    }

    private static ImportedTargetCurve House() =>
        ImportedTargetCurve.FromPoints(
            "house.txt",
            [
                new OverlayPoint(30, 9),
                new OverlayPoint(100, 6),
                new OverlayPoint(1_000, 0),
                new OverlayPoint(10_000, -3)
            ])!;

    private static OverlayTargetSettingsDialog Open(ImportedTargetCurve? imported) =>
        new(
            Mode.EqWizard,
            "EQ target",
            0,
            TargetPreset.Car,
            TargetCurveSpec.FromPreset(TargetPreset.Car) with { Imported = imported },
            toleranceDb: 3,
            TargetDeviationMode.Deviation,
            Color.FromArgb(255, 55, 200, 160),
            strokeThickness: 2,
            OverlayLineStyle.Dash,
            100,
            0,
            [],
            null,
            isolatedTarget: true);

    private static void SelectPreset(OverlayTargetSettingsDialog dialog, TargetPreset preset) =>
        Combo(dialog).SelectedItem = preset;

    private static void SelectImported(OverlayTargetSettingsDialog dialog)
    {
        ThemedComboBox combo = Combo(dialog);
        combo.SelectedItem = combo.Items.Cast<object>()
            .First(item => item is not TargetPreset);
    }

    private static ThemedComboBox Combo(OverlayTargetSettingsDialog dialog) =>
        (ThemedComboBox)Field(dialog, "presetComboBox");

    private static ThemedNumericUpDown Input(OverlayTargetSettingsDialog dialog, string name) =>
        (ThemedNumericUpDown)Field(dialog, name);

    private static object Field(OverlayTargetSettingsDialog dialog, string name) =>
        dialog.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;
}
