namespace Resonalyze;

/// <summary>
/// Repairs the one way a WinForms button can lose a click: the click it decides not to
/// raise because something else was on top of it.
/// </summary>
/// <remarks>
/// <para>
/// <c>ButtonBase.OnMouseUp</c> raises <see cref="Control.Click"/> only while
/// <c>WindowFromPoint</c> at the release point still answers with the control's own
/// handle. Any other window on that one pixel at that instant takes the click and says
/// nothing: a tooltip still up from wherever the pointer came from, a popup that has
/// not finished going away, anything topmost. The press itself is painted from
/// MouseDown/MouseUp, which always arrive — so the control reacts, nothing happens, and
/// it looks random, because it depends on what happened to be on screen.
/// </para>
/// <para>
/// Measured with a window over the release point: 40 of 40 synthetic clicks reached
/// MouseDown and none reached Click. A real WinForms tooltip there does the same, 20 of
/// 20 — tooltip windows are hit-testable, and every button in this app carries one.
/// </para>
/// <para>
/// So the release is read directly, and the question worth asking is asked explicitly:
/// is the pointer still on the control? That is the part <c>WindowFromPoint</c> was
/// standing in for — pressing a button and sliding off it means "no" — and it is a
/// property of the control's own bounds, not of what is stacked above them.
/// </para>
/// </remarks>
internal sealed class ReleaseClickTracker
{
    private bool pressed;
    private bool clickRaised;

    public void Press(MouseEventArgs e) => pressed = e.Button == MouseButtons.Left;

    public void NoteClick() => clickRaised = true;

    /// <summary>Called before the base class gets the release, which may raise Click.</summary>
    public void BeginRelease() => clickRaised = false;

    /// <summary>
    /// Whether this release was a click the base class did not raise, and the control
    /// therefore owes one.
    /// </summary>
    public bool ClickIsOwed(Control control, MouseEventArgs e)
    {
        bool owed =
            pressed &&
            !clickRaised &&
            e.Button == MouseButtons.Left &&
            !control.IsDisposed &&
            control.ClientRectangle.Contains(e.Location);
        pressed = false;
        return owed;
    }
}

/// <summary>
/// A <see cref="Button"/> whose click cannot be stolen by a window that happens to
/// overlap it; see <see cref="ReleaseClickTracker"/>. Every button in the app is one of
/// these — a plain <see cref="Button"/> works most of the time, which is the whole
/// problem.
/// </summary>
public class ReleaseClickButton : Button
{
    private readonly ReleaseClickTracker tracker = new();

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        tracker.Press(mevent);
        base.OnMouseDown(mevent);
    }

    protected override void OnClick(EventArgs e)
    {
        tracker.NoteClick();
        base.OnClick(e);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        tracker.BeginRelease();
        base.OnMouseUp(mevent);
        if (tracker.ClickIsOwed(this, mevent))
        {
            OnClick(EventArgs.Empty);
            OnMouseClick(mevent);
        }
    }
}

/// <summary>
/// A <see cref="CheckBox"/> that cannot lose a click; see
/// <see cref="ReleaseClickTracker"/>. The toggle happens in <c>OnClick</c>, so a
/// stolen click leaves the box unchanged with nothing to show for the press.
/// </summary>
public class ReleaseClickCheckBox : CheckBox
{
    private readonly ReleaseClickTracker tracker = new();

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        tracker.Press(mevent);
        base.OnMouseDown(mevent);
    }

    protected override void OnClick(EventArgs e)
    {
        tracker.NoteClick();
        base.OnClick(e);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        tracker.BeginRelease();
        base.OnMouseUp(mevent);
        if (tracker.ClickIsOwed(this, mevent))
        {
            OnClick(EventArgs.Empty);
            OnMouseClick(mevent);
        }
    }
}

/// <summary>
/// A <see cref="RadioButton"/> that cannot lose a click; see
/// <see cref="ReleaseClickTracker"/>.
/// </summary>
public class ReleaseClickRadioButton : RadioButton
{
    private readonly ReleaseClickTracker tracker = new();

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        tracker.Press(mevent);
        base.OnMouseDown(mevent);
    }

    protected override void OnClick(EventArgs e)
    {
        tracker.NoteClick();
        base.OnClick(e);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        tracker.BeginRelease();
        base.OnMouseUp(mevent);
        if (tracker.ClickIsOwed(this, mevent))
        {
            OnClick(EventArgs.Empty);
            OnMouseClick(mevent);
        }
    }
}
