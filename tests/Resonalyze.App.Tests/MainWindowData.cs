namespace Resonalyze.App.Tests;

/// <summary>The main window loads and saves the test host's portable settings and history; windows in this collection
/// take turns, each starts from neither file and leaves neither behind.</summary>
[CollectionDefinition(Name)]
public sealed class MainWindowData
{
    internal const string Name = "Main window data";

    public static void Reset()
    {
        File.Delete(ApplicationDataPaths.Current.SettingsFile);
        File.Delete(ApplicationDataPaths.Current.HistoryFile);
    }
}
