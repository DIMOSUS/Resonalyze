using System.Globalization;

namespace Resonalyze;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Worker-thread exceptions otherwise killed the process with no trace. Log, then let it proceed.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            TryWriteCrashLog(args.ExceptionObject as Exception);
        Application.ThreadException += (_, args) =>
        {
            string? crashLogPath = TryWriteCrashLog(args.Exception);
            string logNotice = crashLogPath == null
                ? "The crash log could not be written."
                : $"Details were written to '{crashLogPath}'.";
            MessageBox.Show(
                $"An unexpected error occurred.\r\n\r\n{args.Exception.Message}\r\n\r\n" +
                logNotice,
                "Resonalyze",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        ApplicationConfiguration.Initialize();

        // Before reading user files: the last instance to close would overwrite the other's.
        using SingleInstanceGuard? instance =
            SingleInstanceGuard.TryAcquire(ApplicationDataPaths.Current.RootDirectory);
        if (instance == null)
        {
            MessageBox.Show(
                "Resonalyze is already running.\r\n\r\n" +
                "Only one copy can use the same settings and measurement history: " +
                "two would overwrite each other's session.",
                "Resonalyze",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        IReadOnlyList<string> dataWarnings = ApplicationDataPaths.Current.Prepare();
        if (dataWarnings.Count > 0)
        {
            MessageBox.Show(
                "Some existing user data could not be prepared or migrated:\r\n\r\n" +
                string.Join("\r\n\r\n", dataWarnings),
                "Resonalyze user data",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        AppProfiler.SetThreadName("UI");

        // Before the first control exists: a designer reads the palette inside InitializeComponent.
        UiPalette.Use(AppearanceSettingsFile.LoadOrDefault().Theme);
        Application.Run(new Form1());
    }

    private static string? TryWriteCrashLog(Exception? exception)
    {
        if (exception == null)
        {
            return null;
        }

        try
        {
            string path = ApplicationDataPaths.Current.CrashLogFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string entry = string.Create(
                CultureInfo.InvariantCulture,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\r\n\r\n");
            File.AppendAllText(path, entry);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
