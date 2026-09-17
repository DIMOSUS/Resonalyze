using System.Diagnostics;

namespace Resonalyze;

/// <summary>A restart the user asked for, carried out after the message loop has ended.</summary>
/// <remarks><see cref="Application.Restart"/> cannot be used here: it starts the replacement the moment
/// <see cref="Application.Exit"/> returns, and the shell cancels its first close to await the audio aborts. The
/// old process would still own <see cref="SingleInstanceGuard"/>, so the replacement would report that Resonalyze
/// is already running and exit.</remarks>
internal static class ApplicationRestart
{
    public static bool IsRequested { get; private set; }

    public static void Request() => IsRequested = true;

    /// <summary>Call only after <see cref="Application.Run(Form)"/> has returned and the guard is disposed.</summary>
    public static void Launch()
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                $"Resonalyze could not restart itself:\r\n\r\n{exception.Message}\r\n\r\n" +
                "Start it again to see the new theme.",
                "Resonalyze",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
