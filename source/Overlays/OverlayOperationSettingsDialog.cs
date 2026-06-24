using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed class OverlayOperationSettingsDialog : Form
{
    private readonly TextBox nameTextBox = new();
    private readonly ComboBox sourceAComboBox = new();
    private readonly ComboBox sourceBComboBox = new();
    private readonly ComboBox operationComboBox = new();
    private readonly Label blendFrequencyLabel = new();
    private readonly NumericUpDown blendFrequencyInput = new();
    private readonly Label blendWidthLabel = new();
    private readonly ComboBox blendWidthInput = new();
    private readonly CheckBox amplitudeSpaceCheckBox = new();
    private readonly Button colorButton = new();
    private readonly NumericUpDown thicknessInput = new();
    private readonly ComboBox styleComboBox = new();
    private readonly ComboBox smoothingComboBox = new();
    private readonly TrackBar opacityTrackBar = new();
    private readonly Label opacityValueLabel = new();
    private readonly bool supportsSmoothing;
    private readonly bool supportsAmplitudeSpace;
    private Color selectedColor;

    public OverlayOperationSettingsDialog(
        Mode mode,
        string name,
        int sourceSlotA,
        int sourceSlotB,
        OverlayOperation operation,
        double blendFrequencyHz,
        double blendWidthOctaves,
        bool useAmplitudeSpace,
        Color color,
        double strokeThickness,
        OverlayLineStyle lineStyle,
        int opacityPercent,
        int smoothingInverseOctaves,
        IReadOnlyList<OverlaySlotOption> availableSources)
    {
        supportsSmoothing = OverlaySmoothing.SupportsMode(mode);
        supportsAmplitudeSpace = OverlayMath.SupportsAmplitudeSpace(mode);
        selectedColor = color;
        InitializeDialog(availableSources);

        nameTextBox.Text = name;
        SelectSlot(sourceAComboBox, sourceSlotA, 0);
        SelectSlot(sourceBComboBox, sourceSlotB, 1);
        operationComboBox.SelectedItem = operation;
        blendFrequencyInput.Value = (decimal)Math.Clamp(
            blendFrequencyHz,
            1,
            1_000_000);
        amplitudeSpaceCheckBox.Checked = useAmplitudeSpace;
        thicknessInput.Value = (decimal)Math.Clamp(strokeThickness, 0.5, 10);
        styleComboBox.SelectedItem = lineStyle;
        smoothingComboBox.SelectedItem = smoothingInverseOctaves;
        opacityTrackBar.Value = Math.Clamp(opacityPercent, 10, 100);
        SelectBlendWidth(blendWidthOctaves);
        UpdateColorButton();
        UpdateOpacityLabel();
        UpdateBlendControls();
    }

    public string OverlayName => nameTextBox.Text.Trim();
    public int SourceSlotA =>
        ((OverlaySlotOption)sourceAComboBox.SelectedItem!).Slot;
    public int SourceSlotB =>
        ((OverlaySlotOption)sourceBComboBox.SelectedItem!).Slot;
    public OverlayOperation Operation =>
        (OverlayOperation)operationComboBox.SelectedItem!;
    public double BlendFrequencyHz => (double)blendFrequencyInput.Value;
    public double BlendWidthOctaves =>
        ((BlendWidthOption)blendWidthInput.SelectedItem!).Octaves;
    public bool UseAmplitudeSpace => amplitudeSpaceCheckBox.Checked;
    public Color SelectedColor => selectedColor;
    public double StrokeThickness => (double)thicknessInput.Value;
    public OverlayLineStyle LineStyle =>
        (OverlayLineStyle)styleComboBox.SelectedItem!;
    public int OpacityPercent => opacityTrackBar.Value;
    public int SmoothingInverseOctaves => supportsSmoothing
        ? (int)smoothingComboBox.SelectedItem!
        : 0;

    private void InitializeDialog(
        IReadOnlyList<OverlaySlotOption> availableSources)
    {
        SuspendLayout();

        UiStyle.ApplyDarkDialog(
            this,
            new Size(
                440,
                (supportsSmoothing ? 590 : 530) + (supportsAmplitudeSpace ? 20 : 0)),
            title: "Calculated overlay settings");

        AddLabel("Name", 22);
        ConfigureComboOrText(nameTextBox, new Point(20, 42), new Size(400, 24));
        nameTextBox.MaxLength = 80;

        AddLabel("Curve A", 82);
        AddLabel("Curve B", 82, 230);
        ConfigureCombo(sourceAComboBox, new Point(20, 102), new Size(190, 24));
        ConfigureCombo(sourceBComboBox, new Point(230, 102), new Size(190, 24));
        foreach (OverlaySlotOption source in availableSources)
        {
            sourceAComboBox.Items.Add(source);
            sourceBComboBox.Items.Add(source);
        }

        AddLabel("Operation", 142);
        ConfigureCombo(operationComboBox, new Point(20, 162), new Size(400, 24));
        operationComboBox.FormattingEnabled = true;
        foreach (OverlayOperation item in Enum.GetValues<OverlayOperation>())
        {
            operationComboBox.Items.Add(item);
        }
        operationComboBox.Format += (_, args) =>
        {
            if (args.ListItem is OverlayOperation item)
            {
                args.Value = OverlayOperationLabels.GetLabel(item);
            }
        };
        operationComboBox.SelectedIndexChanged += (_, _) => UpdateBlendControls();

        AddLabel("Color", 202);
        colorButton.Location = new Point(20, 222);
        colorButton.Size = new Size(122, 30);
        UiStyle.ApplySurfaceButton(colorButton, UiPalette.DialogSurfaceMuted);
        colorButton.Click += ColorButtonClick;

        AddLabel("Thickness", 202, 162);
        thicknessInput.Location = new Point(162, 222);
        thicknessInput.Size = new Size(90, 24);
        UiStyle.ApplyNumericUpDown(thicknessInput, thicknessInput.Location, thicknessInput.Size);
        thicknessInput.DecimalPlaces = 1;
        thicknessInput.Increment = 0.5m;
        thicknessInput.Minimum = 0.5m;
        thicknessInput.Maximum = 10;

        AddLabel("Style", 202, 272);
        ConfigureCombo(styleComboBox, new Point(272, 222), new Size(148, 24));
        styleComboBox.DataSource = Enum.GetValues<OverlayLineStyle>();

        blendFrequencyLabel.Text = "Blend frequency";
        blendFrequencyLabel.Location = new Point(20, 262);
        blendFrequencyLabel.AutoSize = true;
        blendFrequencyLabel.ForeColor = UiPalette.TextSecondary;
        blendFrequencyLabel.Font = Font;
        Controls.Add(blendFrequencyLabel);
        ConfigureComboOrText(blendFrequencyInput, new Point(20, 282), new Size(190, 24));
        blendFrequencyInput.DecimalPlaces = 1;
        blendFrequencyInput.Minimum = 1;
        blendFrequencyInput.Maximum = 1_000_000;
        blendFrequencyInput.Increment = 1;
        blendFrequencyInput.ThousandsSeparator = true;

        blendWidthLabel.Text = "Transition width";
        blendWidthLabel.Location = new Point(230, 262);
        blendWidthLabel.AutoSize = true;
        blendWidthLabel.ForeColor = UiPalette.TextSecondary;
        blendWidthLabel.Font = Font;
        Controls.Add(blendWidthLabel);
        ConfigureCombo(blendWidthInput, new Point(230, 282), new Size(190, 24));
        blendWidthInput.FormattingEnabled = true;
        foreach (BlendWidthOption option in OverlayBlendWidthOptions.Options)
        {
            blendWidthInput.Items.Add(option);
        }
        blendWidthInput.Format += (_, args) =>
        {
            if (args.ListItem is BlendWidthOption option)
            {
                args.Value = option.Label;
            }
        };

        int amplitudeSpaceY = supportsSmoothing ? 382 : 322;
        int opacityLabelY = supportsSmoothing
            ? (supportsAmplitudeSpace ? 430 : 390)
            : (supportsAmplitudeSpace ? 360 : 330);
        int opacityControlY = opacityLabelY + 20;
        int buttonY = opacityControlY + 115;

        if (supportsSmoothing)
        {
            AddLabel("Smoothing", 322);
            ConfigureCombo(
                smoothingComboBox,
                new Point(20, 342),
                new Size(400, 24));
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

        if (supportsAmplitudeSpace)
        {
            amplitudeSpaceCheckBox.AutoSize = true;
            amplitudeSpaceCheckBox.Location = new Point(20, amplitudeSpaceY);
            amplitudeSpaceCheckBox.Text = "Operate in amplitude space";
            amplitudeSpaceCheckBox.ForeColor = UiPalette.TextBright;
            Controls.Add(amplitudeSpaceCheckBox);
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
        saveButton.Click += SaveButtonClick;

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Controls.AddRange(
        [
            nameTextBox,
            sourceAComboBox,
            sourceBComboBox,
            operationComboBox,
            blendFrequencyLabel,
            blendFrequencyInput,
            blendWidthLabel,
            blendWidthInput,
            colorButton,
            thicknessInput,
            styleComboBox,
            opacityTrackBar,
            opacityValueLabel,
            cancelButton,
            saveButton
        ]);

        OverlayDialogControls.ApplyRuntimeDpiScale(this);
        ResumeLayout(false);
        PerformLayout();
    }

    private void SaveButtonClick(object? sender, EventArgs e)
    {
        bool valid = OverlayName.Length > 0 &&
            sourceAComboBox.SelectedItem != null &&
            sourceBComboBox.SelectedItem != null &&
            SourceSlotA != SourceSlotB;
        if (valid)
        {
            return;
        }

        DialogResult = DialogResult.None;
        System.Media.SystemSounds.Beep.Play();
        if (OverlayName.Length == 0)
        {
            nameTextBox.Focus();
        }
        else
        {
            sourceBComboBox.Focus();
        }
    }

    private void UpdateBlendControls()
    {
        bool isBlend = operationComboBox.SelectedItem is OverlayOperation op &&
            op == OverlayOperation.Blend;
        blendFrequencyLabel.Visible = isBlend;
        blendFrequencyInput.Visible = isBlend;
        blendWidthLabel.Visible = isBlend;
        blendWidthInput.Visible = isBlend;
    }

    private void SelectBlendWidth(double blendWidthOctaves)
    {
        BlendWidthOption? selected = blendWidthInput.Items
            .Cast<BlendWidthOption>()
            .FirstOrDefault(option =>
                Math.Abs(option.Octaves - blendWidthOctaves) < 1e-9);
        blendWidthInput.SelectedItem = selected
            ?? blendWidthInput.Items.Cast<object>().FirstOrDefault();
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

    private void AddLabel(string text, int y, int x = 20)
    {
        Controls.Add(UiStyle.CreateLabel(text, new Point(x, y), UiPalette.TextSecondary, Font));
    }

    private static void ConfigureCombo(
        ComboBox comboBox,
        Point location,
        Size size)
    {
        ConfigureComboOrText(comboBox, location, size);
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    private static void ConfigureComboOrText(
        Control control,
        Point location,
        Size size)
    {
        UiStyle.ApplySurfaceInput(control, location, size);
    }

    private static void SelectSlot(
        ComboBox comboBox,
        int slot,
        int fallbackIndex)
    {
        int index = comboBox.Items
            .Cast<OverlaySlotOption>()
            .Select((item, itemIndex) => (item, itemIndex))
            .Where(pair => pair.item.Slot == slot)
            .Select(pair => pair.itemIndex)
            .DefaultIfEmpty(-1)
            .First();
        comboBox.SelectedIndex = index >= 0
            ? index
            : Math.Min(fallbackIndex, comboBox.Items.Count - 1);
    }
}

internal sealed record OverlaySlotOption(int Slot, string Title)
{
    public override string ToString() => $"{Slot}: {Title}";
}

internal sealed record BlendWidthOption(double Octaves, string Label)
{
    public override string ToString() => Label;
}

internal static class OverlayBlendWidthOptions
{
    public static IReadOnlyList<BlendWidthOption> Options { get; } =
    [
        new BlendWidthOption(1, "1/1"),
        new BlendWidthOption(1.0 / 3.0, "1/3"),
        new BlendWidthOption(1.0 / 6.0, "1/6"),
        new BlendWidthOption(1.0 / 12.0, "1/12"),
        new BlendWidthOption(1.0 / 24.0, "1/24"),
        new BlendWidthOption(1.0 / 48.0, "1/48")
    ];
}

internal static class OverlayOperationLabels
{
    public static string GetLabel(OverlayOperation operation)
    {
        return operation switch
        {
            OverlayOperation.AMinusB => "A - B",
            OverlayOperation.BMinusA => "B - A",
            OverlayOperation.Sum => "A + B",
            OverlayOperation.Average => "(A + B) / 2",
            OverlayOperation.AbsoluteDifference => "|A - B|",
            OverlayOperation.Blend => "Blend A/B",
            _ => "Off"
        };
    }

    public static string GetCompactLabel(OverlayOperation operation)
    {
        return operation switch
        {
            OverlayOperation.AMinusB => "A-B",
            OverlayOperation.BMinusA => "B-A",
            OverlayOperation.Sum => "A+B",
            OverlayOperation.Average => "AVG",
            OverlayOperation.AbsoluteDifference => "|A-B|",
            OverlayOperation.Blend => "XOVR",
            _ => "--"
        };
    }
}
