namespace Resonalyze.App.Tests;

/// <summary>The main window loads and saves the test host's portable settings, history and Virtual DSP autosave; windows
/// in this collection take turns, each starts from none of them and leaves none behind.</summary>
[CollectionDefinition(Name)]
public sealed class MainWindowData
{
    internal const string Name = "Main window data";

    public static void Reset()
    {
        File.Delete(ApplicationDataPaths.Current.SettingsFile);
        File.Delete(ApplicationDataPaths.Current.HistoryFile);
        // Its folder appears only once a tool writes there; File.Delete throws on a missing folder.
        string autosave = VirtualCrossoverProjectFile.GetPath();
        if (File.Exists(autosave))
        {
            File.Delete(autosave);
        }
    }
}
