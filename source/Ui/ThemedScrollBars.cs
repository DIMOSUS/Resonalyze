using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Resonalyze.Ui;

/// <summary>The OS theme for native scrollbars, following the palette in force; best-effort no-op on builds without it.</summary>
internal static class ThemedScrollBars
{
    // Windows 10 1809: first build with DarkMode_Explorer and the undocumented uxtheme app-mode ordinals.
    private const int FirstDarkModeBuild = 17763;

    // 1903 replaced ordinal 135 AllowDarkModeForApp(bool) with SetPreferredAppMode(PreferredAppMode),
    // so the same export takes a BOOL below this build and an enum from it on.
    private const int FirstPreferredAppModeBuild = 18362;

    // PreferredAppMode: Default 0, AllowDark 1, ForceDark 2, ForceLight 3. Force* keeps the app on its own
    // theme whatever Windows is set to; AllowDark would leave a light app dark on a dark desktop.
    private const int ForceDarkAppMode = 2;
    private const int ForceLightAppMode = 3;

    // AllowDarkModeForApp on 1809 takes a bool in the same argument.
    private const int AllowDarkModeForApp = 1;
    private const int DenyDarkModeForApp = 0;

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
            SetPreferredAppMode(PreferredAppModeArgument());
            FlushMenuThemes();
        }
        catch
        {
        }
    }

    private static int PreferredAppModeArgument()
    {
        bool light = UiPalette.Theme == UiTheme.Light;
        if (Environment.OSVersion.Version.Build < FirstPreferredAppModeBuild)
        {
            return light ? DenyDarkModeForApp : AllowDarkModeForApp;
        }

        return light ? ForceLightAppMode : ForceDarkAppMode;
    }

    private static bool IsSupported =>
        Environment.OSVersion.Platform == PlatformID.Win32NT &&
        Environment.OSVersion.Version.Build >= FirstDarkModeBuild;
}
