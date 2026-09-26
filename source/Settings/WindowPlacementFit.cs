namespace Resonalyze;

/// <summary>Where saved window bounds go on the screens there are now: a monitor unplugged, rearranged or given a
/// smaller resolution since the window closed must not leave it off screen or larger than the screen.</summary>
internal static class WindowPlacementFit
{
    /// <summary>The saved bounds moved and shrunk into the working area they overlap most, or centred on the primary
    /// working area when they overlap none; null when nothing usable was saved.</summary>
    public static Rectangle? Fit(
        Rectangle saved,
        IReadOnlyList<Rectangle> workingAreas,
        Rectangle primaryWorkingArea)
    {
        if (saved.Width <= 0 || saved.Height <= 0)
        {
            return null;
        }

        Rectangle? best = null;
        long bestOverlap = 0;
        foreach (Rectangle area in workingAreas)
        {
            Rectangle overlap = Rectangle.Intersect(saved, area);
            long overlapArea = (long)overlap.Width * overlap.Height;
            if (overlapArea > bestOverlap)
            {
                best = area;
                bestOverlap = overlapArea;
            }
        }

        Rectangle target = best ?? primaryWorkingArea;
        int width = Math.Min(saved.Width, target.Width);
        int height = Math.Min(saved.Height, target.Height);
        if (best == null)
        {
            return new Rectangle(
                target.Left + (target.Width - width) / 2,
                target.Top + (target.Height - height) / 2,
                width,
                height);
        }

        return new Rectangle(
            Math.Clamp(saved.Left, target.Left, target.Right - width),
            Math.Clamp(saved.Top, target.Top, target.Bottom - height),
            width,
            height);
    }
}
