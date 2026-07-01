using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed partial class OverlayOperationSettingsDialog : Form
{
    private readonly bool supportsSmoothing;
    private readonly bool supportsAmplitudeSpace;
    private Color selectedColor;

    public OverlayOperationSettingsDialog(
        Mode mode,
        string name,
        int sourceSlotA,
        string? sourceCurveKeyA,
        int sourceSlotB,
        string? sourceCurveKeyB,
        OverlayOperation operation,
        double blendFrequencyHz,
        double blendWidthOctaves,
        bool useAmplitudeSpace,
        Color color,
        double strokeThickness,
        OverlayLineStyle lineStyle,
        int opacityPercent,
        int smoothingInverseOctaves,
        IReadOnlyList<OverlaySlotOption> availableSources,
        IReadOnlyList<LiveCurveOption> availableLiveCurves)
    {
        supportsSmoothing = OverlaySmoothing.SupportsMode(mode);
        supportsAmplitudeSpace = OverlayMath.SupportsAmplitudeSpace(mode);
        selectedColor = color;

        InitializeComponent();
        PopulateControls(availableSources, availableLiveCurves);
        WireEvents();
        InitializeToolTips();
        ApplyModeAvailability();

        nameTextBox.Text = name;
        SelectOperand(sourceAComboBox, sourceSlotA, sourceCurveKeyA, 0);
        SelectOperand(sourceBComboBox, sourceSlotB, sourceCurveKeyB, 1);
        operationComboBox.SelectedItem = operation;
        blendFrequencyInput.Value = (decimal)Math.Clamp(
            blendFrequencyHz,
            1,
            1_000_000);
        amplitudeSpaceCheckBox.Checked = useAmplitudeSpace && supportsAmplitudeSpace;
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
    public int SourceSlotA => SlotOf(sourceAComboBox);
    public int SourceSlotB => SlotOf(sourceBComboBox);
    public string? SourceCurveKeyA => OperandOf(sourceAComboBox)?.CurveKey;
    public string? SourceCurveKeyB => OperandOf(sourceBComboBox)?.CurveKey;
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

    private void PopulateControls(
        IReadOnlyList<OverlaySlotOption> availableSources,
        IReadOnlyList<LiveCurveOption> availableLiveCurves)
    {
        // Live curves (the ones drawn on the plot right now) first, then captured slots.
        // A live-curve operand re-reads its curve on every rebuild; a slot operand is a
        // one-off snapshot.
        foreach (LiveCurveOption live in availableLiveCurves)
        {
            var operand = new OverlayOperandOption(0, live.Key, $"Live: {live.Label}");
            sourceAComboBox.Items.Add(operand);
            sourceBComboBox.Items.Add(operand);
        }

        foreach (OverlaySlotOption source in availableSources)
        {
            var operand = new OverlayOperandOption(
                source.Slot,
                null,
                $"Slot {source.Slot}: {source.Title}");
            sourceAComboBox.Items.Add(operand);
            sourceBComboBox.Items.Add(operand);
        }

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

        styleComboBox.DataSource = Enum.GetValues<OverlayLineStyle>();

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
        operationComboBox.SelectedIndexChanged += (_, _) => UpdateBlendControls();
        colorButton.Click += ColorButtonClick;
        opacityTrackBar.ValueChanged += (_, _) => UpdateOpacityLabel();
        saveButton.Click += SaveButtonClick;
    }

    // Smoothing and amplitude-space are only meaningful for some modes; instead of
    // reflowing the dialog those controls are greyed out, keeping a fixed layout.
    private void ApplyModeAvailability()
    {
        smoothingLabel.Enabled = supportsSmoothing;
        smoothingComboBox.Enabled = supportsSmoothing;
        amplitudeSpaceCheckBox.Enabled = supportsAmplitudeSpace;
    }

    private void InitializeToolTips()
    {
        toolTip.AutoPopDelay = 12_000;
        toolTip.InitialDelay = 400;
        toolTip.ReshowDelay = 150;

        toolTip.SetToolTip(nameTextBox, "Display name shown in the on-plot legend.");
        toolTip.SetToolTip(
            sourceAComboBox,
            "Curve A — a live plot curve (tracks the analysis) or a captured overlay slot.");
        toolTip.SetToolTip(
            sourceBComboBox,
            "Curve B — a live plot curve (tracks the analysis) or a captured overlay slot.");
        toolTip.SetToolTip(
            operationComboBox,
            "Calculation applied between curve A and curve B.");
        blendFrequencyInput.ApplyToolTip(
            toolTip,
            "Crossover frequency for the Blend operation (A below, B above).");
        toolTip.SetToolTip(
            blendWidthInput,
            "Transition width of the blend crossover, in octaves.");
        toolTip.SetToolTip(
            amplitudeSpaceCheckBox,
            "Convert both curves to linear amplitude before the operation and back to dB afterward (for dB-based views).");
        toolTip.SetToolTip(colorButton, "Curve color.");
        thicknessInput.ApplyToolTip(toolTip, "Line thickness.");
        toolTip.SetToolTip(styleComboBox, "Line style (solid, dash, dot, dash-dot).");
        toolTip.SetToolTip(
            smoothingComboBox,
            "Fractional-octave smoothing applied after the operation.");
        toolTip.SetToolTip(opacityTrackBar, "Curve opacity.");
    }

    private void SaveButtonClick(object? sender, EventArgs e)
    {
        OverlayOperandOption? a = OperandOf(sourceAComboBox);
        OverlayOperandOption? b = OperandOf(sourceBComboBox);
        bool valid = OverlayName.Length > 0 &&
            a != null &&
            b != null &&
            !SameOperand(a, b);
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

    // Blend frequency / width only apply to the Blend operation; they are greyed
    // out for the others rather than hidden so nothing shifts around.
    private void UpdateBlendControls()
    {
        bool isBlend = operationComboBox.SelectedItem is OverlayOperation op &&
            op == OverlayOperation.Blend;
        blendFrequencyLabel.Enabled = isBlend;
        blendFrequencyInput.Enabled = isBlend;
        blendWidthLabel.Enabled = isBlend;
        blendWidthInput.Enabled = isBlend;
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

    private static void SelectOperand(
        DarkComboBox comboBox,
        int slot,
        string? curveKey,
        int fallbackIndex)
    {
        int index = comboBox.Items
            .Cast<OverlayOperandOption>()
            .Select((item, itemIndex) => (item, itemIndex))
            .Where(pair => curveKey != null
                ? pair.item.CurveKey == curveKey
                : !pair.item.IsLiveCurve && pair.item.Slot == slot)
            .Select(pair => pair.itemIndex)
            .DefaultIfEmpty(-1)
            .First();
        comboBox.SelectedIndex = index >= 0
            ? index
            : Math.Min(fallbackIndex, comboBox.Items.Count - 1);
    }

    private static OverlayOperandOption? OperandOf(DarkComboBox comboBox) =>
        comboBox.SelectedItem as OverlayOperandOption;

    private static int SlotOf(DarkComboBox comboBox) =>
        OperandOf(comboBox) is { IsLiveCurve: false } operand ? operand.Slot : 0;

    private static bool SameOperand(OverlayOperandOption a, OverlayOperandOption b) =>
        a.IsLiveCurve || b.IsLiveCurve
            ? a.CurveKey == b.CurveKey
            : a.Slot == b.Slot;
}

internal sealed record OverlaySlotOption(int Slot, string Title)
{
    public override string ToString() => $"{Slot}: {Title}";
}

// A live analysis curve (identified by its CurveTag Key) selectable as an operation
// operand directly from the plot, without capturing it into a slot first.
internal sealed record LiveCurveOption(string Key, string Label);

// A unified operation operand: a captured slot (CurveKey null) or a live curve.
internal sealed record OverlayOperandOption(int Slot, string? CurveKey, string Label)
{
    public bool IsLiveCurve => CurveKey != null;

    public override string ToString() => Label;
}

internal sealed record TargetOverlayOption(int Slot, string Title, int SourceSlot)
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
