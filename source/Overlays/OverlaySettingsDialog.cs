namespace Resonalyze;

internal sealed partial class OverlaySettingsDialog : Form
{
    private readonly bool supportsSmoothing;
    // Only for dB magnitude curves (the caller withholds it for coherence).
    private readonly bool includePsychoacousticSmoothing;
    // Nothing is committed until Save; the caller restores on Cancel.
    private readonly Action<OverlayCapturedPreview>? previewChanged;
    private readonly bool initialized;
    private Color selectedColor;

    public OverlaySettingsDialog(
        Mode mode,
        string name,
        Color color,
        double strokeThickness,
        OverlayLineStyle lineStyle,
        int opacityPercent,
        int smoothingInverseOctaves,
        Action<OverlayCapturedPreview>? previewChanged = null,
        bool allowPsychoacousticSmoothing = true)
    {
        this.previewChanged = previewChanged;
        supportsSmoothing = OverlaySmoothing.SupportsMode(mode);
        includePsychoacousticSmoothing = allowPsychoacousticSmoothing &&
            OverlayMath.SupportsAmplitudeSpace(mode);
        selectedColor = color;

        InitializeComponent();
        // Palette value, not a designer literal: the two drifted apart once.
        Ui.UiStyle.ApplySurfaceButton(saveButton, Ui.UiPalette.AccentFill, Ui.UiPalette.TextOnAccent);
        PopulateControls();
        WireEvents();
        InitializeToolTips();
        ApplyModeAvailability();

        nameTextBox.Text = name;
        thicknessInput.Value = (decimal)Math.Clamp(strokeThickness, 0.5, 10);
        styleComboBox.SelectedItem = lineStyle;
        smoothingComboBox.SelectedItem = includePsychoacousticSmoothing
            ? smoothingInverseOctaves
            : Dsp.SpectrumSmoothing.EquivalentInverseOctaves(smoothingInverseOctaves);
        opacityTrackBar.Value = Math.Clamp(opacityPercent, 10, 100);
        UpdateColorButton();
        UpdateOpacityLabel();
        initialized = true;
    }

    public string OverlayName => nameTextBox.Text.Trim();
    public Color SelectedColor => selectedColor;
    public double StrokeThickness => (double)thicknessInput.Value;
    public OverlayLineStyle LineStyle =>
        (OverlayLineStyle)styleComboBox.SelectedItem!;
    public int OpacityPercent => opacityTrackBar.Value;
    public int SmoothingInverseOctaves =>
        supportsSmoothing && smoothingComboBox.SelectedItem is int value
            ? value
            : 0;
    public bool ClearRequested { get; private set; }

    private void PopulateControls()
    {
        styleComboBox.DataSource = Enum.GetValues<OverlayLineStyle>();

        foreach (int value in OverlaySmoothing.SupportedInverseOctaves)
        {
            if (Dsp.SpectrumSmoothing.IsPsychoacoustic(value) &&
                !includePsychoacousticSmoothing)
            {
                continue;
            }

            smoothingComboBox.Items.Add(value);
        }
        smoothingComboBox.Format += (_, args) =>
        {
            if (args.ListItem is int value)
            {
                args.Value = OverlaySmoothing.GetLabel(value);
            }
        };

        CancelButton = cancelButton;
    }

    private void WireEvents()
    {
        nameTextBox.TextChanged += (_, _) => NotifyPreview();
        thicknessInput.ValueChanged += (_, _) => NotifyPreview();
        styleComboBox.SelectedIndexChanged += (_, _) => NotifyPreview();
        smoothingComboBox.SelectedIndexChanged += (_, _) => NotifyPreview();
        colorButton.Click += ColorButtonClick;
        opacityTrackBar.ValueChanged += (_, _) =>
        {
            UpdateOpacityLabel();
            NotifyPreview();
        };
        clearButton.Click += (_, _) => ClearRequested = true;
        saveButton.Click += (_, _) =>
        {
            CommitNumericEditors();

            if (OverlayName.Length == 0)
            {
                DialogResult = DialogResult.None;
                System.Media.SystemSounds.Beep.Play();
                nameTextBox.Focus();
            }
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        ThemedNumericUpDown? input = keyData == Keys.Enter
            ? GetFocusedNumericInput()
            : null;
        if (input != null)
        {
            input.CommitText();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private ThemedNumericUpDown? GetFocusedNumericInput() =>
        NumericInputs().FirstOrDefault(control => control.ContainsFocus);

    private IEnumerable<ThemedNumericUpDown> NumericInputs()
    {
        yield return thicknessInput;
    }

    private void CommitNumericEditors()
    {
        foreach (ThemedNumericUpDown input in NumericInputs())
        {
            input.CommitText();
        }
    }

    private void ApplyModeAvailability()
    {
        Ui.UiStyle.SetTextEnabledLook(smoothingLabel, supportsSmoothing);
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
            NotifyPreview();
        }
    }

    private void NotifyPreview()
    {
        if (!initialized || previewChanged == null)
        {
            return;
        }

        previewChanged(new OverlayCapturedPreview(
            OverlayName,
            SelectedColor,
            StrokeThickness,
            LineStyle,
            OpacityPercent,
            SmoothingInverseOctaves));
    }

    private void UpdateColorButton()
    {
        colorButton.BackColor = selectedColor;
        colorButton.Text =
            $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
        colorButton.FlatAppearance.BorderColor = UiPalette.Border;
    }

    private void UpdateOpacityLabel()
    {
        opacityValueLabel.Text = $"{opacityTrackBar.Value}%";
    }
}

// Mirrors the dialog's output so the caller renders exactly what Save would commit.
internal sealed record OverlayCapturedPreview(
    string Name,
    Color Color,
    double StrokeThickness,
    OverlayLineStyle LineStyle,
    int OpacityPercent,
    int SmoothingInverseOctaves);
