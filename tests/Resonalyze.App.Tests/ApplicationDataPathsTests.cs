namespace Resonalyze.App.Tests;

public sealed class ApplicationDataPathsTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"resonalyze-paths-{Guid.NewGuid():N}");

    [Fact]
    public void InstalledMode_UsesLocalApplicationData()
    {
        string executable = CreateDirectory("app");
        string local = CreateDirectory("local");

        var paths = new ApplicationDataPaths(executable, local);

        Assert.False(paths.IsPortable);
        Assert.Equal(Path.Combine(local, "Resonalyze"), paths.RootDirectory);
        Assert.Equal(
            Path.Combine(local, "Resonalyze", "measurement-settings.json"),
            paths.SettingsFile);
    }

    [Fact]
    public void PortableMarker_KeepsDataBesideExecutable()
    {
        string executable = CreateDirectory("app");
        string local = CreateDirectory("local");
        File.WriteAllText(Path.Combine(executable, "portable.flag"), string.Empty);

        var paths = new ApplicationDataPaths(executable, local);

        Assert.True(paths.IsPortable);
        Assert.Equal(executable, paths.RootDirectory);
        Assert.Empty(paths.Prepare());
    }

    [Fact]
    public void EmptyLocalApplicationData_FallsBackBesideExecutable()
    {
        string executable = CreateDirectory("app");

        var paths = new ApplicationDataPaths(executable, string.Empty);

        Assert.Equal(executable, paths.RootDirectory);
        Assert.Empty(paths.Prepare());
    }

    [Fact]
    public void Prepare_CopiesLegacyFilesAndDirectoriesWithoutOverwriting()
    {
        string executable = CreateDirectory("app");
        string local = CreateDirectory("local");
        File.WriteAllText(
            Path.Combine(executable, "measurement-settings.json"),
            "legacy settings");
        File.WriteAllText(Path.Combine(executable, "crash.log"), "legacy crash");
        File.WriteAllText(
            Path.Combine(executable, "measurement-error.log"),
            "legacy measurement error");
        string legacyOverlay = Path.Combine(executable, "overlays", "FrequencyResponse");
        Directory.CreateDirectory(legacyOverlay);
        File.WriteAllText(Path.Combine(legacyOverlay, "overlay-01.json"), "legacy overlay");
        var paths = new ApplicationDataPaths(executable, local);
        Directory.CreateDirectory(paths.RootDirectory);
        File.WriteAllText(paths.SettingsFile, "current settings");

        IReadOnlyList<string> warnings = paths.Prepare();

        Assert.Empty(warnings);
        Assert.Equal("current settings", File.ReadAllText(paths.SettingsFile));
        Assert.Equal(
            "legacy overlay",
            File.ReadAllText(Path.Combine(
                paths.OverlaysDirectory,
                "FrequencyResponse",
                "overlay-01.json")));
        Assert.Equal("legacy crash", File.ReadAllText(paths.CrashLogFile));
        Assert.Equal(
            "legacy measurement error",
            File.ReadAllText(paths.MeasurementErrorLogFile));
        Assert.True(File.Exists(paths.MigrationMarkerFile));
        Assert.True(File.Exists(Path.Combine(executable, "measurement-settings.json")));
    }

    [Fact]
    public void Prepare_DoesNotRestoreDeletedDataAfterMigrationCompleted()
    {
        string executable = CreateDirectory("app");
        string local = CreateDirectory("local");
        File.WriteAllText(
            Path.Combine(executable, "measurement-settings.json"),
            "legacy settings");
        var paths = new ApplicationDataPaths(executable, local);

        Assert.Empty(paths.Prepare());
        File.Delete(paths.SettingsFile);

        Assert.Empty(paths.Prepare());
        Assert.False(File.Exists(paths.SettingsFile));
        Assert.True(File.Exists(Path.Combine(executable, "measurement-settings.json")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CreateDirectory(string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
