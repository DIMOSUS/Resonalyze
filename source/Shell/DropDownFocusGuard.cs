using System.Runtime.CompilerServices;

namespace Resonalyze;

/// <summary>
/// Cancels the single spurious close a <see cref="ToolStripDropDown"/> can suffer the
/// instant it opens. A borderless custom-chrome window emits an activation change as the
/// dropdown appears, which WinForms reads as a focus-change close, and the menu is gone
/// before it is drawn. This swallows exactly that one close if it lands within
/// <see cref="SpuriousCloseWindowMs"/> of opening, and only once per open, so a genuine
/// later dismissal (a click elsewhere, Esc, choosing an item, a programmatic Close) still
/// closes the menu normally.
/// <para>
/// Half of the answer, and never attached by hand: <see cref="DropDownMenu"/> is where
/// a menu is opened, and it applies this along with the other half.
/// </para>
/// </summary>
internal sealed class DropDownFocusGuard
{
    private const int SpuriousCloseWindowMs = 250;

    private int openedAt;
    private bool armed;

    // Which dropdowns already carry one. Menus rebuilt on every open arrive here as
    // fresh objects and are guarded freshly; the few that are built once and re-shown
    // (the title bar's Tools menu) would otherwise collect a guard per open. Weak
    // keys, so a disposed menu is not held alive by having been guarded.
    private static readonly ConditionalWeakTable<ToolStripDropDown, DropDownFocusGuard>
        Guarded = new();

    /// <summary>
    /// Attaches a guard to <paramref name="dropDown"/> unless it already has one. Each
    /// guard keeps its own state, so a menu rebuilt on every open is guarded freshly.
    /// </summary>
    public static void Attach(ToolStripDropDown dropDown)
    {
        ArgumentNullException.ThrowIfNull(dropDown);
        if (Guarded.TryGetValue(dropDown, out _))
        {
            return;
        }

        var guard = new DropDownFocusGuard();
        Guarded.Add(dropDown, guard);
        dropDown.Opened += guard.OnOpened;
        dropDown.Closing += guard.OnClosing;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        openedAt = Environment.TickCount;
        armed = true;
    }

    private void OnClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange &&
            armed &&
            Environment.TickCount - openedAt < SpuriousCloseWindowMs)
        {
            armed = false;
            e.Cancel = true;
        }
    }
}
