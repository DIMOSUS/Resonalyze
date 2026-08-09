using System.Drawing.Drawing2D;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Which band shape an add zone stands for.</summary>
internal sealed class PeqAddBandEventArgs : EventArgs
{
    public PeqAddBandEventArgs(PeqBandType type)
    {
        Type = type;
    }

    public PeqBandType Type { get; }
}

/// <summary>
/// The "add a filter" tile that trails the PEQ strips: an empty slot outline split
/// into one zone per band shape, each with its own frame, plus and label. It always
/// sits in the cell after the last strip and disappears once the bank is full, so
/// the grid reads as a list the user extends rather than a fixed bank of 32 with
/// most of it greyed out.
/// </summary>
/// <remarks>
/// Three zones rather than one tile opening a menu: adding a filter is the most
/// repeated action in the panel, and a menu costs a second click and a jump away
/// from the strip. The zones keep the shapes visible — the tile says what the bank
/// can hold — and each one lands directly on the shape it names.
/// </remarks>
internal sealed class PeqAddSlotControl : Control
{
    private static readonly Color OutlineColor = Color.FromArgb(70, 78, 94);
    private static readonly Color OutlineHoverColor = UiPalette.AccentBlueSoft;
    private static readonly Color GlyphColor = Color.FromArgb(120, 130, 148);
    private static readonly Color GlyphHoverColor = UiPalette.TextPrimarySoft;

    // Top to bottom: the bell first as the one used most, then the shelves in the
    // order they sit on a frequency axis drawn upwards — high above low.
    private static readonly (PeqBandType Type, string Label)[] Zones =
    {
        (PeqBandType.Peaking, "PK"),
        (PeqBandType.HighShelf, "HS"),
        (PeqBandType.LowShelf, "LS")
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

        BackColor = Color.FromArgb(20, 22, 30);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    /// <summary>Raised with the shape of the zone that was clicked.</summary>
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

        // Bounded rather than trusted: the last zone carries the rounding remainder,
        // so a click on the final pixel row must not index past the array.
        return Math.Clamp(y * Zones.Length / Height, 0, Zones.Length - 1);
    }

    // The zones split the tile evenly, the last one taking whatever the division
    // left over, so together they cover it exactly with no seam at the bottom.
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

        // A wash of the colour the filter itself will wear, so the zone and the
        // strip it creates are recognisably the same thing.
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

        // The plus sits above the label, both sized off the zone so they stay
        // proportional at any DPI and at any panel width.
        int arm = Math.Max(3, Math.Min(bounds.Width, bounds.Height) / 7);
        int plusY = bounds.Top + bounds.Height * 2 / 5;
        int centerX = bounds.Left + bounds.Width / 2;
        using (var glyph = new Pen(glyphColor, 2f))
        {
            graphics.DrawLine(glyph, centerX - arm, plusY, centerX + arm, plusY);
            graphics.DrawLine(glyph, centerX, plusY - arm, centerX, plusY + arm);
        }

        // The label names the shape with the same token the strip header shows once
        // the filter exists, so the tile and the bank speak one vocabulary.
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
