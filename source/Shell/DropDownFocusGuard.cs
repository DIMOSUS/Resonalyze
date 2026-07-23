namespace Resonalyze;

/// <summary>
/// Cancels the single spurious close a <see cref="ToolStripDropDown"/> can suffer the
/// instant it opens. A borderless custom-chrome window emits an activation change as the
/// dropdown appears, which WinForms reads as a focus-change close — the "button pressed,
/// no menu" symptom. This swallows exactly that one close if it lands within
/// <see cref="SpuriousCloseWindowMs"/> of opening, and only once per open, so a genuine
/// later dismissal (a click elsewhere, Esc, choosing an item, a programmatic Close) still
/// closes the menu normally.
/// </summary>
internal sealed class DropDownFocusGuard
{
    private const int SpuriousCloseWindowMs = 250;

    private int openedAt;
    private bool armed;

    /// <summary>
    /// Attaches a guard to <paramref name="dropDown"/>. Each attached guard keeps its own
    /// state, so a menu rebuilt on every open can be re-guarded freshly.
    /// </summary>
    public static void Attach(ToolStripDropDown dropDown)
    {
        ArgumentNullException.ThrowIfNull(dropDown);
        var guard = new DropDownFocusGuard();
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
