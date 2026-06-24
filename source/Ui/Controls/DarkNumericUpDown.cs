using System.ComponentModel;
using System.Globalization;
using System.Drawing.Drawing2D;

namespace Resonalyze;

[DefaultEvent(nameof(ValueChanged))]
public sealed class DarkNumericUpDown : UserControl, ISupportInitialize
{
    private const int LogicalButtonColumnWidth = 18;
    private const int LogicalTextHorizontalPadding = 6;
    private const int LogicalTextToButtonsGap = 4;
    private const int LogicalVerticalPadding = 2;
    private const int LogicalArrowHalfWidth = 4;
    private const int LogicalArrowHalfHeight = 2;

    private readonly TextBox editor;
    private decimal minimum;
    private decimal maximum = 100;
    private decimal increment = 1;
    private decimal value;
    private int decimalPlaces;
    private bool thousandsSeparator;
    private bool suppressEditorSync;
    private bool upHovered;
    private bool downHovered;
    private bool upPressed;
    private bool downPressed;
    private BorderStyle borderStyle = BorderStyle.None;
    private bool readOnly;

    public DarkNumericUpDown()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        BackColor = UiPalette.ControlSurface;
        ForeColor = UiPalette.TextPrimary;
        Size = new Size(80, 19);
        MinimumSize = new Size(36, 19);
        TabStop = true;

        editor = new TextBox
        {
            AutoSize = false,
            BackColor = BackColor,
            BorderStyle = BorderStyle.None,
            ForeColor = ForeColor,
            Location = Point.Empty,
            Margin = Padding.Empty,
            TabStop = true,
            TextAlign = HorizontalAlignment.Right
        };
        editor.Enter += (_, _) => Invalidate();
        editor.Leave += (_, _) =>
        {
            CommitEditorText();
            Invalidate();
        };
        editor.KeyDown += EditorKeyDown;
        editor.MouseWheel += EditorMouseWheel;
        Controls.Add(editor);

        UpdateEditorText();
        LayoutEditor();
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public decimal Minimum
    {
        get => minimum;
        set
        {
            if (minimum == value)
            {
                return;
            }

            minimum = value;
            if (maximum < minimum)
            {
                maximum = minimum;
            }

            Value = Clamp(this.value);
            Invalidate();
        }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public decimal Maximum
    {
        get => maximum;
        set
        {
            if (maximum == value)
            {
                return;
            }

            maximum = value;
            if (minimum > maximum)
            {
                minimum = maximum;
            }

            Value = Clamp(this.value);
            Invalidate();
        }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public decimal Increment
    {
        get => increment;
        set
        {
            increment = value <= 0 ? 1 : value;
        }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int DecimalPlaces
    {
        get => decimalPlaces;
        set
        {
            int newValue = Math.Clamp(value, 0, 8);
            if (decimalPlaces == newValue)
            {
                return;
            }

            decimalPlaces = newValue;
            UpdateEditorText();
        }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool ThousandsSeparator
    {
        get => thousandsSeparator;
        set
        {
            if (thousandsSeparator == value)
            {
                return;
            }

            thousandsSeparator = value;
            UpdateEditorText();
        }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public HorizontalAlignment TextAlign
    {
        get => editor.TextAlign;
        set => editor.TextAlign = value;
    }

    [Browsable(true)]
    [DefaultValue(typeof(BorderStyle), "None")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public new BorderStyle BorderStyle
    {
        get => borderStyle;
        set
        {
            if (borderStyle == value)
            {
                return;
            }

            borderStyle = value;
            Invalidate();
        }
    }

    [Browsable(true)]
    [DefaultValue(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool ReadOnly
    {
        get => readOnly;
        set
        {
            if (readOnly == value)
            {
                return;
            }

            readOnly = value;
            editor.ReadOnly = value;
            Invalidate();
        }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public decimal Value
    {
        get => value;
        set
        {
            decimal newValue = Clamp(RoundToDecimalPlaces(value));
            if (this.value == newValue)
            {
                UpdateEditorText();
                return;
            }

            this.value = newValue;
            UpdateEditorText();
            OnValueChanged(EventArgs.Empty);
        }
    }

    [Browsable(true)]
    public event EventHandler? ValueChanged;

    public override Color BackColor
    {
        get => base.BackColor;
        set
        {
            base.BackColor = value;
            if (editor != null)
            {
                editor.BackColor = value;
            }

            Invalidate();
        }
    }

    public override Color ForeColor
    {
        get => base.ForeColor;
        set
        {
            base.ForeColor = value;
            if (editor != null)
            {
                editor.ForeColor = value;
            }

            Invalidate();
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        editor.Enabled = Enabled;
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (editor != null)
        {
            editor.Font = Font;
            LayoutEditor();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutEditor();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        LayoutEditor();
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Invalidate();
        if (!editor.Focused)
        {
            editor.Focus();
            editor.SelectAll();
        }
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Enabled)
        {
            return;
        }

        if (e.Delta > 0)
        {
            StepUp();
        }
        else if (e.Delta < 0)
        {
            StepDown();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool newUpHovered = GetUpButtonBounds().Contains(e.Location);
        bool newDownHovered = GetDownButtonBounds().Contains(e.Location);
        if (upHovered != newUpHovered || downHovered != newDownHovered)
        {
            upHovered = newUpHovered;
            downHovered = newDownHovered;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        upHovered = false;
        downHovered = false;
        upPressed = false;
        downPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left)
        {
            return;
        }

        if (GetUpButtonBounds().Contains(e.Location))
        {
            upPressed = true;
            StepUp();
            Invalidate();
            return;
        }

        if (GetDownButtonBounds().Contains(e.Location))
        {
            downPressed = true;
            StepDown();
            Invalidate();
            return;
        }

        editor.Focus();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (upPressed || downPressed)
        {
            upPressed = false;
            downPressed = false;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        e.Graphics.Clear(Parent?.BackColor ?? UiPalette.AppBackground);

        using var backgroundBrush = new SolidBrush(Enabled
            ? BackColor
            : UiPalette.ButtonDisabledBackground);
        using var borderPen = new Pen(ContainsFocus
            ? UiPalette.AccentBlueSoft
            : UiPalette.DialogBorderSoft);
        e.Graphics.FillRectangle(backgroundBrush, bounds);
        if (borderStyle != BorderStyle.None || ContainsFocus)
        {
            e.Graphics.DrawRectangle(
                borderPen,
                bounds.X,
                bounds.Y,
                bounds.Width - 1,
                bounds.Height - 1);
        }

        Rectangle buttonColumn = GetButtonColumnBounds();
        using var buttonBrush = new SolidBrush(UiPalette.ButtonBackground);
        e.Graphics.FillRectangle(buttonBrush, buttonColumn);

        Rectangle upBounds = GetUpButtonBounds();
        Rectangle downBounds = GetDownButtonBounds();
        DrawButtonState(e.Graphics, upBounds, upHovered, upPressed);
        DrawButtonState(e.Graphics, downBounds, downHovered, downPressed);

        using var separatorPen = new Pen(UiPalette.DialogBorder);
        e.Graphics.DrawLine(
            separatorPen,
            buttonColumn.Left,
            1,
            buttonColumn.Left,
            Height - 2);
        e.Graphics.DrawLine(
            separatorPen,
            buttonColumn.Left,
            upBounds.Bottom,
            buttonColumn.Right - 1,
            upBounds.Bottom);

        DrawArrow(e.Graphics, upBounds, up: true);
        DrawArrow(e.Graphics, downBounds, up: false);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Enabled)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        if (keyData == Keys.Up)
        {
            StepUp();
            return true;
        }

        if (keyData == Keys.Down)
        {
            StepDown();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        LayoutEditor();
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            CommitEditorText();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Up)
        {
            StepUp();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Down)
        {
            StepDown();
            e.SuppressKeyPress = true;
        }
    }

    private void EditorMouseWheel(object? sender, MouseEventArgs e)
    {
        OnMouseWheel(e);
    }

    private void CommitEditorText()
    {
        if (suppressEditorSync)
        {
            return;
        }

        if (TryParseEditorText(out decimal parsed))
        {
            Value = parsed;
        }
        else
        {
            UpdateEditorText();
        }
    }

    private bool TryParseEditorText(out decimal parsed)
    {
        string text = editor.Text.Trim();
        if (decimal.TryParse(
            text,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out parsed))
        {
            return true;
        }

        return decimal.TryParse(
            text,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out parsed);
    }

    private void UpdateEditorText()
    {
        if (editor == null)
        {
            return;
        }

        suppressEditorSync = true;
        try
        {
            editor.Text = FormatValue(value);
        }
        finally
        {
            suppressEditorSync = false;
        }
    }

    private string FormatValue(decimal currentValue)
    {
        string format = (thousandsSeparator ? "N" : "F") + decimalPlaces.ToString(CultureInfo.InvariantCulture);
        return currentValue.ToString(format, CultureInfo.CurrentCulture);
    }

    private void StepUp()
    {
        Value += increment;
    }

    private void StepDown()
    {
        Value -= increment;
    }

    private void LayoutEditor()
    {
        if (editor == null)
        {
            return;
        }

        int horizontalPadding = ScaleLogical(LogicalTextHorizontalPadding);
        int textToButtonsGap = ScaleLogical(LogicalTextToButtonsGap);
        int verticalPadding = ScaleLogical(LogicalVerticalPadding);
        int buttonColumnWidth = GetButtonColumnWidth();
        int textAreaWidth = Math.Max(
            8,
            Width - buttonColumnWidth - horizontalPadding - textToButtonsGap - 2);
        int textHeight = Math.Max(10, Height - verticalPadding * 2 - 2);
        int textY = Math.Max(1, verticalPadding);
        editor.Font = Font;
        editor.Location = new Point(horizontalPadding, textY);
        editor.Size = new Size(textAreaWidth, textHeight);
        Invalidate();
    }

    private Rectangle GetButtonColumnBounds()
    {
        int buttonColumnWidth = GetButtonColumnWidth();
        return new Rectangle(
            Math.Max(1, Width - buttonColumnWidth - 1),
            1,
            buttonColumnWidth,
            Math.Max(0, Height - 2));
    }

    private Rectangle GetUpButtonBounds()
    {
        Rectangle column = GetButtonColumnBounds();
        int halfHeight = column.Height / 2;
        return new Rectangle(column.X, column.Y, column.Width, halfHeight);
    }

    private Rectangle GetDownButtonBounds()
    {
        Rectangle column = GetButtonColumnBounds();
        int halfHeight = column.Height / 2;
        return new Rectangle(column.X, column.Y + halfHeight, column.Width, column.Height - halfHeight);
    }

    private void DrawButtonState(Graphics graphics, Rectangle bounds, bool hovered, bool pressed)
    {
        Color fill = UiPalette.ButtonBackground;
        if (!Enabled)
        {
            fill = UiPalette.ButtonDisabledBackground;
        }
        else if (pressed)
        {
            fill = UiPalette.ButtonPressedBackground;
        }
        else if (hovered)
        {
            fill = UiPalette.ButtonHoverBackground;
        }

        using var brush = new SolidBrush(fill);
        graphics.FillRectangle(brush, bounds);
    }

    private void DrawArrow(Graphics graphics, Rectangle bounds, bool up)
    {
        Color color = Enabled ? UiPalette.TextPrimarySoft : UiPalette.TextMuted;
        float centerX = bounds.Left + bounds.Width / 2f;
        float centerY = bounds.Top + bounds.Height / 2f;
        float halfWidth = Math.Min(
            ScaleLogical(LogicalArrowHalfWidth),
            Math.Max(2f, (bounds.Width - 6f) / 2f));
        float halfHeight = Math.Min(
            ScaleLogical(LogicalArrowHalfHeight),
            Math.Max(1.5f, (bounds.Height - 6f) / 2f));
        PointF[] points = up
            ? [
                new PointF(centerX - halfWidth, centerY + halfHeight),
                new PointF(centerX + halfWidth, centerY + halfHeight),
                new PointF(centerX, centerY - halfHeight)
            ]
            : [
                new PointF(centerX - halfWidth, centerY - halfHeight),
                new PointF(centerX + halfWidth, centerY - halfHeight),
                new PointF(centerX, centerY + halfHeight)
            ];

        SmoothingMode previousSmoothingMode = graphics.SmoothingMode;
        PixelOffsetMode previousPixelOffsetMode = graphics.PixelOffsetMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var brush = new SolidBrush(color);
        graphics.FillPolygon(brush, points);
        graphics.SmoothingMode = previousSmoothingMode;
        graphics.PixelOffsetMode = previousPixelOffsetMode;
    }

    private decimal Clamp(decimal candidate)
    {
        return Math.Min(maximum, Math.Max(minimum, candidate));
    }

    private decimal RoundToDecimalPlaces(decimal candidate)
    {
        return decimal.Round(candidate, decimalPlaces, MidpointRounding.AwayFromZero);
    }

    private void OnValueChanged(EventArgs e)
    {
        ValueChanged?.Invoke(this, e);
    }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
        LayoutEditor();
        Invalidate();
    }

    private int GetButtonColumnWidth()
    {
        return Math.Max(16, ScaleLogical(LogicalButtonColumnWidth));
    }

    private int ScaleLogical(int logicalPixels)
    {
        float scale = DeviceDpi > 0
            ? DeviceDpi / 96.0f
            : 1.0f;
        return Math.Max(1, (int)Math.Round(logicalPixels * scale));
    }
}
