using System.Runtime.InteropServices;

namespace Resonalyze;

/// <summary>
/// Repairs the one way a WinForms button can lose a click: the click it declines to
/// raise because another window was on top of the point the mouse came up at.
/// </summary>
/// <remarks>
/// <para>
/// <c>ButtonBase.OnMouseUp</c> puts a <c>WindowFromPoint</c> ownership check in the
/// path to <see cref="Control.Click"/> — one of its conditions, not the only one — so
/// any other window on that one pixel at that instant takes the click and says nothing:
/// a tooltip still up from wherever the pointer came from, a popup that has not
/// finished going away, anything topmost. The press itself is painted from
/// MouseDown/MouseUp, which always arrive, so the control reacts, nothing happens, and
/// it looks random, because it depends on what happened to be on screen.
/// </para>
/// <para>
/// Measured with a window over the release point: 40 of 40 synthetic clicks reached
/// MouseDown and none reached Click. A real WinForms tooltip there does the same, 20 of
/// 20 — tooltip windows are hit-testable, and every button in this app carries one.
/// </para>
/// <para>
/// The repair is NOT to raise the click instead. A missing click is not proof of a
/// stolen one: the framework also withholds it on cancelled validation and on
/// <c>ButtonBase</c>'s own press and capture state, and a hit test that fails proves
/// one of those conditions is present — never that the others are absent. So nothing
/// here decides whether a click is due. Only the coordinate the framework runs its own
/// hit test at is moved, to another point on the same control that is not covered, and
/// the framework then answers the whole question itself, validation included.
/// </para>
/// <para>
/// What the move costs: the MouseUp and MouseClick events report that point rather than
/// the one the mouse actually came up at, in the covered case only, and always
/// somewhere inside the same control. A release OUTSIDE the control is left alone —
/// pressing a button and sliding off it means "no", and the framework's own hit test is
/// what enforces that. So is a control covered edge to edge, which has no free point to
/// offer and keeps whatever the framework decides.
/// </para>
/// </remarks>
internal static class ReleaseClick
{
    // Far enough in that a corner pixel is not on the control's own border, which some
    // themes draw as part of the parent.
    private const int Inset = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    /// <summary>
    /// The release to hand the base class: the one that arrived, unless its point
    /// belongs to another window and the control has a free one to offer instead.
    /// </summary>
    public static MouseEventArgs RepairHitTest(Control control, MouseEventArgs release) =>
        RepairHitTest(control, release, WindowFromPoint);

    /// <summary>
    /// The same with the hit test handed in, for the tests: they have no windows on
    /// screen and so nothing to cover a release point with.
    /// </summary>
    internal static MouseEventArgs RepairHitTest(
        Control control,
        MouseEventArgs release,
        Func<Point, IntPtr> windowUnderPoint)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(windowUnderPoint);
        if (release.Button != MouseButtons.Left ||
            control.IsDisposed ||
            !control.IsHandleCreated ||
            !control.ClientRectangle.Contains(release.Location))
        {
            return release;
        }

        IntPtr handle = control.Handle;
        if (windowUnderPoint(control.PointToScreen(release.Location)) == handle)
        {
            return release;
        }

        foreach (Point candidate in FreePointCandidates(control, release.Location))
        {
            if (windowUnderPoint(control.PointToScreen(candidate)) == handle)
            {
                return new MouseEventArgs(
                    release.Button,
                    release.Clicks,
                    candidate.X,
                    candidate.Y,
                    release.Delta);
            }
        }

        return release;
    }

    // The corners, the edge midpoints and the centre, nearest to the real release point
    // first: whatever is covering it is one window with one edge, so a point a few
    // pixels away is usually already clear, and the closest free one is the least
    // distorted stand-in for where the mouse actually came up.
    private static IEnumerable<Point> FreePointCandidates(Control control, Point release)
    {
        Rectangle bounds = control.ClientRectangle;
        int left = bounds.Left + Inset;
        int right = bounds.Right - 1 - Inset;
        int top = bounds.Top + Inset;
        int bottom = bounds.Bottom - 1 - Inset;
        if (right < left || bottom < top)
        {
            yield break;
        }

        int middleX = (left + right) / 2;
        int middleY = (top + bottom) / 2;
        Point[] candidates =
        [
            new(left, top), new(middleX, top), new(right, top),
            new(left, middleY), new(middleX, middleY), new(right, middleY),
            new(left, bottom), new(middleX, bottom), new(right, bottom)
        ];
        foreach (Point candidate in candidates.OrderBy(point => Distance(point, release)))
        {
            yield return candidate;
        }
    }

    private static int Distance(Point from, Point to)
    {
        int dx = from.X - to.X;
        int dy = from.Y - to.Y;
        return (dx * dx) + (dy * dy);
    }
}

/// <summary>
/// A <see cref="Button"/> whose click cannot be taken by a window that happens to
/// overlap it; see <see cref="ReleaseClick"/>. Every button in the app is one of these —
/// a plain <see cref="Button"/> works most of the time, which is the whole problem.
/// </summary>
public class ReleaseClickButton : Button
{
    protected override void OnMouseUp(MouseEventArgs mevent) =>
        base.OnMouseUp(ReleaseClick.RepairHitTest(this, mevent));
}

/// <summary>
/// A <see cref="CheckBox"/> that cannot lose a click; see <see cref="ReleaseClick"/>.
/// The toggle happens in <c>OnClick</c>, so a taken click leaves the box unchanged with
/// nothing to show for the press.
/// </summary>
public class ReleaseClickCheckBox : CheckBox
{
    protected override void OnMouseUp(MouseEventArgs mevent) =>
        base.OnMouseUp(ReleaseClick.RepairHitTest(this, mevent));
}

/// <summary>
/// A <see cref="RadioButton"/> that cannot lose a click; see <see cref="ReleaseClick"/>.
/// </summary>
public class ReleaseClickRadioButton : RadioButton
{
    protected override void OnMouseUp(MouseEventArgs mevent) =>
        base.OnMouseUp(ReleaseClick.RepairHitTest(this, mevent));
}
