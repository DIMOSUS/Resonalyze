using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The Auto Tune Q ceiling. A fit reads one microphone position, and the sharpest
/// bands it can propose correct a peak that only exists there — so the panel caps
/// how narrow a filter the fit may place, well below the Q a strip accepts from the
/// keyboard. These pin the number the fit is handed, and that it survives a restart.
/// </summary>
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
        // Only the narrow end is the user's: a broad band is what the fit should
        // reach for, so the lower bound stays the range every strip accepts.
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
        // The ceiling is a change of behaviour for an existing installation, and
        // that is deliberate: the file says nothing, so the fit is capped the same
        // way a fresh one is.
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
        // The settings file is a format: it can hold a zero, a negative or a
        // number no strip could realise. The box clamps it, and the fit is handed
        // what the box shows.
        using var panel = new EqWizardPanel();

        MeasurementSettingsFile.EqWizardSettings settings = panel.CaptureSettings();
        settings.AutoTuneMaxQ = 0;
        Invoke(panel, "ApplyPersistedSettings", settings);

        Assert.Equal(MaxQBox(panel).Minimum, MaxQBox(panel).Value);
        Assert.Equal((double)MaxQBox(panel).Minimum, Options(panel).QMax);
    }

    private static DarkNumericUpDown MaxQBox(EqWizardPanel panel) =>
        (DarkNumericUpDown)typeof(EqWizardPanel)
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
