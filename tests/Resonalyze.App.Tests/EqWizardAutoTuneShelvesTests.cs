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
    public void TheBoxSitsBesideCutsOnlyWithoutTouchingIt(float scale)
    {
        // Both controls auto-size at designer coordinates, so the row is measured at real display scales.
        using var panel = new EqWizardPanel();
        if (scale != 1.0f)
        {
            panel.Scale(new SizeF(scale, scale));
        }

        var box = (Control)Field(panel, "panelAutoTune");
        var cutsOnly = (Control)Field(panel, "checkBoxCutsOnly");
        Control shelves = ShelvesBox(panel);

        Assert.Same(box, cutsOnly.Parent);
        Assert.Same(box, shelves.Parent);
        Assert.False(
            cutsOnly.Bounds.IntersectsWith(shelves.Bounds),
            $"at {scale:0.00}x the two checkboxes overlap: {cutsOnly.Bounds} and " +
            $"{shelves.Bounds}.");
        Assert.True(
            box.ClientRectangle.Contains(shelves.Bounds),
            $"at {scale:0.00}x Shelves ({shelves.Bounds}) leaves the Auto Tune box " +
            $"({box.ClientRectangle}).");

        // Control.Scale does not regrow text, so only the designer slack is pinned at 1.0; DPI autoscaling preserves its proportion.
        if (scale == 1.0f)
        {
            Assert.True(
                shelves.Left - cutsOnly.Right >= 8,
                $"only {shelves.Left - cutsOnly.Right} px between the two checkboxes: " +
                "too little to survive being scaled with the text.");
            Assert.True(
                box.ClientRectangle.Right - shelves.Right >= 8,
                $"only {box.ClientRectangle.Right - shelves.Right} px between Shelves " +
                "and the edge of the Auto Tune box.");
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
