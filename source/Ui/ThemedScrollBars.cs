using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Resonalyze.Ui;

/// <summary>The OS theme for native scrollbars, following the palette in force; best-effort no-op on builds without it.</summary>
internal static class ThemedScrollBars
{
    // Windows 10 1809: first build with DarkMode_Explorer and the undocumented uxtheme app-mode ordinals.
    private const int FirstDarkModeBuild = 17763;

    // Force*: the app keeps its own theme whatever the OS is set to.
    private const int ForceLightAppMode = 1;
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

        EnsureAppMode();
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
            SetWindowTheme(
                control.Handle,
                UiPalette.Theme == UiTheme.Light ? "Explorer" : "DarkMode_Explorer",
                null);
        }
        catch
        {
        }
    }

    private static void EnsureAppMode()
    {
        if (appModeInitialized)
        {
            return;
        }

        appModeInitialized = true;
        try
        {
            SetPreferredAppMode(
                UiPalette.Theme == UiTheme.Light ? ForceLightAppMode : ForceDarkAppMode);
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
