using System.Runtime.CompilerServices;

namespace Resonalyze;

/// <summary>Cancels the one spurious focus-change close a dropdown suffers on open under the borderless chrome (once, within
/// <see cref="SpuriousCloseWindowMs"/>). A real app switch in that window is caught by a foreground check after
/// <see cref="SettledCheckMs"/>, which closes a stranded topmost menu. Applied by <see cref="DropDownMenu"/>.</summary>
internal sealed class DropDownFocusGuard
{
    private const int SpuriousCloseWindowMs = 250;

    // Past the one-frame artifact, short enough that a stranded menu goes unnoticed.
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

    // Weak keys; menus built once and re-shown must not collect a guard per open.
    private static readonly ConditionalWeakTable<ToolStripDropDown, DropDownFocusGuard>
        Guarded = new();

    public static void Attach(ToolStripDropDown dropDown) =>
        Attach(
            dropDown,
            () => Form.ActiveForm != null,
            menu => menu.Close(ToolStripDropDownCloseReason.AppFocusChange));

    /// <summary>Test seam for the foreground check and the close action.</summary>
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
