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
        Assert.True(saved.TrySave());

        Assert.Equal(UiTheme.Light, AppearanceSettingsFile.LoadOrDefault(Path_).Theme);
    }

    [Fact]
    public void TheThemeIsWrittenByName_SoTheFileReadsAsWhatItIs()
    {
        Directory.CreateDirectory(directory);
        AppearanceSettingsFile saved = AppearanceSettingsFile.LoadOrDefault(Path_);
        saved.Theme = UiTheme.Light;
        Assert.True(saved.TrySave());

        Assert.Contains("\"Light\"", File.ReadAllText(Path_), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnwritablePath_AnswersFalseAndSaysWhy()
    {
        // A directory where the file belongs: the write fails without needing a permission fixture.
        Directory.CreateDirectory(Path_);
        AppearanceSettingsFile settings = AppearanceSettingsFile.LoadOrDefault(Path_);
        settings.Theme = UiTheme.Light;

        Assert.False(settings.TrySave());
        Assert.NotNull(settings.SaveWarning);
        Assert.False(File.Exists(Path_ + ".tmp"));
    }

    [Fact]
    public void AFailedSave_LeavesTheFileItCouldNotReplace()
    {
        Directory.CreateDirectory(directory);
        AppearanceSettingsFile first = AppearanceSettingsFile.LoadOrDefault(Path_);
        first.Theme = UiTheme.Light;
        Assert.True(first.TrySave());
        string written = File.ReadAllText(Path_);

        // Hold the file open for exclusive writing: the replace fails, the content must survive it.
        using (File.Open(Path_, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            AppearanceSettingsFile second = AppearanceSettingsFile.LoadOrDefault(Path_);
            second.Theme = UiTheme.Dark;
            Assert.False(second.TrySave());
        }

        Assert.Equal(written, File.ReadAllText(Path_));
        Assert.Equal(UiTheme.Light, AppearanceSettingsFile.LoadOrDefault(Path_).Theme);
    }

    [Fact]
    public void ASuccessfulSave_ClearsTheWarningOfTheOneBefore()
    {
        Directory.CreateDirectory(directory);
        AppearanceSettingsFile settings = AppearanceSettingsFile.LoadOrDefault(Path_);
        settings.Theme = UiTheme.Light;
        using (File.Open(Path_, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            Assert.False(settings.TrySave());
        }

        Assert.True(settings.TrySave());
        Assert.Null(settings.SaveWarning);
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
