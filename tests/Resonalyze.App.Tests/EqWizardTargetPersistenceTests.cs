namespace Resonalyze.App.Tests;

/// <summary>
/// The EQ Wizard's target lives in the settings file. A house curve imported into
/// it is carried there by value — the file is what the next launch opens on, and
/// a path to wherever the curve was imported from would be a promise the settings
/// cannot keep.
/// </summary>
public sealed class EqWizardTargetPersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-eq-target-{Guid.NewGuid():N}");

    [Fact]
    public void AnImportedTargetComesBackAsTheSameCurve()
    {
        string path = NewSettingsPath();
        ImportedTargetCurve imported = ImportedTargetCurve.FromPoints(
            "house.txt",
            [
                new OverlayPoint(30, 9),
                new OverlayPoint(100, 6),
                new OverlayPoint(1_000, 0),
                new OverlayPoint(10_000, -3)
            ])!;
        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);
        settings.EqWizard.TargetImportedName = imported.Name;
        settings.EqWizard.TargetImportedCurve = imported.ToStorage();
        settings.Save();

        MeasurementSettingsFile reloaded = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.Null(reloaded.LoadWarning);
        Assert.Equal(
            imported,
            ImportedTargetCurve.FromStorage(
                reloaded.EqWizard.TargetImportedName,
                reloaded.EqWizard.TargetImportedCurve));
    }

    [Fact]
    public void AFileFromBeforeTheImportOpensOnItsParametricShape()
    {
        // No imported curve is the ordinary state, and it has to stay readable:
        // every settings file written before this existed is one of them.
        string path = NewSettingsPath();
        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);
        settings.EqWizard.Preset = TargetPreset.Car;
        settings.Save();

        MeasurementSettingsFile reloaded = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.Null(reloaded.EqWizard.TargetImportedName);
        Assert.Null(reloaded.EqWizard.TargetImportedCurve);
        Assert.Null(ImportedTargetCurve.FromStorage(
            reloaded.EqWizard.TargetImportedName,
            reloaded.EqWizard.TargetImportedCurve));
    }

    private string NewSettingsPath()
    {
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
