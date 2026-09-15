namespace Resonalyze;

/// <summary>The only way menus are opened. Posts the show out of the mouse message (else the activation change swallows it)
/// and attaches a <see cref="DropDownFocusGuard"/>. A click that never arrives is <see cref="ReleaseClickButton"/>'s concern.</summary>
internal static class DropDownMenu
{
    public static void ShowUnder(Control owner, ContextMenuStrip menu)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(menu);
        Post(owner, menu, () => menu.Show(owner, new Point(0, owner.Height)));
    }

    public static void ShowAt(Control host, ContextMenuStrip menu, Point screenPoint)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(menu);
        Post(host, menu, () => menu.Show(screenPoint));
    }

    private static void Post(Control host, ContextMenuStrip menu, Action show)
    {
        DropDownFocusGuard.Attach(menu);
        if (!host.IsHandleCreated)
        {
            // No handle or a test harness: no mouse message to escape.
            show();
            return;
        }

        host.BeginInvoke(() =>
        {
            // A second click can dispose the menu or its panel before the post runs.
            if (!host.IsDisposed && !menu.IsDisposed)
            {
                show();
            }
        });
    }
}
