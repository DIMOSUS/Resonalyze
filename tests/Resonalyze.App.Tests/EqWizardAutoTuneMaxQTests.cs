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
        var session = new EqWizardSession();

        Assert.Equal(6.0m, session.AutoTuneMaxQ);
        Assert.Equal(6.0, EqWizardFit.Options(session, 0).QMax);
    }

    [Fact]
    public void TheCeilingTheUserChoseIsWhatTheFitIsGiven()
    {
        var session = new EqWizardSession();

        session.SetAutoTuneMaxQ(2.5m);

        Assert.Equal(2.5, EqWizardFit.Options(session, 0).QMax);
        Assert.Equal(2.5, EqWizardFit.Policy(session).MaxQ);
    }

    [Fact]
    public void TheWidestBandIsStillTheStripsOwnFloor()
    {
        var session = new EqWizardSession();
        session.SetAutoTuneMaxQ(4.0m);
        using var strip = new PeqSlotControl();

        Assert.Equal((double)strip.QInput.Minimum, EqWizardFit.Options(session, 0).QMin);
    }

    [Fact]
    public void TheChoiceSurvivesASettingsRoundTrip()
    {
        var saved = new EqWizardSession();
        saved.SetAutoTuneMaxQ(3.5m);

        var restored = new EqWizardSession();
        restored.ApplySettings(saved.CaptureSettings());

        Assert.Equal(3.5m, restored.AutoTuneMaxQ);
        Assert.Equal(3.5, EqWizardFit.Options(restored, 0).QMax);
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
        var session = new EqWizardSession();
        MeasurementSettingsFile.EqWizardSettings settings = session.CaptureSettings();
        settings.AutoTuneMaxQ = 0;

        session.ApplySettings(settings);

        Assert.Equal(EqWizardLimits.AutoTuneMaxQ.Minimum, session.AutoTuneMaxQ);
        Assert.Equal((double)EqWizardLimits.AutoTuneMaxQ.Minimum, EqWizardFit.Options(session, 0).QMax);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
