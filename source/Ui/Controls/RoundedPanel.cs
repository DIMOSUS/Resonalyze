using System.ComponentModel;

namespace Resonalyze;

/// <summary>Rounded card panel (corners painted, see <see cref="RoundedSurface"/>). Children are not clipped, so a panel fully covered by a
/// docked child has nothing to round. AutoScroll is unsupported: scrolling smears the outline.</summary>
internal sealed class RoundedPanel : Panel
{
    private int cornerRadius = RoundedSurface.DefaultCornerRadius;
    private Color borderColor = UiPalette.Border;

    public RoundedPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    [Category("Appearance")]
    [DefaultValue(RoundedSurface.DefaultCornerRadius)]
    [Description("Corner radius in 96-DPI pixels, scaled to the display when painted.")]
    public int CornerRadius
    {
        get => cornerRadius;
        set
        {
            int radius = Math.Max(0, value);
            if (radius == cornerRadius)
            {
                return;
            }

            cornerRadius = radius;
            Invalidate();
        }
    }

    [Category("Appearance")]
    [Description("Colour of the one-pixel outline; transparent draws none.")]
    public Color BorderColor
    {
        get => borderColor;
        set
        {
            if (value == borderColor)
            {
                return;
            }

            borderColor = value;
            Invalidate();
        }
    }

    // Palette default cannot be a [DefaultValue]; keeps the theme border out of .Designer.cs.
    private bool ShouldSerializeBorderColor() => borderColor != UiPalette.Border;

    private void ResetBorderColor() => BorderColor = UiPalette.Border;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new BorderStyle BorderStyle
    {
        get => base.BorderStyle;

        set => base.BorderStyle = BorderStyle.None;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        RoundedSurface.Paint(this, e.Graphics, cornerRadius, borderColor);
        base.OnPaint(e);
    }

    protected override void OnParentBackColorChanged(EventArgs e)
    {
        base.OnParentBackColorChanged(e);
        Invalidate();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }
}
