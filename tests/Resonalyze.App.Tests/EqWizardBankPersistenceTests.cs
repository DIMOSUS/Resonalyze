using Resonalyze.Dsp;
using PeqBandSettings = Resonalyze.MeasurementSettingsFile.PeqBandSettings;

namespace Resonalyze.App.Tests;

public sealed class EqWizardBankPersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-eq-bank-{Guid.NewGuid():N}");

    [Fact]
    public void SavedBankComesBackInTheSameOrder()
    {
        string path = NewSettingsPath();
        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);
        settings.EqWizard.Bands = new List<MeasurementSettingsFile.PeqBandSettings>
        {
            new() { FrequencyHz = 4000, Q = 8.5, GainDb = -4.5 },
            new() { FrequencyHz = 63, Q = 1.2, GainDb = 3 },
            new() { FrequencyHz = 1000, Q = 5, GainDb = 0 }
        };
        settings.EqWizard.BandCount = 3;
        settings.EqWizard.PreampDb = -6.5;
        settings.Save();

        MeasurementSettingsFile reloaded = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.Null(reloaded.LoadWarning);
        List<MeasurementSettingsFile.PeqBandSettings> bands = reloaded.EqWizard.Bands!;
        Assert.Equal(3, bands.Count);
        // Exported profiles number filters by order, so no sorting by frequency.
        Assert.Equal(4000, bands[0].FrequencyHz);
        Assert.Equal(8.5, bands[0].Q);
        Assert.Equal(-4.5, bands[0].GainDb);
        Assert.Equal(63, bands[1].FrequencyHz);
        Assert.Equal(1000, bands[2].FrequencyHz);
        Assert.Equal(-6.5, reloaded.EqWizard.PreampDb);
    }

    [Fact]
    public void AnEmptyBankIsAStateOfItsOwn()
    {
        // Only an absent list (older file) means rebuild from the count.
        string path = NewSettingsPath();
        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);
        settings.EqWizard.Bands = new List<MeasurementSettingsFile.PeqBandSettings>();
        settings.Save();

        MeasurementSettingsFile reloaded = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.NotNull(reloaded.EqWizard.Bands);
        Assert.Empty(reloaded.EqWizard.Bands!);
    }

    [Fact]
    public void AShapeNoMemberMatchesIsAcceptedByTheFileAndNormalisedOnLoad()
    {
        // The enum converter accepts out-of-range numbers; the file layer must still load (the panel normalises to a bell).
        string path = NewSettingsPath();
        File.WriteAllText(
            path,
            "{ \"SchemaVersion\": 10, \"EqWizard\": { \"Bands\": [ " +
            "{ \"FrequencyHz\": 1000, \"Q\": 2, \"GainDb\": 3, \"Type\": 99 } ] } }");

        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.Null(settings.LoadWarning);
        PeqBandSettings band = Assert.Single(settings.EqWizard.Bands!);
        Assert.False(Enum.IsDefined(band.Type));
        Assert.False(band.Type.IsShelving());
    }

    [Fact]
    public void AFileFromBeforeTheBankWasPersistedKeepsItsFilterCount()
    {
        string path = NewSettingsPath();
        File.WriteAllText(
            path,
            "{ \"SchemaVersion\": 9, \"EqWizard\": { \"BandCount\": 6 } }");

        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.Null(settings.LoadWarning);
        Assert.Null(settings.EqWizard.Bands);
        Assert.Equal(6, settings.EqWizard.BandCount);
        Assert.Equal(0, settings.EqWizard.PreampDb);
    }

    private string NewSettingsPath()
    {
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "measurement-settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
