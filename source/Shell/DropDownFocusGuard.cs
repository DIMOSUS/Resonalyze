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
/// <remarks>
/// The reason and the timing are all WinForms offers, and together they cannot tell the
/// artifact from a real application switch that happens to land in the same quarter
/// second — a notification stealing the foreground, an Alt-Tab already in flight. So the
/// cancel is not the whole answer: it buys the menu the moment it needs, and
/// <see cref="SettledCheckMs"/> later, once the churn is over, the guard asks whether
/// this application is actually in front. If it is not, the switch was real and the menu
/// closes — a dropdown is a topmost window, and one left floating over whatever the user
/// moved to is worse than the flicker this exists to prevent. Deciding after the fact
/// rather than at the instant is deliberate: the artifact is not observable while it is
/// happening, and a test made then would have to be right about a transition nothing
/// reports.
/// </remarks>
internal sealed class DropDownFocusGuard
{
    private const int SpuriousCloseWindowMs = 250;

    // Comfortably past the artifact, which is over within a frame, and short enough that
    // a menu stranded by a real switch is gone before it is noticed.
    private const int SettledCheckMs = 300;

    private readonly Func<bool> applicationIsActive;
    private readonly Action<ToolStripDropDown> closeStranded;
    private int openedAt;
    private bool armed;
    private System.Windows.Forms.Timer? settledCheck;

    private DropDownFocusGuard(
        Func<bool> applicationIsActive,
        Action<ToolStripDropDown> closeStranded)
    {
        this.applicationIsActive = applicationIsActive;
        this.closeStranded = closeStranded;
    }

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
    public static void Attach(ToolStripDropDown dropDown) =>
        Attach(
            dropDown,
            () => Form.ActiveForm != null,
            menu => menu.Close(ToolStripDropDownCloseReason.AppFocusChange));

    /// <summary>
    /// The same, with the two things the settled check does to the outside world handed
    /// in: whether this application is the one in front, and how a stranded menu is
    /// closed. Only the tests pass them — they have no foreground window of their own to
    /// read, and no menu on screen to close.
    /// </summary>
    internal static void Attach(
        ToolStripDropDown dropDown,
        Func<bool> applicationIsActive,
        Action<ToolStripDropDown> closeStranded)
    {
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(applicationIsActive);
        ArgumentNullException.ThrowIfNull(closeStranded);
        if (Guarded.TryGetValue(dropDown, out _))
        {
            return;
        }

        var guard = new DropDownFocusGuard(applicationIsActive, closeStranded);
        Guarded.Add(dropDown, guard);
        dropDown.Opened += guard.OnOpened;
        dropDown.Closing += guard.OnClosing;
        dropDown.Disposed += (_, _) => guard.StopSettledCheck();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        openedAt = Environment.TickCount;
        armed = true;
        StopSettledCheck();
    }

    private void OnClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange &&
            armed &&
            Environment.TickCount - openedAt < SpuriousCloseWindowMs)
        {
            armed = false;
            e.Cancel = true;
            StartSettledCheck(sender as ToolStripDropDown);
        }
    }

    private void StartSettledCheck(ToolStripDropDown? dropDown)
    {
        if (dropDown == null)
        {
            return;
        }

        StopSettledCheck();
        var timer = new System.Windows.Forms.Timer { Interval = SettledCheckMs };
        timer.Tick += (_, _) =>
        {
            StopSettledCheck();
            if (!dropDown.IsDisposed && !applicationIsActive())
            {
                closeStranded(dropDown);
            }
        };
        settledCheck = timer;
        timer.Start();
    }

    private void StopSettledCheck()
    {
        settledCheck?.Stop();
        settledCheck?.Dispose();
        settledCheck = null;
    }
}
