using System.Windows.Forms;

namespace Resonalyze.Ui;

/// <summary>System-drawn column headers ignore the palette; only they are owner-drawn, the rows stay the system's.</summary>
internal static class ThemedListViewHeaders
{
    public static void Apply(ListView list)
    {
        list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) => DrawHeader(list, e);
        list.DrawItem += (_, e) => e.DrawDefault = true;
        list.DrawSubItem += (_, e) => e.DrawDefault = true;
    }

    private static void DrawHeader(ListView list, DrawListViewColumnHeaderEventArgs e)
    {
        using var background = new SolidBrush(UiPalette.AppBackground);
        e.Graphics.FillRectangle(background, e.Bounds);
        using var separator = new Pen(UiPalette.BorderMuted);
        e.Graphics.DrawLine(
            separator,
            e.Bounds.Right - 1,
            e.Bounds.Top + 2,
            e.Bounds.Right - 1,
            e.Bounds.Bottom - 3);
        TextRenderer.DrawText(
            e.Graphics,
            e.Header?.Text ?? string.Empty,
            e.Font ?? list.Font,
            Rectangle.Inflate(e.Bounds, -6, 0),
            UiPalette.TextPrimary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
