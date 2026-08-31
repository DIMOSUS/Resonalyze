using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Resonalyze.Screenshots;

/// <summary>
/// Draws the numbered regions and callouts the manual's dense figures carry.
/// </summary>
/// <remarks>
/// The rules the existing figures follow, and the reasons for them:
/// <list type="bullet">
/// <item>A numbered badge goes in an EMPTY corner of its region. A badge over a
/// control's label hides the very thing the figure points at.</item>
/// <item>Where a panel is dense edge to edge, the canvas grows a plain gutter and the
/// badges stand there, joined to their region by a short leader.</item>
/// <item>The legend lives in the Markdown, not in the image: long labels baked into a
/// PNG cannot be edited, translated, or read at another size.</item>
/// <item>A thin cyan box with NO badge marks a detail the prose singles out.</item>
/// </list>
/// </remarks>
internal sealed class Annotate : IDisposable
{
    private static readonly Color Amber = Color.FromArgb(255, 176, 46);
    private static readonly Color Cyan = Color.FromArgb(108, 214, 255);
    private static readonly Color Ink = Color.FromArgb(18, 20, 28);

    private Bitmap canvas;
    private Graphics graphics;
    private int offsetX;

    private Annotate(Bitmap canvas)
    {
        this.canvas = canvas;
        graphics = Prepare(canvas);
    }

    /// <summary>
    /// Opens a figure for annotation, detached from its file. <c>new Bitmap(path)</c>
    /// keeps the file open for the bitmap's lifetime, so saving back over the shot
    /// that was just taken fails with GDI+'s generic error; the copy releases it.
    /// </summary>
    public static Annotate Open(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var loaded = new Bitmap(file);
        return new Annotate(new Bitmap(loaded));
    }

    private static Graphics Prepare(Bitmap bitmap)
    {
        Graphics created = Graphics.FromImage(bitmap);
        created.SmoothingMode = SmoothingMode.AntiAlias;
        created.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        return created;
    }

    /// <summary>
    /// Widens the canvas so badges have somewhere to stand that is not on top of the
    /// panel, filled with the colour sampled at <paramref name="sample"/> — a point
    /// the caller knows is empty background.
    /// </summary>
    public Annotate Gutter(int width, bool onLeft, Point sample)
    {
        Color fill = canvas.GetPixel(sample.X, sample.Y);
        var widened = new Bitmap(canvas.Width + width, canvas.Height);
        using (Graphics target = Graphics.FromImage(widened))
        {
            target.Clear(fill);
            target.DrawImage(canvas, onLeft ? width : 0, 0);
        }

        graphics.Dispose();
        canvas.Dispose();
        canvas = widened;
        graphics = Prepare(canvas);
        offsetX = onLeft ? width : 0;
        return this;
    }

    /// <summary>A numbered region, with an optional leader to a badge outside it.</summary>
    public Annotate Region(
        Rectangle box,
        string number,
        Point badgeAt,
        bool leader = false,
        int badgeRadius = 17)
    {
        Rectangle shifted = Shift(box);
        Point badge = new(badgeAt.X + (badgeAt.X < offsetX ? 0 : offsetX), badgeAt.Y);
        DrawRounded(shifted, Amber, 3, 9);
        if (leader)
        {
            int fromX = badge.X < shifted.Left
                ? badge.X + badgeRadius
                : badge.X - badgeRadius;
            int toX = badge.X < shifted.Left ? shifted.Left : shifted.Right;
            using var pen = new Pen(Amber, 3);
            graphics.DrawLine(pen, fromX, badge.Y, toX, badge.Y);
        }

        DrawBadge(badge, number, badgeRadius);
        return this;
    }

    /// <summary>A called-out detail inside a region: thin, cyan, unnumbered.</summary>
    public Annotate Detail(Rectangle box)
    {
        DrawRounded(Shift(box), Cyan, 2, 6);
        return this;
    }

    /// <summary>A label joined to a ringed point.</summary>
    public Annotate Callout(Point anchor, Point tip, string text, int fontSize = 23)
    {
        Point a = Shift(anchor);
        Point t = Shift(tip);
        using var pen = new Pen(Amber, 4);
        graphics.DrawLine(pen, a, t);
        graphics.DrawEllipse(pen, t.X - 8, t.Y - 8, 16, 16);

        using Font font = new("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        SizeF size = graphics.MeasureString(text, font);
        // Clamped inside the frame, so a label near an edge is nudged in rather than
        // cropped.
        float x = Math.Clamp(a.X - size.Width / 2, 12, canvas.Width - size.Width - 12);
        float y = Math.Clamp(a.Y - size.Height - 16, 12, canvas.Height - size.Height - 12);
        using var background = new SolidBrush(Ink);
        using var foreground = new SolidBrush(Amber);
        graphics.FillRectangle(background, x - 10, y - 8, size.Width + 20, size.Height + 16);
        graphics.DrawString(text, font, foreground, x, y);
        return this;
    }

    /// <summary>An elbow connector with a head, routed through empty space.</summary>
    public Annotate Arrow(params Point[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 2)
        {
            throw new ArgumentException("An arrow needs at least two points.", nameof(points));
        }

        Point[] shifted = [.. points.Select(Shift)];
        using var pen = new Pen(Cyan, 3)
        {
            EndCap = LineCap.ArrowAnchor,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(pen, shifted);
        return this;
    }

    public void Save(string path)
    {
        graphics.Flush();
        canvas.Save(path, ImageFormat.Png);
    }

    private Rectangle Shift(Rectangle box) =>
        new(box.X + offsetX, box.Y, box.Width, box.Height);

    private Point Shift(Point point) => new(point.X + offsetX, point.Y);

    private void DrawRounded(Rectangle box, Color color, int width, int radius)
    {
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(box.X, box.Y, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var pen = new Pen(color, width);
        graphics.DrawPath(pen, path);
    }

    private void DrawBadge(Point centre, string text, int radius = 17)
    {
        using var fill = new SolidBrush(Amber);
        graphics.FillEllipse(fill, centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
        int fontSize = radius == 17 ? 21 : Math.Max(12, radius + 4);
        using Font font = new("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        SizeF size = graphics.MeasureString(text, font);
        using var ink = new SolidBrush(Ink);
        graphics.DrawString(
            text, font, ink, centre.X - size.Width / 2, centre.Y - size.Height / 2);
    }

    public void Dispose()
    {
        graphics.Dispose();
        canvas.Dispose();
    }
}
