using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>A fit reads one position, so its sharpest bands would correct a peak only there; the Q ceiling is capped below the strip's.</summary>
public sealed class EqWizardAutoTuneMaxQTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-eq-maxq-{Guid.NewGuid():N}");

    [Fact]
    public void ByDefaultTheFitMayNotGoNarrowerThanSix()
    {
        using var panel = new EqWizardPanel();

        Assert.Equal(6.0m, MaxQBox(panel).Value);
        Assert.Equal(6.0, Options(panel).QMax);
    }

    [Fact]
    public void TheCeilingTheUserTypedIsWhatTheFitIsGiven()
    {
        using var panel = new EqWizardPanel();

        MaxQBox(panel).Value = 2.5m;

        Assert.Equal(2.5, Options(panel).QMax);
    }

    [Fact]
    public void TheWidestBandIsStillTheStripsOwnFloor()
    {
        using var panel = new EqWizardPanel();

        MaxQBox(panel).Value = 4.0m;

        Assert.Equal(PeqSlotControl.MinimumQ, Options(panel).QMin);
    }

    [Fact]
    public void TheChoiceSurvivesASettingsRoundTrip()
    {
        using var saved = new EqWizardPanel();
        MaxQBox(saved).Value = 3.5m;

        using var restored = new EqWizardPanel();
        Invoke(restored, "ApplyPersistedSettings", saved.CaptureSettings());

        Assert.Equal(3.5m, MaxQBox(restored).Value);
        Assert.Equal(3.5, Options(restored).QMax);
    }

    [Fact]
    public void AFileFromBeforeTheCeilingExistedOpensAtTheDefault()
    {
        // Deliberate behaviour change: a file without the field is capped like a fresh install.
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "measurement-settings.json");
        File.WriteAllText(
            path,
            "{ \"SchemaVersion\": 12, \"EqWizard\": { \"CutsOnly\": true } }");

        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.Null(settings.LoadWarning);
        Assert.Equal(6.0, settings.EqWizard.AutoTuneMaxQ);
    }

    [Fact]
    public void AFileHoldingAnUnusableCeilingIsClampedRatherThanObeyed()
    {
        using var panel = new EqWizardPanel();

        MeasurementSettingsFile.EqWizardSettings settings = panel.CaptureSettings();
        settings.AutoTuneMaxQ = 0;
        Invoke(panel, "ApplyPersistedSettings", settings);

        Assert.Equal(MaxQBox(panel).Minimum, MaxQBox(panel).Value);
        Assert.Equal((double)MaxQBox(panel).Minimum, Options(panel).QMax);
    }

    private static ThemedNumericUpDown MaxQBox(EqWizardPanel panel) =>
        (ThemedNumericUpDown)typeof(EqWizardPanel)
            .GetField("numericQMax", BindingFlags.NonPublic | BindingFlags.Instance)!
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
