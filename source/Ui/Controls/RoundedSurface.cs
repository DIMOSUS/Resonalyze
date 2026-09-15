using System.Drawing.Drawing2D;

namespace Resonalyze;

/// <summary>Rounded card painting. Corners are painted with <see cref="ColorBehind"/> rather than cut by a Region (not anti-aliased),
/// which assumes a flat colour behind. Radius is in 96-DPI pixels, scaled at paint time (DeviceDpi is final only with a handle).</summary>
internal static class RoundedSurface
{
    internal const int DefaultCornerRadius = 6;

    /// <summary>Clamped to half the shorter side, beyond which the path folds on itself.</summary>
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

    /// <summary>The control must use <see cref="ControlStyles.UserPaint"/> and carry no framework border.</summary>
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

        GraphicsState state = graphics.Save();

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Half puts pixel centres at x+0.5; the default made the 1 px outline washed out and two pixels thick.
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        graphics.Clear(outside);

        bool bordered = border.A > 0;

        // Inset half a pixel so the 1 px pen lands on the outermost pixel instead of spilling outside.
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
            // Stroke over the fill to cover its anti-aliased pale fringe.
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

    /// <summary>Walks past transparent parents; with no opaque parent keeps its own colour (square corners, not a guess).</summary>
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
