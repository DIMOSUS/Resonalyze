using System.Windows.Forms;

namespace Resonalyze;

/// <summary>
/// Long-press detection for a button: holding the left mouse button down for
/// the configured delay fires <c>onLongPress</c> and swallows the click that
/// the release would otherwise deliver. Replaces the timer + two flags that
/// lived as raw fields on <c>Form1</c> (the record button uses it to cancel
/// a whole averaged-measurement series). UI-thread only.
/// </summary>
internal sealed class ButtonLongPressBehavior : IDisposable
{
    private readonly System.Windows.Forms.Timer timer;
    private readonly Func<bool> canTrigger;
    private readonly Func<Task> onLongPress;
    private bool triggered;
    private bool suppressNextClick;

    public ButtonLongPressBehavior(
        Control button,
        int longPressMilliseconds,
        Func<bool> canTrigger,
        Func<Task> onLongPress)
    {
        this.canTrigger = canTrigger;
        this.onLongPress = onLongPress;
        timer = new System.Windows.Forms.Timer { Interval = longPressMilliseconds };
        timer.Tick += async (_, _) => await HandleLongPressElapsedAsync();
        button.MouseDown += (_, e) => HandleMouseDown(e.Button);
        button.MouseUp += (_, _) => timer.Stop();
        button.MouseLeave += (_, _) => timer.Stop();
    }

    /// <summary>
    /// Called at the start of the button's Click handler: stops a pending
    /// long-press countdown and returns true exactly once after a long press
    /// fired, so the click that ends the press is swallowed.
    /// </summary>
    public bool ConsumeClickSuppression()
    {
        timer.Stop();
        if (!suppressNextClick)
        {
            return false;
        }

        suppressNextClick = false;
        return true;
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Dispose();
    }

    // The two handlers below are internal so tests can drive the state
    // machine directly — the WinForms timer needs a message pump to tick.
    internal void HandleMouseDown(MouseButtons buttons)
    {
        if (buttons != MouseButtons.Left || !canTrigger())
        {
            return;
        }

        triggered = false;
        suppressNextClick = false;
        timer.Start();
    }

    internal async Task HandleLongPressElapsedAsync()
    {
        timer.Stop();
        if (!canTrigger() || triggered)
        {
            return;
        }

        triggered = true;
        suppressNextClick = true;
        await onLongPress();
    }
}
