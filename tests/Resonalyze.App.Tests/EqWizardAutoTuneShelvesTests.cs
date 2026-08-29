using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The Auto Tune Shelves switch. It changes the SHAPE of what a fit returns — a low
/// and a high shelf may join the bells — so it is the user's to make, off unless they
/// ask, and it has to survive a restart the way the rest of the fit's settings do.
/// </summary>
public sealed class EqWizardAutoTuneShelvesTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-eq-shelves-{Guid.NewGuid():N}");

    [Fact]
    public void TheFitIsBellsOnlyUntilTheUserAsksOtherwise()
    {
        using var panel = new EqWizardPanel();

        Assert.False(ShelvesBox(panel).Checked);
        Assert.False(Options(panel).AllowShelves);
    }

    [Fact]
    public void TickingTheBoxIsWhatTheFitIsGiven()
    {
        using var panel = new EqWizardPanel();

        ShelvesBox(panel).Checked = true;

        Assert.True(Options(panel).AllowShelves);
    }

    [Fact]
    public void TheChoiceSurvivesASettingsRoundTrip()
    {
        using var saved = new EqWizardPanel();
        ShelvesBox(saved).Checked = true;

        using var restored = new EqWizardPanel();
        Invoke(restored, "ApplyPersistedSettings", saved.CaptureSettings());

        Assert.True(ShelvesBox(restored).Checked);
        Assert.True(Options(restored).AllowShelves);
    }

    [Fact]
    public void AFileFromBeforeTheSwitchExistedOpensWithBellsOnly()
    {
        // Turning shelves on changes the curve a fit returns, so an existing
        // installation must not find its Auto Tune quietly fitting a new shape.
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
        // Shelves shares the row with Cuts only because the Auto Tune box has no room
        // left below its button. Both are placed at designer coordinates and both
        // auto-size to their text, and text grows faster than the slack between them
        // does — which is exactly how the high-DPI overlaps in this app happened — so
        // the row is measured at the scales a real display uses rather than at 96 DPI
        // alone.
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

        // Control.Scale moves the boxes without regrowing their text, so the loop above
        // proves the positions separate and no more. What protects the row on a real
        // 150% display is that DPI autoscaling grows the coordinates and the glyphs by
        // the SAME factor, which preserves whatever proportion of slack the designer
        // left — so the slack is what gets pinned, at the one scale where the text is
        // measured honestly.
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

    private static EqAutoTuner.Options Options(EqWizardPanel panel) =>
        (EqAutoTuner.Options)typeof(EqWizardPanel)
            .GetMethod(
                "CreateAutoTuneOptions",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [0])!;

    private static void Invoke(EqWizardPanel panel, string name, params object[] arguments) =>
        typeof(EqWizardPanel)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, arguments);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
