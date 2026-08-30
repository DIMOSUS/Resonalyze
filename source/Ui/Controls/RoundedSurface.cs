using System.Drawing.Drawing2D;

namespace Resonalyze;

/// <summary>
/// The rounded rectangle the app's cards are drawn as, and the painting a
/// WinForms control has to do to show one.
/// </summary>
/// <remarks>
/// <para>
/// A control's window stays a rectangle whatever it paints inside itself, and
/// WinForms has no per-pixel alpha to composite a cut corner with. A window
/// region (<see cref="Control.Region"/>) does cut one, but a region is not
/// anti-aliased, so the arc comes out as a staircase. So nothing is cut here:
/// the corners are PAINTED with the colour behind the control
/// (<see cref="ColorBehind"/>) and the rounded shape is drawn on top of them,
/// anti-aliased. The one assumption that costs is that whatever is behind the
/// control is a flat colour — every surface in this app is, and a control over
/// a gradient or an image would show a square patch of the wrong colour in each
/// corner.
/// </para>
/// <para>
/// The radius is stated in 96-DPI pixels and scaled at paint time, like every
/// other hand-drawn dimension in this folder: a radius fixed in device pixels
/// visibly shrinks against the text beside it at 150%. Paint time, not
/// construction time, because <see cref="Control.DeviceDpi"/> is only final once
/// the handle exists in its monitor's context.
/// </para>
/// </remarks>
internal static class RoundedSurface
{
    /// <summary>Corner radius of a card, in 96-DPI pixels.</summary>
    internal const int DefaultCornerRadius = 6;

    /// <summary>
    /// The logical radius in device pixels, clamped so the arcs can never
    /// overrun the shape: at more than half the shorter side the four corners
    /// would meet and the path would fold on itself.
    /// </summary>
    internal static int ScaleRadius(int logicalRadius, int deviceDpi, Size size)
    {
        if (logicalRadius <= 0)
        {
            return 0;
        }

        int dpi = deviceDpi > 0 ? deviceDpi : 96;
        int radius = (int)Math.Round(logicalRadius * dpi / 96.0);
        int limit = Math.Min(size.Width, size.Height) / 2;
        return Math.Clamp(radius, 0, Math.Max(0, limit));
    }

    /// <summary>
    /// The rounded rectangle as a path. A radius of zero gives the plain
    /// rectangle, so a caller does not have to branch on it.
    /// </summary>
    internal static GraphicsPath CreatePath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(
            bounds.Right - diameter,
            bounds.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Paints <paramref name="control"/> as a rounded surface of its own
    /// <see cref="Control.BackColor"/>, outlined in <paramref name="border"/>,
    /// with <paramref name="logicalRadius"/> corners scaled to its display. The
    /// control has to have asked for <see cref="ControlStyles.UserPaint"/> and
    /// must not also carry a framework border.
    /// </summary>
    internal static void Paint(
        Control control,
        Graphics graphics,
        int logicalRadius,
        Color border)
    {
        Paint(
            graphics,
            control.ClientRectangle,
            ScaleRadius(logicalRadius, control.DeviceDpi, control.ClientSize),
            ColorBehind(control),
            control.BackColor,
            border);
    }

    /// <summary>
    /// Fills <paramref name="client"/> with the rounded surface: the corners
    /// take <paramref name="outside"/>, the shape takes <paramref name="fill"/>,
    /// and a one-pixel <paramref name="border"/> outlines it. A fully
    /// transparent fill or border is simply not drawn.
    /// </summary>
    internal static void Paint(
        Graphics graphics,
        Rectangle client,
        int radius,
        Color outside,
        Color fill,
        Color border)
    {
        if (client.Width <= 0 || client.Height <= 0)
        {
            return;
        }

        // The caller may have more to draw on this surface afterwards, and the
        // two modes below are not what it asked for.
        GraphicsState state = graphics.Save();

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Anti-aliased GDI+ puts a pixel's CENTRE on its integer coordinate by
        // default, so a one-pixel line laid on a half-pixel straddles two rows
        // and each gets part of the colour — the outline came out washed out and
        // two pixels thick. Half puts the centre at x+0.5, which is the offset
        // the inset below is written against.
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        // What makes the corners read as cut away: the colour behind the control
        // painted into them before anything else. Clear honours the clip, so a
        // partial invalidation still only costs its own rectangle.
        graphics.Clear(outside);

        bool bordered = border.A > 0;

        // A one-pixel pen strokes half in and half out of the path, so an outline
        // laid on the client edge would spill half of itself outside the control
        // and come out grey. Inset by half a pixel and it lands ON the outermost
        // pixel, exactly where the framework's own FixedSingle border was.
        RectangleF bounds = bordered
            ? new RectangleF(
                client.X + 0.5f,
                client.Y + 0.5f,
                client.Width - 1f,
                client.Height - 1f)
            : client;

        using GraphicsPath path = CreatePath(bounds, radius);
        if (fill.A > 0)
        {
            // Filled first and stroked over: the stroke covers the fill's own
            // anti-aliased edge, which would otherwise show as a pale fringe
            // between the surface and its outline.
            using var brush = new SolidBrush(fill);
            graphics.FillPath(brush, path);
        }

        if (bordered)
        {
            using var pen = new Pen(border);
            graphics.DrawPath(pen, path);
        }

        graphics.Restore(state);
    }

    /// <summary>
    /// The colour behind <paramref name="control"/> — what its cut corners show.
    /// Transparent parents are walked past the way <see cref="Control"/> walks
    /// them for its own simulated transparency; a control with no opaque parent
    /// at all keeps its own colour, which draws square corners rather than a
    /// guess.
    /// </summary>
    internal static Color ColorBehind(Control control)
    {
        for (Control? parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent.BackColor.A == 255)
            {
                return parent.BackColor;
            }
        }

        return control.BackColor;
    }
}
