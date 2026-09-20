using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

public sealed class EqWizardAutoTuneShelvesTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-eq-shelves-{Guid.NewGuid():N}");

    [Fact]
    public void TheFitIsBellsOnlyUntilTheUserAsksOtherwise()
    {
        var session = new EqWizardSession();

        Assert.False(session.AllowShelves);
        Assert.False(EqWizardFit.Options(session, 0).AllowShelves);
    }

    [Fact]
    public void AllowingShelvesIsWhatTheFitIsGiven()
    {
        var session = new EqWizardSession();

        session.SetAllowShelves(true);

        Assert.True(EqWizardFit.Options(session, 0).AllowShelves);
        Assert.True(EqWizardFit.Policy(session).AllowShelves);
    }

    [Fact]
    public void TheChoiceSurvivesASettingsRoundTrip()
    {
        var saved = new EqWizardSession();
        saved.SetAllowShelves(true);

        var restored = new EqWizardSession();
        restored.ApplySettings(saved.CaptureSettings());

        Assert.True(restored.AllowShelves);
        Assert.True(EqWizardFit.Options(restored, 0).AllowShelves);
    }

    [Fact]
    public void AFileFromBeforeTheSwitchExistedOpensWithBellsOnly()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "measurement-settings.json");
        File.WriteAllText(
            path,
            "{ \"SchemaVersion\": 12, \"EqWizard\": { \"CutsOnly\": true } }");

        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.Null(settings.LoadWarning);
        Assert.False(settings.EqWizard.AllowShelves);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void TheBoostsRowShelvesAndTheButtonStackWithoutTouching(float scale)
    {
        // The label and the checkbox auto-size at designer coordinates, so the block is measured at real display scales.
        using var panel = new EqWizardPanel();
        if (scale != 1.0f)
        {
            panel.Scale(new SizeF(scale, scale));
        }

        var box = (Control)Field(panel, "panelAutoTune");
        Control[] stacked =
        [
            (Control)Field(panel, "labelBoosts"),
            (Control)Field(panel, "comboBoxBoosts"),
            ShelvesBox(panel),
            (Control)Field(panel, "buttonAutoTune")
        ];

        foreach (Control control in stacked)
        {
            Assert.Same(box, control.Parent);
            Assert.True(
                box.ClientRectangle.Contains(control.Bounds),
                $"at {scale:0.00}x {control.Name} ({control.Bounds}) leaves the Auto Tune box " +
                $"({box.ClientRectangle}).");
        }

        for (int a = 0; a < stacked.Length; a++)
        {
            for (int b = a + 1; b < stacked.Length; b++)
            {
                Assert.False(
                    stacked[a].Bounds.IntersectsWith(stacked[b].Bounds),
                    $"at {scale:0.00}x {stacked[a].Name} {stacked[a].Bounds} overlaps " +
                    $"{stacked[b].Name} {stacked[b].Bounds}.");
            }
        }

        // Control.Scale does not regrow text, so only the designer slack is pinned at 1.0; DPI autoscaling preserves its proportion.
        if (scale == 1.0f)
        {
            Assert.True(
                stacked[1].Left - stacked[0].Right >= 8,
                $"only {stacked[1].Left - stacked[0].Right} px between Boosts and its box: " +
                "too little to survive being scaled with the text.");
        }
    }

    private static ReleaseClickCheckBox ShelvesBox(EqWizardPanel panel) =>
        (ReleaseClickCheckBox)Field(panel, "checkBoxShelves");

    private static object Field(EqWizardPanel panel, string name) =>
        typeof(EqWizardPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
