using System.ComponentModel;

namespace Resonalyze;

/// <summary>
/// The app's card: a <see cref="Panel"/> that paints itself with rounded corners
/// and its own one-pixel outline, in place of the framework's square
/// <see cref="BorderStyle.FixedSingle"/> one.
/// </summary>
/// <remarks>
/// <para>
/// The corners are painted, not cut — see <see cref="RoundedSurface"/> for why,
/// and for the one thing that assumes: the parent behind the panel has to be a
/// flat colour. <see cref="Control.BackColor"/> is the surface as it always was,
/// so a panel converted from a stock one keeps the colour its designer gave it.
/// </para>
/// <para>
/// Children are NOT clipped to the rounded shape (no region is set), so a child
/// laid flush into a corner paints its own background over the arc. At the radii
/// used here that is a pixel or two, and every panel in the app leaves its
/// content a margin; a child that genuinely has to sit in the corner wants a
/// smaller <see cref="CornerRadius"/> rather than a region.
/// </para>
/// <para>
/// The same rule decides where this panel does NOT belong: a container whose
/// child is docked over the whole of it has no surface left to round, and the
/// outline would be painted under that child instead of around it. The EQ
/// wizard's PEQ well is filled edge to edge by its slot table and keeps the
/// framework's non-client border for exactly that reason.
/// </para>
/// <para>
/// <see cref="ScrollableControl.AutoScroll"/> is not supported: scrolling shifts
/// the painted pixels and only invalidates what the shift uncovered, which
/// smears the outline. Nothing in the app scrolls a card.
/// </para>
/// </remarks>
internal sealed class RoundedPanel : Panel
{
    private int cornerRadius = RoundedSurface.DefaultCornerRadius;
    private Color borderColor = UiPalette.DialogBorder;

    public RoundedPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    /// <summary>
    /// Corner radius in 96-DPI pixels, scaled to the display when painted. Zero
    /// gives square corners and leaves the outline, which is the stock panel's
    /// look drawn in the palette's colour.
    /// </summary>
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

    /// <summary>
    /// Colour of the one-pixel outline. <see cref="Color.Transparent"/> — or any
    /// fully transparent colour — draws none, for a panel that is only a tinted
    /// surface.
    /// </summary>
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

    // The default is a palette colour rather than a constant, so the designer
    // cannot be told it with [DefaultValue]; this is the pair it asks for
    // instead, and it keeps the theme's own border out of the .Designer.cs.
    private bool ShouldSerializeBorderColor() => borderColor != UiPalette.DialogBorder;

    private void ResetBorderColor() => BorderColor = UiPalette.DialogBorder;

    /// <summary>
    /// Hidden: the panel draws its own outline, and the framework's square one
    /// would be drawn around the rounded shape rather than instead of it.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new BorderStyle BorderStyle
    {
        get => base.BorderStyle;

        // Whatever is asked for, the answer is none — including a designer session
        // that re-opens the form and offers the property in its grid.
        set => base.BorderStyle = BorderStyle.None;
    }

    // The surface is painted whole in OnPaint, corners included, so the stock
    // background fill would only be overdrawn a moment later.
    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        RoundedSurface.Paint(this, e.Graphics, cornerRadius, borderColor);
        base.OnPaint(e);
    }

    // The corners are the parent's colour, so they are only right for as long as
    // the parent's colour is what it was — including the move to a new parent.
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

    // The radius is in logical pixels, so it means a different number of device
    // pixels on the new display.
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }
}
