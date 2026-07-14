namespace Resonalyze.App.Tests;

public sealed class MeasurementSettingsFileRecoveryTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-settings-{Guid.NewGuid():N}");

    [Fact]
    public void LoadOrDefault_CorruptFileIsBackedUpAndReported()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "measurement-settings.json");
        File.WriteAllText(path, "{ not json");

        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.NotNull(settings.LoadWarning);
        Assert.Contains("could not be loaded", settings.LoadWarning);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".backup"));
    }

    [Fact]
    public void LoadOrDefault_UnsupportedVersionIsBackedUpAndReported()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "measurement-settings.json");
        File.WriteAllText(path, "{ \"SchemaVersion\": 999 }");

        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);

        Assert.Contains("version 999 is not supported", settings.LoadWarning);
        Assert.True(File.Exists(path + ".backup"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
