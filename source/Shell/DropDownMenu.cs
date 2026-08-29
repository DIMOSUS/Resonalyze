namespace Resonalyze;

/// <summary>
/// The one way a menu is opened from a control in this app. Nothing else calls
/// <see cref="ToolStripDropDown.Show(Control, Point)"/> directly.
/// </summary>
/// <remarks>
/// <para>
/// The obvious <c>menu.Show(button, …)</c> inside a click handler fails
/// intermittently: the menu opens and is gone again before it is ever drawn. Two
/// WinForms quirks produce that, and each needs its own answer. (The other half of
/// "pressed, no menu" is the click never arriving at all, which belongs to
/// <see cref="ReleaseClickButton"/> and is fixed there.)
/// </para>
/// <list type="bullet">
/// <item>Shown from INSIDE the mouse message, the dropdown is swallowed by the
/// activation change that opening it causes. So the show is posted and runs once
/// that message is finished.</item>
/// <item>This app's borderless custom chrome emits a focus change as the dropdown
/// appears, which WinForms reads as a reason to close it. So every menu carries a
/// <see cref="DropDownFocusGuard"/>.</item>
/// </list>
/// <para>
/// Both are cheap, neither is discoverable, and a menu that misses either one works
/// most of the time — which is exactly why they belong here rather than in a recipe
/// each call site is trusted to remember.
/// </para>
/// </remarks>
internal static class DropDownMenu
{
    /// <summary>
    /// Drops <paramref name="menu"/> below <paramref name="owner"/>, the shape every
    /// menu button in the app uses.
    /// </summary>
    public static void ShowUnder(Control owner, ContextMenuStrip menu)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(menu);
        Post(owner, menu, () => menu.Show(owner, new Point(0, owner.Height)));
    }

    /// <summary>
    /// Opens <paramref name="menu"/> at a screen point — for a menu asked for by a
    /// right-click rather than by a button. <paramref name="host"/> is only the
    /// control the show is posted through.
    /// </summary>
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
            // Nothing to post through — a control not realised yet, or a test
            // harness. There is no mouse message to get out of either, so show it.
            show();
            return;
        }

        host.BeginInvoke(() =>
        {
            // The wait is short but it is not nothing: a second click can rebuild and
            // dispose this very menu before the post runs, and the panel it belongs
            // to can be torn down under it.
            if (!host.IsDisposed && !menu.IsDisposed)
            {
                show();
            }
        });
    }
}
