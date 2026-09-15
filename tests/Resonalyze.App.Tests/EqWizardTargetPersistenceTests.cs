namespace Resonalyze.App.Tests;

/// <summary>An imported house curve is stored by value, not by path.</summary>
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
