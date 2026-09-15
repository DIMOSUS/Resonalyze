using System.Runtime.InteropServices;

namespace Resonalyze;

/// <summary>Moves a covered release point to a free point on the same control so WinForms' own hit test passes; see AGENTS.md (ReleaseClick*).</summary>
internal static class ReleaseClick
{
    // Clear of the control's own border, which some themes draw as part of the parent.
    private const int Inset = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    public static MouseEventArgs RepairHitTest(Control control, MouseEventArgs release) =>
        RepairHitTest(control, release, WindowFromPoint);

    /// <summary>Hit test injected for tests, which have no windows on screen.</summary>
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

    // Nearest to the real release first: the closest free point is the least distorted stand-in.
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

public class ReleaseClickButton : Button
{
    protected override void OnMouseUp(MouseEventArgs mevent) =>
        base.OnMouseUp(ReleaseClick.RepairHitTest(this, mevent));
}

public class ReleaseClickCheckBox : CheckBox
{
    protected override void OnMouseUp(MouseEventArgs mevent) =>
        base.OnMouseUp(ReleaseClick.RepairHitTest(this, mevent));
}

public class ReleaseClickRadioButton : RadioButton
{
    protected override void OnMouseUp(MouseEventArgs mevent) =>
        base.OnMouseUp(ReleaseClick.RepairHitTest(this, mevent));
}
