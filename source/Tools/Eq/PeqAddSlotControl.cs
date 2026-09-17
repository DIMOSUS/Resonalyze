using System.Drawing.Drawing2D;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed class PeqAddBandEventArgs : EventArgs
{
    public PeqAddBandEventArgs(PeqBandType type)
    {
        Type = type;
    }

    public PeqBandType Type { get; }
}

/// <summary>
/// Trailing "add a filter" tile, one zone per shape (adding is the most repeated action; a menu costs a click).
/// Hidden once the bank is full.
/// </summary>
internal sealed class PeqAddSlotControl : Control
{
    private static readonly Color OutlineColor = UiPalette.BorderMuted;
    private static readonly Color OutlineHoverColor = UiPalette.AccentMark;
    private static readonly Color GlyphColor = UiPalette.TextMuted;
    private static readonly Color GlyphHoverColor = UiPalette.TextDefault;

    // Bell first (most used), shelves high above low, then all-pass first order above second.
    private static readonly (PeqBandType Type, string Label)[] Zones =
    {
        (PeqBandType.Peaking, "PK"),
        (PeqBandType.HighShelf, "HS"),
        (PeqBandType.LowShelf, "LS"),
        (PeqBandType.AllPassFirstOrder, "AP1"),
        (PeqBandType.AllPassSecondOrder, "AP2")
    };

    private int hoveredZone = -1;

    public PeqAddSlotControl()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = UiPalette.PanelSurfaceDeep;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public event EventHandler<PeqAddBandEventArgs>? AddRequested;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHoveredZone(ZoneAt(e.Y));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHoveredZone(-1);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        int zone = ZoneAt(e.Y);
        if (zone >= 0)
        {
            AddRequested?.Invoke(this, new PeqAddBandEventArgs(Zones[zone].Type));
        }
    }

    private void SetHoveredZone(int zone)
    {
        if (hoveredZone == zone)
        {
            return;
        }

        hoveredZone = zone;
        Invalidate();
    }

    private int ZoneAt(int y)
    {
        if (Height <= 0)
        {
            return -1;
        }

        // Clamped: the last zone carries the rounding remainder.
        return Math.Clamp(y * Zones.Length / Height, 0, Zones.Length - 1);
    }

    private Rectangle ZoneBounds(int zone)
    {
        int top = Height * zone / Zones.Length;
        int bottom = Height * (zone + 1) / Zones.Length;
        return Rectangle.FromLTRB(0, top, Width, bottom);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        for (int zone = 0; zone < Zones.Length; zone++)
        {
            DrawZone(graphics, zone);
        }
    }

    private void DrawZone(Graphics graphics, int zone)
    {
        Rectangle bounds = ZoneBounds(zone);
        bounds.Inflate(-2, -2);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        bool hovered = hoveredZone == zone;

        using (var wash = new SolidBrush(PeqBandPalette.TileZone(Zones[zone].Type)))
        {
            graphics.FillRectangle(wash, bounds);
        }

        using var outline = new Pen(hovered ? OutlineHoverColor : OutlineColor)
        {
            DashStyle = DashStyle.Dash
        };
        graphics.DrawRectangle(outline, bounds);

        Color glyphColor = hovered ? GlyphHoverColor : GlyphColor;

        int arm = Math.Max(3, Math.Min(bounds.Width, bounds.Height) / 7);
        int plusY = bounds.Top + bounds.Height * 2 / 5;
        int centerX = bounds.Left + bounds.Width / 2;
        using (var glyph = new Pen(glyphColor, 2f))
        {
            graphics.DrawLine(glyph, centerX - arm, plusY, centerX + arm, plusY);
            graphics.DrawLine(glyph, centerX, plusY - arm, centerX, plusY + arm);
        }

        var labelArea = Rectangle.FromLTRB(
            bounds.Left,
            plusY + arm,
            bounds.Right,
            bounds.Bottom);
        if (labelArea.Height <= 0)
        {
            return;
        }

        TextRenderer.DrawText(
            graphics,
            Zones[zone].Label,
            Font,
            labelArea,
            glyphColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding);
    }
}
