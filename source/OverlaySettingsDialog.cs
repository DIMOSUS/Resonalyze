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

    private void InitializeDialog()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(40, 42, 48);
        ClientSize = new Size(440, supportsSmoothing ? 360 : 300);
        Font = new Font("Segoe UI", 9F);
        ForeColor = Color.FromArgb(235, 237, 240);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = new Padding(20);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Overlay settings";

        AddSectionTitle("Overlay", 18);
        AddLabel("Name", 52);
        ConfigureInput(nameTextBox, new Point(20, 72), new Size(400, 24));
        nameTextBox.MaxLength = 80;

        AddLabel("Color", 112);
        colorButton.Location = new Point(20, 132);
        colorButton.Size = new Size(122, 30);
        colorButton.FlatStyle = FlatStyle.Flat;
        colorButton.ForeColor = Color.White;
        colorButton.Click += ColorButtonClick;

        AddLabel("Thickness", 112, 162);
        thicknessInput.Location = new Point(162, 132);
        thicknessInput.Size = new Size(90, 24);
        thicknessInput.BackColor = Color.FromArgb(55, 58, 65);
        thicknessInput.ForeColor = Color.White;
        thicknessInput.DecimalPlaces = 1;
        thicknessInput.Increment = 0.5m;
        thicknessInput.Minimum = 0.5m;
        thicknessInput.Maximum = 10;

        AddLabel("Style", 112, 272);
        styleComboBox.Location = new Point(272, 132);
        styleComboBox.Size = new Size(148, 24);
        styleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        styleComboBox.BackColor = Color.FromArgb(55, 58, 65);
        styleComboBox.ForeColor = Color.White;
        styleComboBox.DataSource = Enum.GetValues<OverlayLineStyle>();

        int opacityLabelY = supportsSmoothing ? 238 : 178;
        int opacityControlY = supportsSmoothing ? 258 : 198;
        int buttonY = supportsSmoothing ? 315 : 255;

        if (supportsSmoothing)
        {
            AddLabel("Smoothing", 178);
            ConfigureInput(
                smoothingComboBox,
                new Point(20, 198),
                new Size(400, 24));
            smoothingComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
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
            cancelButton,
            saveButton
        ]);

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
            Color.FromArgb(100, 105, 115);
    }

    private void UpdateOpacityLabel()
    {
        opacityValueLabel.Text = $"{opacityTrackBar.Value}%";
    }

    private void AddSectionTitle(string text, int y)
    {
        Controls.Add(OverlayDialogControls.CreateLabel(
            text,
            new Point(20, y),
            ForeColor,
            new Font(Font, FontStyle.Bold)));
    }

    private void AddLabel(string text, int y, int x = 20)
    {
        Controls.Add(OverlayDialogControls.CreateLabel(
            text,
            new Point(x, y),
            Color.FromArgb(185, 190, 200),
            Font));
    }

    private static void ConfigureInput(
        Control control,
        Point location,
        Size size)
    {
        control.BackColor = Color.FromArgb(55, 58, 65);
        control.ForeColor = Color.White;
        control.Location = location;
        control.Size = size;
    }
}

internal static class OverlayDialogControls
{
    public static Button CreateDialogButton(
        string text,
        DialogResult result,
        bool accent)
    {
        var button = new Button
        {
            BackColor = accent
                ? Color.FromArgb(64, 116, 255)
                : Color.FromArgb(62, 65, 73),
            DialogResult = result,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Size = new Size(94, 30),
            Text = text,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    public static Label CreateLabel(
        string text,
        Point location,
        Color color,
        Font font)
    {
        return new Label
        {
            AutoSize = true,
            Font = font,
            ForeColor = color,
            Location = location,
            Text = text
        };
    }
}
