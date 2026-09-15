using System.ComponentModel;
using System.Globalization;
using System.Drawing.Drawing2D;

namespace Resonalyze;

[DefaultEvent(nameof(ValueChanged))]
public sealed class DarkNumericUpDown : UserControl, ISupportInitialize
{
    private const int LogicalButtonColumnWidth = 18;
    private const int LogicalTextHorizontalPadding = 6;
    private const int LogicalTextToButtonsGap = 1;
    private const int LogicalVerticalPadding = 2;
    private const int LogicalArrowHalfWidth = 4;
    private const int LogicalArrowHalfHeight = 2;

    private const int LogarithmicStepsPerOctave = 96;

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
    private bool resetHovered;
    private bool resetPressed;
    private decimal? defaultValue;
    private BorderStyle borderStyle = BorderStyle.None;
    private bool readOnly;
    private bool initializing;
    private string inlineLabel = string.Empty;
    private string valueSuffix = string.Empty;
    private decimal logarithmicAnchor;
    private decimal logarithmicRung;
    private int logarithmicPosition;

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
            if (initializing)
            {
                // Designer sets properties in arbitrary order; reconcile in EndInit so Value is not clamped against an unassigned bound.
                return;
            }

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
            if (initializing)
            {
                return;
            }

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

    /// <summary>One step = 1/96 octave instead of <see cref="Increment"/>, for Hz fields; rounded to the display, never below one unit.
    /// Steps walk an anchored ladder, so a step and a step back return to the same value.</summary>
    [Browsable(true)]
    [DefaultValue(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool LogarithmicFrequencyStep { get; set; }

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

    /// <summary>Muted caption at the inner-left; reserves no space, the value draws over it.</summary>
    [Browsable(true)]
    [DefaultValue("")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string InlineLabel
    {
        get => inlineLabel;
        set
        {
            string newValue = value ?? string.Empty;
            if (inlineLabel == newValue)
            {
                return;
            }

            inlineLabel = newValue;
            UpdateEditorVisibility();
            Invalidate();
        }
    }

    /// <summary>Muted unit right of the value; reserves its own space.</summary>
    [Browsable(true)]
    [DefaultValue("")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string ValueSuffix
    {
        get => valueSuffix;
        set
        {
            string newValue = value ?? string.Empty;
            if (valueSuffix == newValue)
            {
                return;
            }

            valueSuffix = newValue;
            LayoutEditor();
            Invalidate();
        }
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

    /// <summary>When set, an "R" reset button restores this value.</summary>
    [Browsable(true)]
    [DefaultValue(null)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public decimal? DefaultValue
    {
        get => defaultValue;
        set
        {
            if (defaultValue == value)
            {
                return;
            }

            defaultValue = value;
            LayoutEditor();
            Invalidate();
        }
    }

    private bool ShowResetButton => defaultValue.HasValue;

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public decimal Value
    {
        get => value;
        set
        {
            if (initializing)
            {
                this.value = value;
                return;
            }

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

    public void CommitText()
    {
        CommitEditorText();
    }

    public override Color BackColor
    {
        get => base.BackColor;
        set
        {
            base.BackColor = value;
            if (editor != null && Enabled)
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
            if (editor != null && Enabled)
            {
                editor.ForeColor = value;
            }

            Invalidate();
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        // A disabled control self-paints its value (Windows colours a native EDIT under a disabled parent).
        editor.ReadOnly = readOnly || !Enabled;
        editor.ForeColor = Enabled ? ForeColor : UiPalette.TextDisabled;
        editor.BackColor = Enabled ? BackColor : UiPalette.ButtonDisabledBackground;
        UpdateEditorVisibility();
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
        editor.Visible = true;
        if (!editor.Focused)
        {
            editor.Focus();
            editor.SelectAll();
        }
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        UpdateEditorVisibility();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // Only a focused field takes the wheel; unfocused it bubbles so scrolling the channel list does not edit fields.
        if (!Enabled || !ContainsFocus)
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

        // Consumed, or WinForms bubbles it to the AutoScroll parent. The inner editor forwards its wheel here too.
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool newUpHovered = GetUpButtonBounds().Contains(e.Location);
        bool newDownHovered = GetDownButtonBounds().Contains(e.Location);
        bool newResetHovered = ShowResetButton && GetResetButtonBounds().Contains(e.Location);
        if (upHovered != newUpHovered ||
            downHovered != newDownHovered ||
            resetHovered != newResetHovered)
        {
            upHovered = newUpHovered;
            downHovered = newDownHovered;
            resetHovered = newResetHovered;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        upHovered = false;
        downHovered = false;
        resetHovered = false;
        upPressed = false;
        downPressed = false;
        resetPressed = false;
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

        if (ShowResetButton && GetResetButtonBounds().Contains(e.Location))
        {
            resetPressed = true;
            ResetToDefault();
            Invalidate();
            return;
        }

        editor.Visible = true;
        editor.Focus();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (upPressed || downPressed || resetPressed)
        {
            upPressed = false;
            downPressed = false;
            resetPressed = false;
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

        if (ShowResetButton)
        {
            Rectangle resetBounds = GetResetButtonBounds();
            DrawButtonState(e.Graphics, resetBounds, resetHovered, resetPressed);
            e.Graphics.DrawLine(
                separatorPen,
                resetBounds.Left,
                1,
                resetBounds.Left,
                Height - 2);

            Color glyphColor = Enabled ? UiPalette.TextPrimarySoft : UiPalette.TextDisabled;
            TextRenderer.DrawText(
                e.Graphics,
                "R",
                Font,
                resetBounds,
                glyphColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }

        if (!editor.Visible)
        {
            Rectangle textBounds = editor.Bounds;
            if (HasInlineLabel)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    inlineLabel,
                    Font,
                    textBounds,
                    UiPalette.TextDisabled,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);
            }

            TextRenderer.DrawText(
                e.Graphics,
                FormatValue(value),
                Font,
                textBounds,
                Enabled ? ForeColor : UiPalette.TextDisabled,
                TextFormatFlags.Right |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }

        if (HasSuffix)
        {
            var suffixBounds = new Rectangle(
                editor.Right, editor.Top, MeasureSuffixWidth() + 1, editor.Height);
            TextRenderer.DrawText(
                e.Graphics,
                SuffixDisplay,
                Font,
                suffixBounds,
                Enabled ? UiPalette.TextSecondary : UiPalette.TextDisabled,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }
    }

    private bool HasInlineLabel => inlineLabel.Length > 0;

    private bool HasSuffix => valueSuffix.Length > 0;

    private string SuffixDisplay => " " + valueSuffix;

    private int MeasureSuffixWidth() => HasSuffix
        ? TextRenderer.MeasureText(SuffixDisplay, Font, Size.Empty, TextFormatFlags.NoPadding).Width
        : 0;

    // The opaque editor would hide the inline caption, so in inline-label mode it shows only while focused.
    private void UpdateEditorVisibility()
    {
        // Disabled hides the editor: a native edit under a disabled parent is painted system grey (2.5:1), unreadable.
        bool shouldShow = Enabled && (!HasInlineLabel || ContainsFocus);
        if (editor.Visible != shouldShow)
        {
            editor.Visible = shouldShow;
            Invalidate();
        }
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

        if (keyData == Keys.Enter)
        {
            // AcceptButton consumes Enter before the editor's KeyDown; commit so the accept handler reads the typed text.
            bool hadPendingEdit = HasPendingEditorText;
            CommitEditorText();

            // The Enter that commits stops here, or it would also fire the default button; a second Enter reaches it.
            if (hadPendingEdit)
            {
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        LayoutEditor();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // DeviceDpi is final only once the handle exists; runtime-added controls would keep a 96-DPI editor layout.
        LayoutEditor();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
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

    // Compared against FormatValue (what UpdateEditorText writes), so an untouched field reads false.
    private bool HasPendingEditorText =>
        !suppressEditorSync &&
        editor != null &&
        !string.Equals(editor.Text, FormatValue(value), StringComparison.Ordinal);

    private bool TryParseEditorText(out decimal parsed) =>
        NumericTextParser.TryParse(editor.Text, CultureInfo.CurrentCulture, out parsed);

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

        if (HasInlineLabel && !editor.Visible)
        {
            Invalidate();
        }
    }

    private string FormatValue(decimal currentValue)
    {
        string format = (thousandsSeparator ? "N" : "F") + decimalPlaces.ToString(CultureInfo.InvariantCulture);
        return currentValue.ToString(format, CultureInfo.CurrentCulture);
    }

    // Shares the read-only guard so ReadOnly also blocks reset.
    private void ResetToDefault()
    {
        if (readOnly || !defaultValue.HasValue)
        {
            return;
        }

        Value = defaultValue.Value;
    }

    // Rungs at anchor x 2^(n/96): anchoring keeps both directions exact opposites (a measured step went 347 -> 345 -> 348).
    // Rebuilt when the value came from anything but a step; a clamped step keeps its anchor and position.
    private decimal NextRung(int direction, out int position)
    {
        if (value != logarithmicRung || logarithmicAnchor <= 0)
        {
            logarithmicAnchor = value;
            logarithmicPosition = 0;
        }

        position = logarithmicPosition;
        if (value <= 0)
        {
            return value + (direction * SmallestDisplayableStep);
        }

        // Rungs rounding onto the current value are skipped (below ~69 Hz a 96th octave is under 0.5 Hz).
        decimal rung;
        do
        {
            position += direction;
            rung = RungValue(position);
        }
        while (direction > 0 ? rung <= value : rung >= value);

        return rung;
    }

    private decimal RungValue(int position) => RoundToDecimalPlaces((decimal)(
        (double)logarithmicAnchor *
        Math.Pow(2, position / (double)LogarithmicStepsPerOctave)));

    private decimal SmallestDisplayableStep => new decimal(1, 0, 0, false, (byte)decimalPlaces);

    // Commit first so the step applies to typed text. The single read-only choke point for all step paths.
    private void Step(int direction)
    {
        if (readOnly)
        {
            return;
        }

        CommitEditorText();
        if (!LogarithmicFrequencyStep)
        {
            Value = value + (direction * increment);
            return;
        }

        decimal previous = value;
        Value = NextRung(direction, out int position);

        // Record the climb only when the value moved, so a wheel held at a limit does not wind past the rung that reached it.
        if (value != previous)
        {
            logarithmicPosition = position;
        }

        logarithmicRung = value;
    }
    private void StepUp() => Step(1);

    private void StepDown() => Step(-1);

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
        int resetColumnWidth = GetResetColumnWidth();
        int textAreaWidth = Math.Max(
            8,
            Width - buttonColumnWidth - resetColumnWidth - horizontalPadding
                - textToButtonsGap - 2 - MeasureSuffixWidth());
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
        int resetColumnWidth = GetResetColumnWidth();
        return new Rectangle(
            Math.Max(1, Width - buttonColumnWidth - resetColumnWidth - 1),
            1,
            buttonColumnWidth,
            Math.Max(0, Height - 2));
    }

    private int GetResetColumnWidth() =>
        ShowResetButton ? GetButtonColumnWidth() : 0;

    private Rectangle GetResetButtonBounds()
    {
        if (!ShowResetButton)
        {
            return Rectangle.Empty;
        }

        int resetColumnWidth = GetResetColumnWidth();
        return new Rectangle(
            Math.Max(1, Width - resetColumnWidth - 1),
            1,
            resetColumnWidth,
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
        Color color = Enabled ? UiPalette.TextPrimarySoft : UiPalette.TextDisabled;
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

    public void ApplyToolTip(WrappingToolTip toolTip, string text)
    {
        ArgumentNullException.ThrowIfNull(toolTip);
        toolTip.SetToolTip(this, text);
        toolTip.SetToolTip(editor, text);
    }

    public void BeginInit()
    {
        initializing = true;
    }

    public void EndInit()
    {
        initializing = false;
        if (maximum < minimum)
        {
            maximum = minimum;
        }

        value = Clamp(RoundToDecimalPlaces(value));
        UpdateEditorText();
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
