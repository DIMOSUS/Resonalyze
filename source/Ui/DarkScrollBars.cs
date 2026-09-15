using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Resonalyze.Ui;

/// <summary>OS dark theme for native scrollbars; best-effort no-op on builds without it.</summary>
internal static class DarkScrollBars
{
    // Windows 10 1809: first build with DarkMode_Explorer and the undocumented uxtheme app-mode ordinals.
    private const int FirstDarkModeBuild = 17763;

    // ForceDark: the app is dark even when the OS is light.
    private const int ForceDarkAppMode = 2;

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
    private static extern void FlushMenuThemes();

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(
        nint hWnd,
        string? subAppName,
        string? subIdList);

    private static bool appModeInitialized;

    public static void Apply(Control control)
    {
        if (!IsSupported)
        {
            return;
        }

        EnsureDarkAppMode();
        // Always subscribe: RecreateHandle would silently revert the theme.
        control.HandleCreated += (_, _) => ApplyTheme(control);
        if (control.IsHandleCreated)
        {
            ApplyTheme(control);
        }
    }

    private static void ApplyTheme(Control control)
    {
        try
        {
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }
        catch
        {
        }
    }

    private static void EnsureDarkAppMode()
    {
        if (appModeInitialized)
        {
            return;
        }

        appModeInitialized = true;
        try
        {
            SetPreferredAppMode(ForceDarkAppMode);
            FlushMenuThemes();
        }
        catch
        {
        }
    }

    private static bool IsSupported =>
        Environment.OSVersion.Platform == PlatformID.Win32NT &&
        Environment.OSVersion.Version.Build >= FirstDarkModeBuild;
}
