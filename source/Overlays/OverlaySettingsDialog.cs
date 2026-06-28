namespace Resonalyze;

internal sealed partial class OverlaySettingsDialog : Form
{
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

        InitializeComponent();
        PopulateControls();
        WireEvents();
        InitializeToolTips();
        ApplyModeAvailability();

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

    private void PopulateControls()
    {
        styleComboBox.DataSource = Enum.GetValues<OverlayLineStyle>();

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

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void WireEvents()
    {
        colorButton.Click += ColorButtonClick;
        opacityTrackBar.ValueChanged += (_, _) => UpdateOpacityLabel();
        clearButton.Click += (_, _) => ClearRequested = true;
        saveButton.Click += (_, _) =>
        {
            if (OverlayName.Length == 0)
            {
                DialogResult = DialogResult.None;
                System.Media.SystemSounds.Beep.Play();
                nameTextBox.Focus();
            }
        };
    }

    // Smoothing is not meaningful for every mode; rather than reflowing the layout
    // the row is simply greyed out so the dialog keeps a single fixed shape.
    private void ApplyModeAvailability()
    {
        smoothingLabel.Enabled = supportsSmoothing;
        smoothingComboBox.Enabled = supportsSmoothing;
    }

    private void InitializeToolTips()
    {
        toolTip.AutoPopDelay = 12_000;
        toolTip.InitialDelay = 400;
        toolTip.ReshowDelay = 150;

        toolTip.SetToolTip(nameTextBox, "Display name shown in the on-plot legend.");
        toolTip.SetToolTip(colorButton, "Curve color.");
        thicknessInput.ApplyToolTip(toolTip, "Line thickness.");
        toolTip.SetToolTip(styleComboBox, "Line style (solid, dash, dot, dash-dot).");
        toolTip.SetToolTip(
            smoothingComboBox,
            "Fractional-octave smoothing applied to the displayed curve.");
        toolTip.SetToolTip(opacityTrackBar, "Curve opacity.");
        toolTip.SetToolTip(clearButton, "Delete this overlay slot in the current analysis mode.");
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
        colorButton.FlatAppearance.BorderColor = UiPalette.DialogBorder;
    }

    private void UpdateOpacityLabel()
    {
        opacityValueLabel.Text = $"{opacityTrackBar.Value}%";
    }
}
