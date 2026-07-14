using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Resonalyze;

/// <summary>
/// A dark, mixing-console style vertical gain fader used by the EQ Wizard PEQ
/// strips. It is a view/controller over a gain value in dB: the strip keeps a
/// <see cref="DarkNumericUpDown"/> as the source of truth and mirrors this fader
/// to it, so dragging the cap and typing the number stay in lock-step. The
/// control paints its own dB scale beside the track (the upper/lower limits plus
/// the 0 dB unity mark), so a band's boost or cut reads at a glance.
/// </summary>
internal sealed class GainFader : Control
{
    // The track is offset to the right of centre to leave a gutter on the left
    // for the dB scale labels; everything else is derived from the client size.
    private const float TrackCenterFraction = 0.62f;

    private double minimum = -15;
    private double maximum = 6;
    private double value;
    private double increment = 0.1;
    private double pageIncrement = 1.0;
    private bool hovered;
    private bool dragging;
    private bool wasActiveOnPress;

    public GainFader()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);

        // The strips drive the tab order through the numeric fields; the fader is
        // reached by clicking (which also focuses it, enabling the arrow keys).
        TabStop = false;
        BackColor = Color.FromArgb(44, 50, 60);
        ForeColor = UiPalette.TextSecondary;
        Font = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
    }

    /// <summary>Raised whenever the value changes, whether by drag, wheel or keyboard.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>
    /// Set by the owning strip: true when this fader's band is the selected one.
    /// A click only jumps the value when the strip was already active; the click
    /// that first selects the band does not move the fader.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool StripActive { get; set; }

    // The strip configures these from code (mirroring the gain field), never from
    // the designer, so keep them out of the property grid and designer serializer.
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Minimum
    {
        get => minimum;
        set
        {
            minimum = value;
            if (maximum < minimum)
            {
                maximum = minimum;
            }

            Value = Clamp(this.value);
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Maximum
    {
        get => maximum;
        set
        {
            maximum = value;
            if (minimum > maximum)
            {
                minimum = maximum;
            }

            Value = Clamp(this.value);
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Increment
    {
        get => increment;
        set => increment = value <= 0 ? 0.1 : value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double PageIncrement
    {
        get => pageIncrement;
        set => pageIncrement = value <= 0 ? 1.0 : value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => value;
        set
        {
            double clamped = Clamp(Quantize(value));
            if (this.value == clamped)
            {
                return;
            }

            this.value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override Size DefaultSize => new(48, 110);

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Parent?.BackColor ?? BackColor);
        if (Width <= 4 || Height <= 4)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        TrackMetrics track = GetTrackMetrics();
        float trackLeft = track.CenterX - track.Width / 2f;
        var trackRect = new RectangleF(trackLeft, track.Top, track.Width, track.Bottom - track.Top);

        using (GraphicsPath groove = RoundedRectangle(trackRect, track.Width / 2f))
        {
            using var grooveBrush = new SolidBrush(UiPalette.PlotTrack);
            graphics.FillPath(grooveBrush, groove);
            using var groovePen = new Pen(UiPalette.DialogBorderSoft);
            graphics.DrawPath(groovePen, groove);
        }

        float zeroY = ValueToY(0);
        float thumbY = ValueToY(value);
        bool enabled = Enabled;

        // Fill the groove from the unity mark to the current value: green above
        // (boost), red below (cut), so the departure from 0 dB is obvious. A
        // disabled band fills in a flat, faint grey instead.
        float fillTop = Math.Min(zeroY, thumbY);
        float fillBottom = Math.Max(zeroY, thumbY);
        if (fillBottom - fillTop > 0.5f)
        {
            Color fillColor = enabled
                ? value >= 0 ? UiPalette.SuccessGreen : UiPalette.WarningRed
                : UiPalette.MeterDimFill;
            var fillRect = new RectangleF(
                trackLeft + 1f, fillTop, track.Width - 2f, fillBottom - fillTop);
            using var fillBrush = new SolidBrush(Color.FromArgb(enabled ? 150 : 70, fillColor));
            graphics.FillRectangle(fillBrush, fillRect);
        }

        DrawScale(graphics, track, trackLeft, zeroY, enabled);
        DrawThumb(graphics, track, thumbY, enabled);
    }

    private void DrawScale(
        Graphics graphics, TrackMetrics track, float trackLeft, float zeroY, bool enabled)
    {
        float tickRight = trackLeft - ScaleF(2);
        float tickLeft = tickRight - ScaleF(4);
        using (var tickPen = new Pen(Color.FromArgb(110, 150, 160, 175), 1f))
        {
            double firstTick = Math.Ceiling(minimum / 3.0) * 3.0;
            for (double db = firstTick; db <= maximum + 1e-6; db += 3.0)
            {
                float y = ValueToY(db);
                graphics.DrawLine(tickPen, tickLeft, y, tickRight, y);
            }
        }

        float labelRight = tickLeft - ScaleF(2);
        Color maxColor = enabled ? UiPalette.SuccessGreenSoft : UiPalette.TextMuted;
        Color zeroColor = enabled ? UiPalette.TextSecondary : UiPalette.TextMuted;
        Color minColor = enabled ? UiPalette.ErrorSoft : UiPalette.TextMuted;
        DrawScaleLabel(graphics, FormatDb(maximum), track.Top, labelRight, maxColor);
        DrawScaleLabel(graphics, "0", zeroY, labelRight, zeroColor);
        DrawScaleLabel(graphics, FormatDb(minimum), track.Bottom, labelRight, minColor);
    }

    private void DrawScaleLabel(Graphics graphics, string text, float centerY, float rightX, Color color)
    {
        Size size = TextRenderer.MeasureText(
            graphics, text, Font, Size.Empty, TextFormatFlags.NoPadding);
        var origin = new Point(
            (int)Math.Round(rightX - size.Width),
            (int)Math.Round(centerY - size.Height / 2f));
        TextRenderer.DrawText(graphics, text, Font, origin, color, TextFormatFlags.NoPadding);
    }

    private void DrawThumb(Graphics graphics, TrackMetrics track, float thumbY, bool enabled)
    {
        float capWidth = Math.Min(Width - ScaleF(6), ScaleF(15));
        float capHeight = ScaleF(11);
        var cap = new RectangleF(
            track.CenterX - capWidth / 2f,
            thumbY - capHeight / 2f,
            capWidth,
            capHeight);

        bool active = enabled && (dragging || hovered);
        Color faceTop;
        Color faceBottom;
        if (!enabled)
        {
            faceTop = Color.FromArgb(48, 52, 62);
            faceBottom = Color.FromArgb(34, 37, 45);
        }
        else if (active)
        {
            faceTop = Color.FromArgb(72, 80, 112);
            faceBottom = Color.FromArgb(44, 50, 74);
        }
        else
        {
            faceTop = Color.FromArgb(58, 64, 84);
            faceBottom = Color.FromArgb(36, 40, 54);
        }

        using (GraphicsPath capPath = RoundedRectangle(cap, ScaleF(3)))
        {
            using (var faceBrush = new LinearGradientBrush(
                cap, faceTop, faceBottom, LinearGradientMode.Vertical))
            {
                graphics.FillPath(faceBrush, capPath);
            }

            Color borderColor = !enabled
                ? UiPalette.DialogBorderSoft
                : active || Focused ? UiPalette.AccentBlueSoft : UiPalette.DialogBorder;
            using var capPen = new Pen(borderColor);
            graphics.DrawPath(capPen, capPath);
        }

        // The bright grip line down the middle of the cap marks the exact value.
        Color gripColor = enabled
            ? Color.FromArgb(220, UiPalette.TextPrimarySoft)
            : Color.FromArgb(120, UiPalette.TextMuted);
        using var gripPen = new Pen(gripColor, Math.Max(1f, ScaleF(1.4f)));
        graphics.DrawLine(gripPen, cap.Left + ScaleF(2), thumbY, cap.Right - ScaleF(2), thumbY);
    }

    protected override void WndProc(ref Message m)
    {
        // Capture whether this fader's band was already the selected one the
        // instant the button goes down — before the click's own focus/selection
        // side effects flip it — so OnMouseDown can tell an activating click from
        // a value-setting one regardless of when WinForms moves focus.
        const int WM_LBUTTONDOWN = 0x0201;
        if (m.Msg == WM_LBUTTONDOWN)
        {
            wasActiveOnPress = StripActive;
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left)
        {
            return;
        }

        if (!Focused)
        {
            Focus();
        }

        // A click that first selects this fader's band only activates the strip;
        // it must not jump the value. Once the band is the selected one — whether
        // it was selected via this fader or one of its numeric fields — clicks
        // drag it. This stops switching between slots from overwriting their gain.
        if (!wasActiveOnPress)
        {
            return;
        }

        dragging = true;
        Capture = true;
        Value = YToValue(e.Y);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging)
        {
            Value = YToValue(e.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (dragging)
        {
            dragging = false;
            Capture = false;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Enabled)
        {
            return;
        }

        Value = value + (e.Delta > 0 ? increment : -increment);

        // Consume the wheel so it steps the fader instead of bubbling to an
        // AutoScroll parent that would scroll the whole strip bank.
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown => true,
        _ => base.IsInputKey(keyData)
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Up:
                Value = value + increment;
                e.Handled = true;
                break;
            case Keys.Down:
                Value = value - increment;
                e.Handled = true;
                break;
            case Keys.PageUp:
                Value = value + pageIncrement;
                e.Handled = true;
                break;
            case Keys.PageDown:
                Value = value - pageIncrement;
                e.Handled = true;
                break;
        }
    }

    private TrackMetrics GetTrackMetrics()
    {
        float pad = ScaleF(12);
        return new TrackMetrics(
            Top: pad,
            Bottom: Height - pad,
            CenterX: Width * TrackCenterFraction,
            Width: ScaleF(8));
    }

    private float ValueToY(double candidate)
    {
        TrackMetrics track = GetTrackMetrics();
        double fraction = maximum > minimum
            ? (candidate - minimum) / (maximum - minimum)
            : 0.0;
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        return (float)(track.Bottom - fraction * (track.Bottom - track.Top));
    }

    private double YToValue(float y)
    {
        TrackMetrics track = GetTrackMetrics();
        double span = track.Bottom - track.Top;
        double fraction = span > 0 ? (track.Bottom - y) / span : 0.0;
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        return minimum + fraction * (maximum - minimum);
    }

    private double Quantize(double candidate)
    {
        if (increment <= 0)
        {
            return candidate;
        }

        double steps = Math.Round((candidate - minimum) / increment, MidpointRounding.AwayFromZero);
        return Math.Round(minimum + steps * increment, 4);
    }

    private double Clamp(double candidate) => Math.Min(maximum, Math.Max(minimum, candidate));

    private static string FormatDb(double db)
    {
        long rounded = (long)Math.Round(db);
        return rounded > 0 ? $"+{rounded}" : rounded.ToString();
    }

    private float ScaleF(float logical)
    {
        float scale = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
        return logical * scale;
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2f;
        if (diameter <= 0 || rect.Width < diameter || rect.Height < diameter)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private readonly record struct TrackMetrics(float Top, float Bottom, float CenterX, float Width);
}
