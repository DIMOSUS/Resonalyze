using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Resonalyze.Ui;

/// <summary>
/// Renders a control's native (non-client) scrollbars in the OS dark theme so
/// they fit the application's dark palette instead of the default light bar.
/// Best-effort: on Windows builds without the dark scrollbar theme the calls are
/// simply no-ops and the control keeps its default scrollbars.
/// </summary>
internal static class DarkScrollBars
{
    // The first Windows 10 build (1809) that ships the DarkMode_Explorer theme
    // and the undocumented uxtheme app-mode entry points used below.
    private const int FirstDarkModeBuild = 17763;

    // uxtheme app-mode: 2 = ForceDark, so the dark scrollbar theme applies even
    // when the OS itself is set to light — the app is dark regardless.
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

    /// <summary>
    /// Applies the dark scrollbar theme to <paramref name="control"/>, deferring
    /// until its handle exists. Safe to call for any control; it only affects
    /// controls that actually show scrollbars.
    /// </summary>
    public static void Apply(Control control)
    {
        if (!IsSupported)
        {
            return;
        }

        EnsureDarkAppMode();
        // Always subscribe: WinForms can recreate the handle (RecreateHandle on
        // certain property changes), which would silently revert the theme.
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
            // The theme entry point is unavailable on this build; leave the
            // control on its default scrollbars.
        }
    }

    // Opting the process into dark mode is what lets the DarkMode_Explorer theme
    // actually darken the scrollbars; done once, best-effort.
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
            // Older uxtheme without the app-mode ordinals; the per-control theme
            // call below is still attempted and simply may not take effect.
        }
    }

    private static bool IsSupported =>
        Environment.OSVersion.Platform == PlatformID.Win32NT &&
        Environment.OSVersion.Version.Build >= FirstDarkModeBuild;
}
