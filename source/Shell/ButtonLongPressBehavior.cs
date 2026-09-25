using System.Windows.Forms;

namespace Resonalyze;

/// <summary>Holding the left button for the delay fires the long press and swallows the release click. UI thread only.</summary>
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
        button.MouseUp += (_, e) => HandleMouseUp(button.ClientRectangle.Contains(e.Location));
        button.MouseLeave += (_, _) => timer.Stop();
    }

    /// <summary>Call first in Click: true exactly once after a long press fired.</summary>
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

    // Internal so tests can drive the state machine; the WinForms timer needs a message pump.
    internal void HandleMouseDown(MouseButtons buttons)
    {
        if (buttons != MouseButtons.Left)
        {
            return;
        }

        // A new press ends the last one, whose release click (if any) has been delivered already.
        suppressNextClick = false;
        if (!canTrigger())
        {
            return;
        }

        triggered = false;
        timer.Start();
    }

    // A release off the button raises no click, so there is none left to swallow.
    internal void HandleMouseUp(bool overButton)
    {
        timer.Stop();
        if (!overButton)
        {
            suppressNextClick = false;
        }
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
