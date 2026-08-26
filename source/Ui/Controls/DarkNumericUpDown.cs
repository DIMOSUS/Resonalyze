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

    // One step of LogarithmicFrequencyStep is this fraction of an octave, which is
    // what makes it the same distance everywhere on a logarithmic frequency axis.
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
                // Between BeginInit and EndInit the designer sets properties in
                // arbitrary order; defer the range/value reconciliation to EndInit
                // so Value is never clamped against a not-yet-assigned bound.
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

    /// <summary>
    /// Makes one step — spin button, wheel or arrow key — a fixed fraction of an
    /// octave (a 96th) rather than the fixed <see cref="Increment"/>, so one wheel
    /// notch covers the same distance wherever it is taken on a logarithmic
    /// frequency axis. It is meant for fields in Hz, where no absolute step fits
    /// the whole band: the 10 Hz a crossover corner used to move by is nearly half
    /// an octave at 30 Hz and a rounding error at 15 kHz. The step comes out at
    /// 1 Hz at 100 Hz, 7 Hz at 1 kHz and 145 Hz at 20 kHz — rounded to what the
    /// field can show and never below one unit of it, which is why a whole-Hz field
    /// under about 69 Hz moves by 1 Hz and so covers more than a 96th of an octave
    /// there. The steps walk a ladder anchored on wherever the value last came from,
    /// so a step and a step straight back always land on the value they left, whichever
    /// way round they are taken. Off by default, which leaves the control stepping by
    /// <see cref="Increment"/> as before.
    /// </summary>
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

    /// <summary>
    /// Optional caption painted inside the field, glued to the inner-left edge in
    /// a muted half-tone. It reserves no space: the value is still right-aligned
    /// across the full field and its digits draw over the caption. Empty by
    /// default, which leaves the control's behaviour unchanged.
    /// </summary>
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
            // With a caption the value is self-painted at rest so the caption can
            // show behind it; the editor only appears while editing.
            UpdateEditorVisibility();
            Invalidate();
        }
    }

    /// <summary>
    /// Optional unit painted just to the right of the value inside the field
    /// (e.g. "dB" or "Hz"), in a muted tone so the number stays primary. It
    /// reserves its own space, so the value never overlaps it. Empty by default.
    /// </summary>
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

    /// <summary>
    /// Optional default value. When set, a small "R" reset button appears to the
    /// right of the spin buttons that restores this value.
    /// </summary>
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
        // A disabled control paints its own value (see UpdateEditorVisibility) — the
        // native EDIT under a disabled parent is coloured by Windows, not by us.
        // ReadOnly is a safety net against programmatic edits meanwhile.
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
        // A hidden editor (inline-label mode, at rest) cannot take focus; reveal
        // it first so typing works and the caret shows.
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
        // Only a focused (i.e. clicked-into) field responds to the wheel. Merely
        // hovering must not step the value — otherwise scrolling the channel list
        // silently edits whatever field the cursor happens to pass over. When
        // unfocused the wheel is left unconsumed so it bubbles to the AutoScroll
        // parent and scrolls the list as expected.
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

        // Consume the wheel so it only steps the value: otherwise WinForms bubbles
        // it to an AutoScroll parent (e.g. the scrolling channel list) which then
        // scrolls instead. The inner editor forwards its wheel here too, so this
        // covers hovering over the number as well as the spin buttons.
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

        // Whenever the editor is hidden — inline-label mode at rest, or a disabled
        // control, which hides it so the value is not left to the system's grey —
        // paint the value here: the caption, if any, sits at the inner-left in a
        // half-tone, and the value is right-aligned across the whole field and
        // drawn last so its digits cover the caption where they meet.
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

        // The unit sits in the reserved slice just right of the number (whose
        // right edge is the editor's right edge because the value is
        // right-aligned), in a muted tone so the number stays primary.
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

    // A leading space separates the unit from the number ("-15 dB", "20000 Hz").
    private string SuffixDisplay => " " + valueSuffix;

    private int MeasureSuffixWidth() => HasSuffix
        ? TextRenderer.MeasureText(SuffixDisplay, Font, Size.Empty, TextFormatFlags.NoPadding).Width
        : 0;

    // The editor is opaque and would hide the inline caption, so in inline-label
    // mode it is shown only while the control is focused (i.e. being edited);
    // otherwise the value is self-painted with the caption behind it. Without an
    // inline label the editor is always visible and behaviour is unchanged.
    private void UpdateEditorVisibility()
    {
        // A disabled control hides the editor and paints the value itself. Keeping
        // the inner EDIT's own Enabled=true is not enough: a native edit under a
        // DISABLED PARENT is painted by Windows in the system's grey (109,109,109)
        // whatever ForeColor says — 2.5:1 here, and it is the value in force that
        // goes unreadable (#116). Self-painting is the only way the palette's
        // colour actually reaches those digits.
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
            // A dialog's AcceptButton consumes Enter before the editor's KeyDown
            // ever fires; commit here so the accept handler reads the typed text
            // rather than the last committed value.
            bool hadPendingEdit = HasPendingEditorText;
            CommitEditorText();

            // The Enter that lands a typed number stops here. Letting it through as
            // well would fire the dialog's default button in the same keystroke —
            // in the Virtual DSP auto-setup that means running the whole crossover
            // proposal and closing the window while the user was still filling in a
            // field. A second Enter, with nothing pending, reaches the accept button
            // as usual, so the keyboard route to OK survives.
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
        // DeviceDpi is only final once the handle exists in its monitor's context.
        // A control created at runtime (e.g. an added Virtual DSP channel) lays its
        // editor out at the default 96 DPI in the constructor, so without this the
        // text stays offset inside a higher-DPI field. Designer-placed instances are
        // masked by the form's startup scale pass; runtime-added ones are not.
        LayoutEditor();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        // Moving the window to a monitor at another scale re-fires this; the inner
        // editor must be re-laid-out for the new DeviceDpi.
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

    // True while the editor holds text that is not the committed value's own rendering:
    // a half-typed number, an edited one, or something unparseable. Comparing against
    // FormatValue is what UpdateEditorText writes, so an untouched field reads false.
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

        // When the value is self-painted (inline-label mode, editor hidden), the
        // editor's own repaint does not cover it, so refresh the control.
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

    // Snaps back to the default. Shares the read-only guard with the step paths so
    // the reset button honours the lock too — otherwise ReadOnly blocks typing and
    // spinning but a reset click still rewrites the value.
    private void ResetToDefault()
    {
        if (readOnly || !defaultValue.HasValue)
        {
            return;
        }

        Value = defaultValue.Value;
    }

    // The ladder a logarithmic step walks: rungs at anchor × 2 ^ (n / 96), rounded to
    // what the field displays. Anchoring the ladder — rather than measuring a fresh
    // step off the current value every time — is what makes the two directions exact
    // opposites, so a step and a step straight back always land on the value they
    // left. A measured step cannot promise that: the ratio rounds one way going out
    // and another coming back, which sent 347 Hz down to 345 and back up to 348.
    // The ladder is rebuilt wherever the value came from something other than a step —
    // typed, loaded with a session, fitted by Auto Tune — and a value that is no
    // longer the rung the last step left on is exactly what says so.
    private decimal NextRung(int direction)
    {
        if (value != RungValue(logarithmicPosition))
        {
            logarithmicAnchor = value;
            logarithmicPosition = 0;
        }

        if (value <= 0)
        {
            // A logarithmic ladder says nothing about zero or below.
            return value + (direction * SmallestDisplayableStep);
        }

        // Rungs that round onto the value we are already on are stepped over: below
        // about 69 Hz a 96th of an octave is under half a Hz, and a spin button that
        // moves nothing reads as a broken control.
        int position = logarithmicPosition;
        decimal rung;
        do
        {
            position += direction;
            rung = RungValue(position);
        }
        while (direction > 0 ? rung <= value : rung >= value);

        logarithmicPosition = position;
        return rung;
    }

    private decimal RungValue(int position) => RoundToDecimalPlaces((decimal)(
        (double)logarithmicAnchor *
        Math.Pow(2, position / (double)LogarithmicStepsPerOctave)));

    // The smallest change the field can show: 1, 0.1, 0.01 ... for its decimal places
    // (the decimal constructor's scale argument is exactly that power of ten).
    private decimal SmallestDisplayableStep => new decimal(1, 0, 0, false, (byte)decimalPlaces);

    // Commit first: stepping must apply to what the user typed, not overwrite
    // uncommitted editor text with lastCommitted ± the step — and in logarithmic mode
    // that committed value is also what the ladder is rebuilt on. A read-only field
    // ignores every step path alike (spin buttons, wheel, arrow keys, reset) — the
    // single choke point that makes ReadOnly a true lock, not just a typing block.
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

        decimal rung = NextRung(direction);
        Value = rung;
        if (value != rung)
        {
            // The range clamped the step; rebuild the ladder on where it actually
            // landed rather than leave it pointing at a rung outside the range.
            logarithmicAnchor = value;
            logarithmicPosition = 0;
        }
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
        // The unit suffix (if any) takes the rightmost slice of the text region;
        // the editor keeps the rest so the number never overlaps the unit.
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

    /// <summary>
    /// Assigns a tooltip to the control and its inner text editor so it shows
    /// regardless of whether the cursor is over the number or the spin buttons.
    /// </summary>
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
        // Reconcile the batched assignments now that every property has landed:
        // designer property order can no longer clamp Value against a default bound.
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
