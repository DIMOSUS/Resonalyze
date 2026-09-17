using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

public sealed class AppearanceSettingsFileTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "resonalyze-appearance-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(directory, "appearance.json");

    [Fact]
    public void AMissingFile_StartsDark()
    {
        Assert.Equal(UiTheme.Dark, AppearanceSettingsFile.LoadOrDefault(Path_).Theme);
    }

    [Fact]
    public void TheChosenTheme_SurvivesARoundTrip()
    {
        Directory.CreateDirectory(directory);
        AppearanceSettingsFile saved = AppearanceSettingsFile.LoadOrDefault(Path_);
        saved.Theme = UiTheme.Light;
        saved.Save();

        Assert.Equal(UiTheme.Light, AppearanceSettingsFile.LoadOrDefault(Path_).Theme);
    }

    [Fact]
    public void TheThemeIsWrittenByName_SoTheFileReadsAsWhatItIs()
    {
        Directory.CreateDirectory(directory);
        AppearanceSettingsFile saved = AppearanceSettingsFile.LoadOrDefault(Path_);
        saved.Theme = UiTheme.Light;
        saved.Save();

        Assert.Contains("\"Light\"", File.ReadAllText(Path_), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"Theme\": \"Sepia\" }")]
    [InlineData("")]
    public void AFileItCannotUnderstand_FallsBackToTheDefault(string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path_, content);

        Assert.Equal(UiTheme.Dark, AppearanceSettingsFile.LoadOrDefault(Path_).Theme);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
