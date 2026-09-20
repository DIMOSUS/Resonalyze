using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardAutoTuneBoostsTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-eq-boosts-{Guid.NewGuid():N}");

    [Fact]
    public void ByDefaultTheFitOnlyRefillsItsOwnCuts()
    {
        var session = new EqWizardSession();

        Assert.Equal(EqAutoTuneBoosts.RefillOwnCuts, session.Boosts);
        Assert.Equal(EqAutoTuneBoosts.RefillOwnCuts, EqWizardFit.Options(session, 0).Boosts);
        Assert.Equal(EqAutoTuneBoosts.RefillOwnCuts, EqWizardFit.Policy(session).Boosts);
    }

    [Theory]
    [InlineData(EqAutoTuneBoosts.Off)]
    [InlineData(EqAutoTuneBoosts.RefillOwnCuts)]
    public void ABankThatMayNotLiftTheCurve_MovesThePreampUnderAZeroCeiling(EqAutoTuneBoosts boosts)
    {
        var session = new EqWizardSession();
        session.SetBoosts(boosts);

        EqAutoTuner.Options options = EqWizardFit.Options(session, 0);

        Assert.Equal(boosts, options.Boosts);
        Assert.Equal((double)EqWizardLimits.Preamp.Minimum, options.PreampMinDb);
        Assert.Equal((double)EqWizardLimits.Preamp.Maximum, options.PreampMaxDb);
        Assert.Equal(0, options.TotalGainMaxDb);
    }

    [Fact]
    public void AllowedBoosts_KeepThePreampTheUserSet()
    {
        var session = new EqWizardSession();
        session.SetBoosts(EqAutoTuneBoosts.Allowed);
        session.Bank.Load([], -3);

        EqAutoTuner.Options options = EqWizardFit.Options(session, 0);

        Assert.Equal(-3, options.PreampMinDb);
        Assert.Equal(-3, options.PreampMaxDb);
        Assert.Equal(double.PositiveInfinity, options.TotalGainMaxDb);
    }

    [Theory]
    [InlineData(EqAutoTuneBoosts.Off, true)]
    [InlineData(EqAutoTuneBoosts.RefillOwnCuts, true)]
    [InlineData(EqAutoTuneBoosts.Allowed, false)]
    public void TheChoiceSurvivesASettingsRoundTrip_AndOlderBuildsReadItsCutsOnlyFlag(
        EqAutoTuneBoosts boosts, bool legacyCutsOnly)
    {
        var saved = new EqWizardSession();
        saved.SetBoosts(boosts);

        MeasurementSettingsFile.EqWizardSettings settings = saved.CaptureSettings();
        var restored = new EqWizardSession();
        restored.ApplySettings(settings);

        Assert.Equal(legacyCutsOnly, settings.CutsOnly);
        Assert.Equal(boosts, restored.Boosts);
    }

    [Theory]
    [InlineData("true", EqAutoTuneBoosts.RefillOwnCuts)]
    [InlineData("false", EqAutoTuneBoosts.Allowed)]
    public void AFileFromBeforeTheChoiceExisted_KeepsItsPromiseAboutLifting(string cutsOnly, EqAutoTuneBoosts expected)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "measurement-settings.json");
        File.WriteAllText(
            path,
            $"{{ \"SchemaVersion\": 12, \"EqWizard\": {{ \"CutsOnly\": {cutsOnly} }} }}");

        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);
        var session = new EqWizardSession();
        session.ApplySettings(settings.EqWizard);

        Assert.Null(settings.LoadWarning);
        Assert.Equal(expected, session.Boosts);
    }

    [Fact]
    public void AnUndefinedModeInTheFile_FallsBackToTheLegacyFlag()
    {
        var settings = new MeasurementSettingsFile.EqWizardSettings
        {
            CutsOnly = true,
            AutoTuneBoosts = (EqAutoTuneBoosts)42
        };

        Assert.Equal(EqAutoTuneBoosts.RefillOwnCuts, settings.ResolveAutoTuneBoosts());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
