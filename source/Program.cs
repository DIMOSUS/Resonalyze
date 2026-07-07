using System.Globalization;

namespace Resonalyze;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Audio callbacks and measurement tasks run on worker threads; without
        // these hooks any exception escaping them killed the process with no
        // trace at all. Best-effort: log, then let the failure proceed.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            TryWriteCrashLog(args.ExceptionObject as Exception);
        Application.ThreadException += (_, args) =>
        {
            TryWriteCrashLog(args.Exception);
            MessageBox.Show(
                $"An unexpected error occurred.\r\n\r\n{args.Exception.Message}\r\n\r\n" +
                $"Details were written to crash.log next to the application.",
                "Resonalyze",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        ApplicationConfiguration.Initialize();
        AppProfiler.SetThreadName("UI");
        Application.Run(new Form1());
    }

    private static void TryWriteCrashLog(Exception? exception)
    {
        if (exception == null)
        {
            return;
        }

        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "crash.log");
            string entry = string.Create(
                CultureInfo.InvariantCulture,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\r\n\r\n");
            File.AppendAllText(path, entry);
        }
        catch
        {
            // Logging must never make a crash worse.
        }
    }
}
