using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Windows.Forms.Design;

namespace Resonalyze;

[DefaultEvent(nameof(SelectedIndexChanged))]
[DesignerCategory("Code")]
public sealed class DarkComboBox : UserControl
{
    private const int LogicalButtonWidth = 18;
    private const int LogicalArrowHalfWidth = 4;
    private const int LogicalArrowHalfHeight = 2;
    private const int LogicalHorizontalPadding = 6;
    private const int LogicalTextButtonGap = 4;
    private const int LogicalBorderThickness = 1;
    private const int LogicalItemVerticalPadding = 2;
    private const int DefaultMaxDropDownItems = 8;

    private readonly ComboBox modelComboBox;
    private readonly Panel buttonPanel;
    private readonly Panel resetPanel;
    private ToolStripDropDown? popupDropDown;
    private ListBox? popupListBox;
    private ToolStripControlHost? popupHost;
    private bool dropDownVisible;
    private bool buttonHovered;
    private bool buttonPressed;
    private bool resetHovered;
    private bool resetPressed;
    private object? defaultSelectedItem;
    private long suppressToggleUntilTick;

    public DarkComboBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);

        BackColor = UiPalette.ControlSurface;
        ForeColor = UiPalette.TextPrimary;
        Margin = new Padding(3);
        Size = new Size(121, 19);
        MinimumSize = new Size(36, 19);
        TabStop = true;

        modelComboBox = new ComboBox
        {
            DrawMode = DrawMode.Normal,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            IntegralHeight = false,
            Visible = false
        };
        modelComboBox.SelectedIndexChanged += ModelComboBoxSelectedIndexChanged;

        buttonPanel = new Panel
        {
            BackColor = Color.Transparent,
            Cursor = Cursors.Default,
            Margin = Padding.Empty,
            TabStop = false
        };
        buttonPanel.Paint += ButtonPanelPaint;
        buttonPanel.MouseEnter += (_, _) =>
        {
            buttonHovered = true;
            buttonPanel.Invalidate();
        };
        buttonPanel.MouseLeave += (_, _) =>
        {
            buttonHovered = false;
            buttonPressed = false;
            buttonPanel.Invalidate();
        };
        buttonPanel.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !Enabled)
            {
                return;
            }

            if (!ContainsFocus)
            {
                Focus();
            }

            buttonPressed = true;
            buttonPanel.Invalidate();
        };
        buttonPanel.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            bool wasPressed = buttonPressed;
            buttonPressed = false;
            buttonPanel.Invalidate();
            if (wasPressed &&
                Enabled &&
                buttonPanel.ClientRectangle.Contains(e.Location) &&
                !ShouldSuppressToggle())
            {
                ToggleDropDown();
            }
        };

        resetPanel = new Panel
        {
            BackColor = Color.Transparent,
            Cursor = Cursors.Default,
            Margin = Padding.Empty,
            TabStop = false,
            Visible = false
        };
        resetPanel.Paint += ResetPanelPaint;
        resetPanel.MouseEnter += (_, _) =>
        {
            resetHovered = true;
            resetPanel.Invalidate();
        };
        resetPanel.MouseLeave += (_, _) =>
        {
            resetHovered = false;
            resetPressed = false;
            resetPanel.Invalidate();
        };
        resetPanel.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !Enabled)
            {
                return;
            }

            if (!ContainsFocus)
            {
                Focus();
            }

            resetPressed = true;
            resetPanel.Invalidate();
        };
        resetPanel.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            bool wasPressed = resetPressed;
            resetPressed = false;
            resetPanel.Invalidate();
            if (wasPressed &&
                Enabled &&
                resetPanel.ClientRectangle.Contains(e.Location))
            {
                ResetToDefault();
            }
        };

        Controls.Add(modelComboBox);
        Controls.Add(buttonPanel);
        Controls.Add(resetPanel);
        buttonPanel.BringToFront();
        resetPanel.BringToFront();

        LayoutInnerControls();
    }

    /// <summary>
    /// Optional default selection. When set, a small "R" reset button appears to the
    /// right of the drop-down arrow that restores this item.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? DefaultSelectedItem
    {
        get => defaultSelectedItem;
        set
        {
            defaultSelectedItem = value;
            LayoutInnerControls();
            Invalidate();
        }
    }

    private bool ShowResetButton => defaultSelectedItem != null;

    private void ResetToDefault()
    {
        if (defaultSelectedItem == null)
        {
            return;
        }

        SelectedItem = defaultSelectedItem;

        // Reset is a deliberate user commit; raise the committed event so listeners
        // (e.g. the docked settings host) apply it, like picking from the drop-down.
        SelectionChangeCommitted?.Invoke(this, EventArgs.Empty);
    }

    [Browsable(true)]
    [DefaultValue(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool FormattingEnabled
    {
        get => modelComboBox.FormattingEnabled;
        set => modelComboBox.FormattingEnabled = value;
    }

    [Browsable(true)]
    [DefaultValue(typeof(ComboBoxStyle), "DropDownList")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public ComboBoxStyle DropDownStyle
    {
        get => modelComboBox.DropDownStyle;
        set => modelComboBox.DropDownStyle = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ComboBox.ObjectCollection Items => modelComboBox.Items;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? DataSource
    {
        get => modelComboBox.DataSource;
        set
        {
            modelComboBox.DataSource = value;
            SyncPopupSelection();
            Invalidate();
        }
    }

    [Browsable(true)]
    [DefaultValue("")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string DisplayMember
    {
        get => modelComboBox.DisplayMember;
        set => modelComboBox.DisplayMember = value;
    }

    [Browsable(true)]
    [DefaultValue("")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string ValueMember
    {
        get => modelComboBox.ValueMember;
        set => modelComboBox.ValueMember = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => modelComboBox.SelectedItem;
        set
        {
            modelComboBox.SelectedItem = value;
            SyncPopupSelection();
            Invalidate();
        }
    }

    [Browsable(true)]
    [DefaultValue(-1)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int SelectedIndex
    {
        get => modelComboBox.SelectedIndex;
        set
        {
            modelComboBox.SelectedIndex = value;
            SyncPopupSelection();
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedValue
    {
        get => modelComboBox.SelectedValue;
        set
        {
            if (value == null)
            {
                modelComboBox.SelectedIndex = -1;
            }
            else
            {
                modelComboBox.SelectedValue = value;
            }

            SyncPopupSelection();
            Invalidate();
        }
    }

    [Browsable(true)]
    [DefaultValue(106)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int DropDownWidth { get; set; } = 106;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ItemHeight
    {
        get => GetPopupItemHeight();
        set
        {
            EnsurePopupCreated();
            popupListBox!.ItemHeight = value;
        }
    }

    [Browsable(true)]
    [DefaultValue(DefaultMaxDropDownItems)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int MaxDropDownItems { get; set; } = DefaultMaxDropDownItems;

    [Browsable(true)]
    public event EventHandler? SelectedIndexChanged;

    [Browsable(true)]
    public event EventHandler? SelectionChangeCommitted;

    [Browsable(true)]
    public event ListControlConvertEventHandler? Format
    {
        add => modelComboBox.Format += value;
        remove => modelComboBox.Format -= value;
    }

    public string GetItemText(object? item)
    {
        return modelComboBox.GetItemText(item) ?? string.Empty;
    }

    public override Color BackColor
    {
        get => base.BackColor;
        set
        {
            base.BackColor = value;
            if (popupListBox != null)
            {
                popupListBox.BackColor = value;
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
            if (popupListBox != null)
            {
                popupListBox.ForeColor = value;
            }

            Invalidate();
        }
    }

    [AllowNull]
    public override Font Font
    {
        get => base.Font;
        set
        {
            base.Font = value;
            if (popupListBox != null)
            {
                popupListBox.Font = value;
                popupListBox.ItemHeight = GetPopupItemHeight();
            }

            LayoutInnerControls();
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (popupDropDown != null)
            {
                popupDropDown.Closed -= PopupDropDownClosed;
                popupDropDown.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (buttonPanel == null)
        {
            return;
        }

        buttonPanel.Enabled = Enabled;
        if (!Enabled)
        {
            HideDropDown();
        }

        Invalidate();
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutInnerControls();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        LayoutInnerControls();
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        LayoutInnerControls();
        if (popupListBox != null)
        {
            popupListBox.ItemHeight = GetPopupItemHeight();
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Up or Keys.Down or Keys.Space
            ? true
            : base.IsInputKey(keyData);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Enabled)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Enter is deliberately not handled: like a native ComboBox, it must
        // reach the dialog so an AcceptButton can fire while the combo has focus.
        switch (keyData)
        {
            case Keys.F4:
            case Keys.Space:
            case Keys.Alt | Keys.Down:
                ToggleDropDown();
                return true;
            case Keys.Escape when dropDownVisible:
                HideDropDown();
                return true;
            case Keys.Down:
                MoveSelection(+1, committed: false);
                return true;
            case Keys.Up:
                MoveSelection(-1, committed: false);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // A UserControl does not take focus on click by itself; without this the
        // combo never shows keyboard focus and arrow keys keep going elsewhere.
        if (Enabled && !ContainsFocus)
        {
            Focus();
        }

        if (!buttonPanel.Bounds.Contains(e.Location) &&
            !(resetPanel.Visible && resetPanel.Bounds.Contains(e.Location)) &&
            !ShouldSuppressToggle())
        {
            ToggleDropDown();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Enabled || dropDownVisible || modelComboBox.Items.Count == 0)
        {
            return;
        }

        MoveSelection(e.Delta > 0 ? -1 : +1, committed: true);

        // Consume the wheel so it only changes the selection, and does not bubble
        // to an AutoScroll parent (e.g. the scrolling channel list) which would
        // scroll the panel instead.
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
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

        using var background = new SolidBrush(Enabled ? BackColor : UiPalette.DialogSurface);
        e.Graphics.FillRectangle(background, bounds);

        Color borderColor = dropDownVisible || ContainsFocus
            ? UiPalette.AccentBlueSoft
            : UiPalette.DialogBorder;
        using var borderPen = new Pen(borderColor, ScaleLogical(LogicalBorderThickness));
        e.Graphics.DrawRectangle(borderPen, 0, 0, bounds.Width - 1, bounds.Height - 1);

        int buttonWidth = GetButtonWidth();
        int resetWidth = ShowResetButton ? buttonWidth : 0;
        int separatorX = Math.Max(0, bounds.Width - buttonWidth - resetWidth - 1);
        using var separatorPen = new Pen(UiPalette.DialogBorderSoft);
        e.Graphics.DrawLine(separatorPen, separatorX, 1, separatorX, bounds.Height - 2);
        if (ShowResetButton)
        {
            int resetSeparatorX = Math.Max(0, bounds.Width - resetWidth - 1);
            e.Graphics.DrawLine(separatorPen, resetSeparatorX, 1, resetSeparatorX, bounds.Height - 2);
        }

        Rectangle textBounds = new(
            ScaleLogical(LogicalHorizontalPadding),
            1,
            Math.Max(
                1,
                separatorX - ScaleLogical(LogicalHorizontalPadding) - ScaleLogical(LogicalTextButtonGap)),
            Math.Max(1, bounds.Height - 2));
        string text = modelComboBox.GetItemText(modelComboBox.SelectedItem) ?? string.Empty;
        Color textColor = Enabled
            ? UiPalette.TextPrimary
            : UiPalette.TextMuted;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            textBounds,
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding);
    }

    private void ButtonPanelPaint(object? sender, PaintEventArgs e)
    {
        Rectangle bounds = buttonPanel.ClientRectangle;
        Color buttonColor = !Enabled
            ? UiPalette.DialogSurfaceMuted
            : buttonPressed || dropDownVisible
                ? UiPalette.ButtonPressedBackground
                : buttonHovered
                    ? UiPalette.ButtonHoverBackground
                    : UiPalette.ButtonBackground;
        using var backgroundBrush = new SolidBrush(buttonColor);
        e.Graphics.FillRectangle(backgroundBrush, bounds);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int halfWidth = ScaleLogical(LogicalArrowHalfWidth);
        int halfHeight = ScaleLogical(LogicalArrowHalfHeight);
        int centerX = bounds.Width / 2;
        int centerY = bounds.Height / 2 + ScaleLogical(1);
        using var arrowBrush = new SolidBrush(Enabled ? UiPalette.TextPrimary : UiPalette.TextMuted);
        e.Graphics.FillPolygon(
            arrowBrush,
            [
                new PointF(centerX - halfWidth, centerY - halfHeight),
                new PointF(centerX + halfWidth, centerY - halfHeight),
                new PointF(centerX, centerY + halfHeight)
            ]);
    }

    private void ResetPanelPaint(object? sender, PaintEventArgs e)
    {
        Rectangle bounds = resetPanel.ClientRectangle;
        Color buttonColor = !Enabled
            ? UiPalette.DialogSurfaceMuted
            : resetPressed
                ? UiPalette.ButtonPressedBackground
                : resetHovered
                    ? UiPalette.ButtonHoverBackground
                    : UiPalette.ButtonBackground;
        using var backgroundBrush = new SolidBrush(buttonColor);
        e.Graphics.FillRectangle(backgroundBrush, bounds);

        Color glyphColor = Enabled ? UiPalette.TextPrimary : UiPalette.TextMuted;
        TextRenderer.DrawText(
            e.Graphics,
            "R",
            Font,
            bounds,
            glyphColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding);
    }

    private void PopupListBoxDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || popupListBox == null)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color backgroundColor = selected
            ? UiPalette.AccentBlueStrong
            : UiPalette.ControlSurface;
        Color textColor = Enabled
            ? UiPalette.TextPrimary
            : UiPalette.TextMuted;

        using var backgroundBrush = new SolidBrush(backgroundColor);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

        if (selected)
        {
            using var markerBrush = new SolidBrush(UiPalette.AccentBlueSoft);
            e.Graphics.FillRectangle(
                markerBrush,
                new Rectangle(
                    e.Bounds.Left,
                    e.Bounds.Top,
                    ScaleLogical(3),
                    e.Bounds.Height));

            using var selectionPen = new Pen(UiPalette.AccentBlueSoft);
            Rectangle selectionBounds = Rectangle.Inflate(e.Bounds, -1, -1);
            e.Graphics.DrawRectangle(
                selectionPen,
                selectionBounds.X,
                selectionBounds.Y,
                selectionBounds.Width - 1,
                selectionBounds.Height - 1);
        }

        Rectangle textBounds = Rectangle.Inflate(e.Bounds, -ScaleLogical(LogicalHorizontalPadding), 0);
        string text = modelComboBox.GetItemText(popupListBox.Items[e.Index]) ?? string.Empty;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            textBounds,
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding);
    }

    private void PopupListBoxMouseClick(object? sender, MouseEventArgs e)
    {
        if (popupListBox == null || e.Button != MouseButtons.Left)
        {
            return;
        }

        int index = popupListBox.IndexFromPoint(e.Location);
        if (index >= 0)
        {
            CommitPopupSelection(index);
        }
    }

    private void PopupListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (popupListBox == null)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Enter:
                if (popupListBox.SelectedIndex >= 0)
                {
                    CommitPopupSelection(popupListBox.SelectedIndex);
                }

                e.Handled = true;
                break;
            case Keys.Escape:
                HideDropDown();
                e.Handled = true;
                break;
        }
    }

    private void ModelComboBoxSelectedIndexChanged(object? sender, EventArgs e)
    {
        SyncPopupSelection();
        SelectedIndexChanged?.Invoke(this, e);
        Invalidate();
    }

    private void PopupDropDownClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        dropDownVisible = false;
        buttonPressed = false;
        suppressToggleUntilTick = Environment.TickCount64 + 150;
        SyncPopupSelection();
        Invalidate();
        buttonPanel.Invalidate();
    }

    private void EnsurePopupCreated()
    {
        if (popupDropDown != null && popupListBox != null && popupHost != null)
        {
            return;
        }

        popupListBox = new ListBox
        {
            BackColor = UiPalette.ControlSurface,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ForeColor = UiPalette.TextPrimary,
            FormattingEnabled = true,
            IntegralHeight = false,
            ItemHeight = GetPopupItemHeight(),
            Margin = Padding.Empty,
            SelectionMode = SelectionMode.One
        };
        popupListBox.DrawItem += PopupListBoxDrawItem;
        popupListBox.MouseClick += PopupListBoxMouseClick;
        popupListBox.KeyDown += PopupListBoxKeyDown;

        var popupPanel = new Panel
        {
            BackColor = UiPalette.ControlSurface,
            Padding = new Padding(1),
            Margin = Padding.Empty
        };
        popupPanel.Paint += (_, e) =>
        {
            using var borderPen = new Pen(UiPalette.AccentBlueSoft);
            e.Graphics.DrawRectangle(
                borderPen,
                0,
                0,
                popupPanel.Width - 1,
                popupPanel.Height - 1);
        };
        popupPanel.Controls.Add(popupListBox);

        popupHost = new ToolStripControlHost(popupPanel)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        popupDropDown = new ToolStripDropDown
        {
            AutoClose = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        popupDropDown.Items.Add(popupHost);
        popupDropDown.Closed += PopupDropDownClosed;
    }

    private void ToggleDropDown()
    {
        if (ShouldSuppressToggle())
        {
            return;
        }

        if (dropDownVisible)
        {
            HideDropDown();
        }
        else
        {
            ShowDropDown();
        }
    }

    private void ShowDropDown()
    {
        if (!Enabled)
        {
            return;
        }

        EnsurePopupCreated();
        RebuildPopupItems();

        if (popupListBox == null || popupDropDown == null || popupHost == null)
        {
            return;
        }

        int width = Math.Max(Width, DropDownWidth);
        int visibleItems = Math.Max(1, Math.Min(MaxDropDownItems, popupListBox.Items.Count));
        int listHeight = Math.Max(
            popupListBox.ItemHeight + ScaleLogical(2),
            visibleItems * popupListBox.ItemHeight + ScaleLogical(2));
        var popupPanel = (Panel)popupHost.Control;
        popupPanel.Size = new Size(width, listHeight + 2);
        popupListBox.Bounds = new Rectangle(1, 1, width - 2, listHeight);
        popupHost.Size = popupPanel.Size;

        popupListBox.SelectedIndex = GetPopupSafeSelectedIndex();
        dropDownVisible = true;
        Invalidate();
        buttonPanel.Invalidate();

        Point screenLocation = PointToScreen(new Point(0, Height - 1));
        popupDropDown.Show(screenLocation);
        popupListBox.Focus();
    }

    private void HideDropDown()
    {
        popupDropDown?.Close(ToolStripDropDownCloseReason.CloseCalled);
    }

    private void RebuildPopupItems()
    {
        if (popupListBox == null)
        {
            return;
        }

        popupListBox.BeginUpdate();
        popupListBox.Items.Clear();
        foreach (object? item in modelComboBox.Items)
        {
            popupListBox.Items.Add(item);
        }

        popupListBox.EndUpdate();
    }

    private void SyncPopupSelection()
    {
        if (popupListBox != null)
        {
            if (popupListBox.Items.Count != modelComboBox.Items.Count)
            {
                RebuildPopupItems();
            }

            popupListBox.SelectedIndex = GetPopupSafeSelectedIndex();
        }
    }

    private int GetPopupSafeSelectedIndex()
    {
        int selectedIndex = modelComboBox.SelectedIndex;
        if (popupListBox == null ||
            selectedIndex < 0 ||
            selectedIndex >= popupListBox.Items.Count)
        {
            return -1;
        }

        return selectedIndex;
    }

    private void CommitPopupSelection(int index)
    {
        if (index < 0 || index >= modelComboBox.Items.Count)
        {
            return;
        }

        modelComboBox.SelectedIndex = index;
        SelectionChangeCommitted?.Invoke(this, EventArgs.Empty);
        HideDropDown();
    }

    private void MoveSelection(int delta, bool committed)
    {
        if (modelComboBox.Items.Count == 0)
        {
            return;
        }

        int currentIndex = modelComboBox.SelectedIndex;
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        int nextIndex = Math.Clamp(currentIndex + delta, 0, modelComboBox.Items.Count - 1);
        if (nextIndex == modelComboBox.SelectedIndex)
        {
            return;
        }

        modelComboBox.SelectedIndex = nextIndex;
        if (committed)
        {
            SelectionChangeCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void LayoutInnerControls()
    {
        if (buttonPanel == null)
        {
            return;
        }

        int buttonWidth = GetButtonWidth();
        int resetWidth = ShowResetButton ? buttonWidth : 0;

        if (resetPanel != null)
        {
            resetPanel.Visible = ShowResetButton;
            if (ShowResetButton)
            {
                resetPanel.Bounds = new Rectangle(
                    Math.Max(0, Width - resetWidth - 1),
                    1,
                    Math.Max(1, resetWidth),
                    Math.Max(1, Height - 2));
            }
        }

        buttonPanel.Bounds = new Rectangle(
            Math.Max(0, Width - buttonWidth - resetWidth - 1),
            1,
            Math.Max(1, buttonWidth),
            Math.Max(1, Height - 2));
        buttonPanel.BringToFront();
        resetPanel?.BringToFront();
    }

    private int GetPopupItemHeight()
    {
        return Math.Max(
            ScaleLogical(16),
            TextRenderer.MeasureText("Ag", Font).Height + ScaleLogical(LogicalItemVerticalPadding));
    }

    private int GetButtonWidth()
    {
        return ScaleLogical(LogicalButtonWidth);
    }

    private int ScaleLogical(int value)
    {
        float scale = DeviceDpi / 96f;
        return Math.Max(1, (int)Math.Round(value * scale));
    }

    private bool ShouldSuppressToggle()
    {
        return Environment.TickCount64 < suppressToggleUntilTick;
    }
}
