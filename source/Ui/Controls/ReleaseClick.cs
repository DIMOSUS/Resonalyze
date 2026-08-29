using System.Runtime.InteropServices;

namespace Resonalyze;

/// <summary>
/// Repairs the one way a WinForms button can lose a click: the click it decides not to
/// raise because something else was on top of it.
/// </summary>
/// <remarks>
/// <para>
/// <c>ButtonBase.OnMouseUp</c> puts a <c>WindowFromPoint</c> ownership check in the
/// path to <see cref="Control.Click"/> — one of its conditions, not the only one — so
/// any other window on that one pixel at that instant takes the click and says nothing:
/// a tooltip still up from wherever the pointer came from, a popup that has not
/// finished going away, anything topmost. The press itself is painted from
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
/// <para>
/// It repairs THAT failure and no other. A missing Click is not proof of a stolen one:
/// <c>ButtonBase</c> also withholds it when validation was cancelled, and on its own
/// press-and-capture state. So the hit test is run here as well, and a click is
/// manufactured only when it genuinely comes back with somebody else's window. When it
/// answers with this control, the framework had the release and declined it for a
/// reason of its own, which is a reason to leave it alone.
/// </para>
/// </remarks>
internal sealed class ReleaseClickTracker
{
    private readonly Func<Control, Point, bool> releasePointIsCovered;
    private bool pressed;
    private bool clickRaised;

    public ReleaseClickTracker()
        : this(ReleasePointIsCovered)
    {
    }

    /// <summary>
    /// The same with the hit test handed in, for the tests: they have no windows on
    /// screen and so nothing to cover a release point with.
    /// </summary>
    internal ReleaseClickTracker(Func<Control, Point, bool> releasePointIsCovered) =>
        this.releasePointIsCovered = releasePointIsCovered;

    public void Press(MouseEventArgs e) => pressed = e.Button == MouseButtons.Left;

    public void NoteClick() => clickRaised = true;

    /// <summary>Called before the base class gets the release, which may raise Click.</summary>
    public void BeginRelease() => clickRaised = false;

    /// <summary>
    /// Whether this release was a click the base class did not raise BECAUSE another
    /// window held the release point, and the control therefore owes one.
    /// </summary>
    public bool ClickIsOwed(Control control, MouseEventArgs e)
    {
        bool owed =
            pressed &&
            !clickRaised &&
            e.Button == MouseButtons.Left &&
            !control.IsDisposed &&
            control.ClientRectangle.Contains(e.Location) &&
            releasePointIsCovered(control, e.Location);
        pressed = false;
        return owed;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    // The same question ButtonBase asks, asked again: is the window under the release
    // point this control's own? A control with no window of its own cannot be the
    // answer either, so the framework's test cannot have been the condition that held.
    private static bool ReleasePointIsCovered(Control control, Point location) =>
        !control.IsHandleCreated ||
        WindowFromPoint(control.PointToScreen(location)) != control.Handle;
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
