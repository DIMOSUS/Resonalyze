namespace Resonalyze;

internal sealed class OverlaySettingsDialog : Form
{
    private readonly TextBox nameTextBox = new();
    private readonly Button colorButton = new();
    private readonly NumericUpDown thicknessInput = new();
    private readonly ComboBox styleComboBox = new();
    private readonly ComboBox smoothingComboBox = new();
    private readonly TrackBar opacityTrackBar = new();
    private readonly Label opacityValueLabel = new();
    private readonly bool supportsSmoothing;
    private Color selectedColor;

    public OverlaySettingsDialog(
        Mode mode,
        string name,
        Color color,
        double strokeThickness,
        OverlayLineStyle lineStyle,
        int opacityPercent,
        int smoothingInverseOctaves)
    {
        supportsSmoothing = OverlaySmoothing.SupportsMode(mode);
        selectedColor = color;
        InitializeDialog();

        nameTextBox.Text = name;
        thicknessInput.Value = (decimal)Math.Clamp(strokeThickness, 0.5, 10);
        styleComboBox.SelectedItem = lineStyle;
        smoothingComboBox.SelectedItem = smoothingInverseOctaves;
        opacityTrackBar.Value = Math.Clamp(opacityPercent, 10, 100);
        UpdateColorButton();
        UpdateOpacityLabel();
    }

    public string OverlayName => nameTextBox.Text.Trim();
    public Color SelectedColor => selectedColor;
    public double StrokeThickness => (double)thicknessInput.Value;
    public OverlayLineStyle LineStyle =>
        (OverlayLineStyle)styleComboBox.SelectedItem!;
    public int OpacityPercent => opacityTrackBar.Value;
    public int SmoothingInverseOctaves => supportsSmoothing
        ? (int)smoothingComboBox.SelectedItem!
        : 0;
    public bool ClearRequested { get; private set; }

    private void InitializeDialog()
    {
        SuspendLayout();

        UiStyle.ApplyDarkDialog(
            this,
            new Size(440, supportsSmoothing ? 360 : 300),
            title: "Overlay settings");

        AddSectionTitle("Overlay", 18);
        AddLabel("Name", 52);
        ConfigureInput(nameTextBox, new Point(20, 72), new Size(400, 24));
        nameTextBox.MaxLength = 80;

        AddLabel("Color", 112);
        colorButton.Location = new Point(20, 132);
        colorButton.Size = new Size(122, 30);
        UiStyle.ApplySurfaceButton(colorButton, UiPalette.DialogSurfaceMuted);
        colorButton.Click += ColorButtonClick;

        AddLabel("Thickness", 112, 162);
        thicknessInput.Location = new Point(162, 132);
        thicknessInput.Size = new Size(90, 24);
        UiStyle.ApplyNumericUpDown(thicknessInput, thicknessInput.Location, thicknessInput.Size);
        thicknessInput.DecimalPlaces = 1;
        thicknessInput.Increment = 0.5m;
        thicknessInput.Minimum = 0.5m;
        thicknessInput.Maximum = 10;

        AddLabel("Style", 112, 272);
        styleComboBox.Location = new Point(272, 132);
        styleComboBox.Size = new Size(148, 24);
        UiStyle.ApplyComboBox(styleComboBox, styleComboBox.Location, styleComboBox.Size);
        styleComboBox.DataSource = Enum.GetValues<OverlayLineStyle>();

        int opacityLabelY = supportsSmoothing ? 238 : 178;
        int opacityControlY = supportsSmoothing ? 258 : 198;
        int buttonY = supportsSmoothing ? 315 : 255;

        if (supportsSmoothing)
        {
            AddLabel("Smoothing", 178);
            UiStyle.ApplyComboBox(
                smoothingComboBox,
                new Point(20, 198),
                new Size(400, 24));
            smoothingComboBox.FormattingEnabled = true;
            foreach (int value in OverlaySmoothing.SupportedInverseOctaves)
            {
                smoothingComboBox.Items.Add(value);
            }
            smoothingComboBox.Format += (_, args) =>
            {
                if (args.ListItem is int value)
                {
                    args.Value = OverlaySmoothing.GetLabel(value);
                }
            };
            Controls.Add(smoothingComboBox);
        }

        AddLabel("Opacity", opacityLabelY);
        opacityTrackBar.Location = new Point(14, opacityControlY);
        opacityTrackBar.Size = new Size(340, 40);
        opacityTrackBar.Minimum = 10;
        opacityTrackBar.Maximum = 100;
        opacityTrackBar.TickFrequency = 10;
        opacityTrackBar.ValueChanged += (_, _) => UpdateOpacityLabel();
        opacityValueLabel.AutoSize = true;
        opacityValueLabel.Location = new Point(370, opacityControlY + 7);

        var cancelButton = OverlayDialogControls.CreateDialogButton(
            "Cancel",
            DialogResult.Cancel,
            accent: false);
        cancelButton.Location = new Point(226, buttonY);
        var clearButton = OverlayDialogControls.CreateDialogButton(
            "Clear",
            DialogResult.OK,
            accent: false);
        clearButton.Location = new Point(20, buttonY);
        clearButton.Click += (_, _) => ClearRequested = true;
        var saveButton = OverlayDialogControls.CreateDialogButton(
            "Save",
            DialogResult.OK,
            accent: true);
        saveButton.Location = new Point(326, buttonY);
        saveButton.Click += (_, _) =>
        {
            if (OverlayName.Length == 0)
            {
                DialogResult = DialogResult.None;
                System.Media.SystemSounds.Beep.Play();
                nameTextBox.Focus();
            }
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Controls.AddRange(
        [
            nameTextBox,
            colorButton,
            thicknessInput,
            styleComboBox,
            opacityTrackBar,
            opacityValueLabel,
            clearButton,
            cancelButton,
            saveButton
        ]);

        OverlayDialogControls.ApplyRuntimeDpiScale(this);
        ResumeLayout(false);
        PerformLayout();
    }

    private void ColorButtonClick(object? sender, EventArgs e)
    {
        using var dialog = new ColorPickerDialog(selectedColor);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            selectedColor = dialog.SelectedColor;
            UpdateColorButton();
        }
    }

    private void UpdateColorButton()
    {
        colorButton.BackColor = selectedColor;
        colorButton.Text =
            $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
        colorButton.FlatAppearance.BorderColor =
            UiPalette.DialogBorder;
    }

    private void UpdateOpacityLabel()
    {
        opacityValueLabel.Text = $"{opacityTrackBar.Value}%";
    }

    private void AddSectionTitle(string text, int y)
    {
        Controls.Add(UiStyle.CreateLabel(
            text,
            new Point(20, y),
            ForeColor,
            new Font(Font, FontStyle.Bold)));
    }

    private void AddLabel(string text, int y, int x = 20)
    {
        Controls.Add(UiStyle.CreateLabel(text, new Point(x, y), UiPalette.TextSecondary, Font));
    }

    private static void ConfigureInput(
        Control control,
        Point location,
        Size size)
    {
        UiStyle.ApplySurfaceInput(control, location, size);
    }
}

internal static class OverlayDialogControls
{
    public static void ApplyRuntimeDpiScale(Form form)
    {
        float factor = GetRuntimeDpiScale(form);
        if (factor <= 1.01f)
        {
            return;
        }

        form.ClientSize = ScaleSize(form.ClientSize, factor);
        form.Padding = ScalePadding(form.Padding, factor);
        foreach (Control child in form.Controls)
        {
            ScaleControlTree(child, factor);
        }
    }

    public static Button CreateDialogButton(
        string text,
        DialogResult result,
        bool accent)
    {
        return UiStyle.CreateDialogButton(text, result, accent);
    }

    public static Label CreateLabel(
        string text,
        Point location,
        Color color,
        Font font)
    {
        return UiStyle.CreateLabel(text, location, color, font);
    }

    private static void ScaleControlTree(Control control, float factor)
    {
        control.Bounds = new Rectangle(
            Scale(control.Left, factor),
            Scale(control.Top, factor),
            Math.Max(1, Scale(control.Width, factor)),
            Math.Max(1, Scale(control.Height, factor)));
        control.Margin = ScalePadding(control.Margin, factor);
        control.Padding = ScalePadding(control.Padding, factor);

        foreach (Control child in control.Controls)
        {
            ScaleControlTree(child, factor);
        }
    }

    private static float GetRuntimeDpiScale(Form form)
    {
        using Graphics graphics = form.CreateGraphics();
        return Math.Max(form.DeviceDpi / 96.0f, graphics.DpiX / 96.0f);
    }

    private static Size ScaleSize(Size size, float factor) =>
        new(Scale(size.Width, factor), Scale(size.Height, factor));

    private static Padding ScalePadding(Padding padding, float factor) =>
        new(
            Scale(padding.Left, factor),
            Scale(padding.Top, factor),
            Scale(padding.Right, factor),
            Scale(padding.Bottom, factor));

    private static int Scale(int value, float factor) =>
        (int)Math.Round(value * factor);
}
