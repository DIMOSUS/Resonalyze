using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze;

internal sealed partial class OverlayOperationSettingsDialog : Form
{
    private readonly bool supportsSmoothing;
    private readonly bool supportsAmplitudeSpace;
    private readonly bool supportsComplexSum;
    // Fired on every change; nothing is committed until Save, and the caller restores on Cancel.
    private readonly Action<OverlayOperationPreview>? previewChanged;
    private readonly bool initialized;
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
        bool tiltEnabled,
        double tiltDbPerOctave,
        double tiltPivotHz,
        double compareDelayMs,
        bool compareInvertPolarity,
        Color color,
        double strokeThickness,
        OverlayLineStyle lineStyle,
        int opacityPercent,
        int smoothingInverseOctaves,
        IReadOnlyList<OverlaySlotOption> availableSources,
        IReadOnlyList<LiveCurveOption> availableLiveCurves,
        Action<OverlayOperationPreview>? previewChanged = null)
    {
        this.previewChanged = previewChanged;
        supportsSmoothing = OverlaySmoothing.SupportsMode(mode);
        supportsAmplitudeSpace = OverlayMath.SupportsAmplitudeSpace(mode);
        supportsComplexSum = mode == Mode.FrequencyResponse;
        selectedColor = color;

        InitializeComponent();
        // Palette value, not a designer literal: the two drifted apart once.
        Ui.UiStyle.ApplySurfaceButton(saveButton, Ui.UiPalette.AccentFill);
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
        tiltCheckBox.Checked = tiltEnabled && SupportsTilt;
        tiltSlopeInput.Value = (decimal)Math.Clamp(
            tiltDbPerOctave,
            (double)tiltSlopeInput.Minimum,
            (double)tiltSlopeInput.Maximum);
        tiltPivotInput.Value = (decimal)Math.Clamp(
            tiltPivotHz,
            (double)tiltPivotInput.Minimum,
            (double)tiltPivotInput.Maximum);
        numericTimeOffset.Value = (decimal)Math.Clamp(
            compareDelayMs,
            (double)numericTimeOffset.Minimum,
            (double)numericTimeOffset.Maximum);
        checkBoxInvPhase.Checked = compareInvertPolarity;
        thicknessInput.Value = (decimal)Math.Clamp(strokeThickness, 0.5, 10);
        styleComboBox.SelectedItem = lineStyle;
        smoothingComboBox.SelectedItem = supportsAmplitudeSpace
            ? smoothingInverseOctaves
            : Dsp.SpectrumSmoothing.EquivalentInverseOctaves(smoothingInverseOctaves);
        opacityTrackBar.Value = Math.Clamp(opacityPercent, 10, 100);
        SelectBlendWidth(blendWidthOctaves);
        UpdateColorButton();
        UpdateOpacityLabel();
        UpdateOperationControls();
        UpdateTiltControls();
        initialized = true;
    }

    // Amplitude-space math and tilt need a magnitude mode and a decibel result (not coherence).
    private bool SupportsAmplitudeSpaceMath =>
        supportsAmplitudeSpace &&
        ResultSemantics.IsDecibels &&
        operationComboBox.SelectedItem is OverlayOperation selected &&
        selected is not (
            OverlayOperation.ComplexSum or
            OverlayOperation.ComplexSumLoss or
            OverlayOperation.CurveA);

    private bool SupportsTilt => supportsAmplitudeSpace && ResultSemantics.IsDecibels;

    private OverlayCurveSemantics ResultSemantics
    {
        get
        {
            OverlayOperandOption? a = OperandOf(sourceAComboBox);
            OverlayOperandOption? b = OperandOf(sourceBComboBox);
            OverlayOperation? operation =
                operationComboBox.SelectedItem as OverlayOperation?;
            return operation is { } value && a != null
                ? OverlayCurveSemantics.ForOperation(
                    value,
                    a.Semantics,
                    value == OverlayOperation.CurveA || b == null
                        ? OverlayCurveSemantics.None
                        : b.Semantics).Curve
                : OverlayCurveSemantics.None;
        }
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
    public bool UseAmplitudeSpace =>
        SupportsAmplitudeSpaceMath && amplitudeSpaceCheckBox.Checked;
    public bool TiltEnabled => SupportsTilt && tiltCheckBox.Checked;
    public double TiltDbPerOctave => (double)tiltSlopeInput.Value;
    public double TiltPivotHz => (double)tiltPivotInput.Value;
    public double CompareDelayMs => (double)numericTimeOffset.Value;
    public bool CompareInvertPolarity => checkBoxInvPhase.Checked;
    public Color SelectedColor => selectedColor;
    public double StrokeThickness => (double)thicknessInput.Value;
    public OverlayLineStyle LineStyle =>
        (OverlayLineStyle)styleComboBox.SelectedItem!;
    public int OpacityPercent => opacityTrackBar.Value;
    public int SmoothingInverseOctaves =>
        supportsSmoothing && smoothingComboBox.SelectedItem is int value
            ? value
            : 0;

    private void PopulateControls(
        IReadOnlyList<OverlaySlotOption> availableSources,
        IReadOnlyList<LiveCurveOption> availableLiveCurves)
    {
        foreach (LiveCurveOption live in availableLiveCurves)
        {
            var operand = new OverlayOperandOption(
                0,
                live.Key,
                $"Live: {live.Label}",
                live.Semantics);
            sourceAComboBox.Items.Add(operand);
            sourceBComboBox.Items.Add(operand);
        }

        foreach (OverlaySlotOption source in availableSources)
        {
            var operand = new OverlayOperandOption(
                source.Slot,
                null,
                $"Slot {source.Slot}: {source.Title}",
                source.Semantics);
            sourceAComboBox.Items.Add(operand);
            sourceBComboBox.Items.Add(operand);
        }

        foreach (OverlayOperation item in Enum.GetValues<OverlayOperation>())
        {
            if (item is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss &&
                !supportsComplexSum)
            {
                continue;
            }

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
            // Psychoacoustic is magnitude-only; its floor would bias a signed phase/GD result upward.
            if (Dsp.SpectrumSmoothing.IsPsychoacoustic(value) &&
                !supportsAmplitudeSpace)
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
        operationComboBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateOperationControls();
            UpdateTiltControls();
            NotifyPreview();
        };
        nameTextBox.TextChanged += (_, _) => NotifyPreview();
        // Operands decide whether the result is decibels, so dB-only controls follow them.
        sourceAComboBox.SelectedIndexChanged += (_, _) => OperandChanged();
        sourceBComboBox.SelectedIndexChanged += (_, _) => OperandChanged();
        blendFrequencyInput.ValueChanged += (_, _) => NotifyPreview();
        blendWidthInput.SelectedIndexChanged += (_, _) => NotifyPreview();
        amplitudeSpaceCheckBox.CheckedChanged += (_, _) => NotifyPreview();
        tiltCheckBox.CheckedChanged += (_, _) =>
        {
            UpdateTiltControls();
            NotifyPreview();
        };
        tiltSlopeInput.ValueChanged += (_, _) => NotifyPreview();
        tiltPivotInput.ValueChanged += (_, _) => NotifyPreview();
        numericTimeOffset.ValueChanged += (_, _) => NotifyPreview();
        checkBoxInvPhase.CheckedChanged += (_, _) => NotifyPreview();
        thicknessInput.ValueChanged += (_, _) => NotifyPreview();
        styleComboBox.SelectedIndexChanged += (_, _) => NotifyPreview();
        smoothingComboBox.SelectedIndexChanged += (_, _) => NotifyPreview();
        colorButton.Click += ColorButtonClick;
        opacityTrackBar.ValueChanged += (_, _) =>
        {
            UpdateOpacityLabel();
            NotifyPreview();
        };
        saveButton.Click += SaveButtonClick;
    }

    private void OperandChanged()
    {
        UpdateOperationControls();
        UpdateTiltControls();
        NotifyPreview();
    }

    private void NotifyPreview()
    {
        if (!initialized || previewChanged == null)
        {
            return;
        }

        previewChanged(new OverlayOperationPreview(
            OverlayName,
            SourceSlotA,
            SourceCurveKeyA,
            SourceSlotB,
            SourceCurveKeyB,
            Operation,
            BlendFrequencyHz,
            BlendWidthOctaves,
            UseAmplitudeSpace,
            TiltEnabled,
            TiltDbPerOctave,
            TiltPivotHz,
            CompareDelayMs,
            CompareInvertPolarity,
            SelectedColor,
            StrokeThickness,
            LineStyle,
            OpacityPercent,
            SmoothingInverseOctaves));
    }

    private void ApplyModeAvailability()
    {
        UiStyle.SetTextEnabledLook(smoothingLabel, supportsSmoothing);
        smoothingComboBox.Enabled = supportsSmoothing;
        UiStyle.SetTextEnabledLook(amplitudeSpaceCheckBox, supportsAmplitudeSpace, interactive: true);
    }

    private void UpdateTiltControls()
    {
        UiStyle.SetTextEnabledLook(tiltCheckBox, SupportsTilt, interactive: true);
        bool enabled = SupportsTilt && tiltCheckBox.Checked;
        UiStyle.SetTextEnabledLook(tiltPivotLabel, enabled);
        tiltPivotInput.Enabled = enabled;
        UiStyle.SetTextEnabledLook(tiltSlopeLabel, enabled);
        tiltSlopeInput.Enabled = enabled;
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
            "Calculation applied between curve A and curve B — or \"A only\", which " +
            "draws curve A alone, so this slot's smoothing, offset and tilt can be " +
            "applied to a single curve. The complex sum instead " +
            "adds the Main and Compare transfer responses as complex spectra " +
            "(delay, polarity, and phase included) — the physically summed output of " +
            "two sources; it needs a Compare measurement with a transfer IR.");
        blendFrequencyInput.ApplyToolTip(
            toolTip,
            "Crossover frequency for the Blend operation (A below, B above).");
        toolTip.SetToolTip(
            blendWidthInput,
            "Transition width of the blend crossover, in octaves.");
        toolTip.SetToolTip(
            amplitudeSpaceCheckBox,
            "Convert both curves to linear amplitude before the operation and back to dB afterward (for dB-based views).");
        numericTimeOffset.ApplyToolTip(
            toolTip,
            "Extra delay applied to the Compare response before the complex sum, in " +
            "milliseconds — the delay you would dial into that DSP channel.");
        toolTip.SetToolTip(
            checkBoxInvPhase,
            "Invert the polarity of the Compare response before the complex sum — " +
            "the phase/polarity switch of that DSP channel.");
        toolTip.SetToolTip(
            tiltCheckBox,
            "Add a straight slope to the result, in dB per octave. Typically undoes the " +
            "slope of the excitation itself — pink noise falls 3 dB per octave through " +
            "a constant-bandwidth analyzer.");
        tiltPivotInput.ApplyToolTip(
            toolTip,
            "Frequency the tilt hinges on: the curve is unchanged here and rotates " +
            "about it.");
        tiltSlopeInput.ApplyToolTip(
            toolTip,
            "Decibels added per octave above the pivot (and subtracted per octave " +
            "below it). Either sign.");
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
        CommitNumericEditors();

        OverlayOperandOption? a = OperandOf(sourceAComboBox);
        OverlayOperandOption? b = OperandOf(sourceBComboBox);
        // Incompatible operands (SPL vs relative, coherence vs dB) are refused rather than saved into a slot that never draws.
        bool operandsValid = Operation switch
        {
            OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss => true,
            OverlayOperation.CurveA => a != null,
            _ => a != null && b != null && !SameOperand(a, b) &&
                OverlayCurveSemantics.AreCompatible(a.Semantics, b.Semantics)
        };
        bool valid = OverlayName.Length > 0 && operandsValid;
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        DarkNumericUpDown? input = keyData == Keys.Enter
            ? GetFocusedNumericInput()
            : null;
        if (input != null)
        {
            input.CommitText();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private DarkNumericUpDown? GetFocusedNumericInput() =>
        NumericInputs().FirstOrDefault(control => control.ContainsFocus);

    private IEnumerable<DarkNumericUpDown> NumericInputs()
    {
        yield return blendFrequencyInput;
        yield return tiltPivotInput;
        yield return tiltSlopeInput;
        yield return numericTimeOffset;
        yield return thicknessInput;
    }

    private void CommitNumericEditors()
    {
        foreach (DarkNumericUpDown input in NumericInputs())
        {
            input.CommitText();
        }
    }

    // Inapplicable controls are greyed out, not hidden, so the layout stays fixed.
    private void UpdateOperationControls()
    {
        OverlayOperation? op = operationComboBox.SelectedItem as OverlayOperation?;
        bool isBlend = op == OverlayOperation.Blend;
        bool isComplexSum = op is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss;
        bool usesB = !isComplexSum && op != OverlayOperation.CurveA;
        UiStyle.SetTextEnabledLook(blendFrequencyLabel, isBlend);
        blendFrequencyInput.Enabled = isBlend;
        UiStyle.SetTextEnabledLook(blendWidthLabel, isBlend);
        blendWidthInput.Enabled = isBlend;
        UiStyle.SetTextEnabledLook(curveALabel, !isComplexSum);
        sourceAComboBox.Enabled = !isComplexSum;
        UiStyle.SetTextEnabledLook(curveBLabel, usesB);
        sourceBComboBox.Enabled = usesB;
        UiStyle.SetTextEnabledLook(
            amplitudeSpaceCheckBox,
            SupportsAmplitudeSpaceMath,
            interactive: true);
        UiStyle.SetTextEnabledLook(labelTimeOffset, isComplexSum);
        numericTimeOffset.Enabled = isComplexSum;
        UiStyle.SetTextEnabledLook(checkBoxInvPhase, isComplexSum, interactive: true);
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
            NotifyPreview();
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

internal sealed record OverlaySlotOption(
    int Slot,
    string Title,
    OverlayCurveSemantics Semantics = default)
{
    public override string ToString() => $"{Slot}: {Title}";
}

internal sealed record LiveCurveOption(
    string Key,
    string Label,
    OverlayCurveSemantics Semantics = default);

// Mirrors the dialog's output so the caller renders exactly what Save would commit.
internal sealed record OverlayOperationPreview(
    string Name,
    int SourceSlotA,
    string? SourceCurveKeyA,
    int SourceSlotB,
    string? SourceCurveKeyB,
    OverlayOperation Operation,
    double BlendFrequencyHz,
    double BlendWidthOctaves,
    bool UseAmplitudeSpace,
    bool TiltEnabled,
    double TiltDbPerOctave,
    double TiltPivotHz,
    double CompareDelayMs,
    bool CompareInvertPolarity,
    Color Color,
    double StrokeThickness,
    OverlayLineStyle LineStyle,
    int OpacityPercent,
    int SmoothingInverseOctaves);

internal sealed record OverlayOperandOption(
    int Slot,
    string? CurveKey,
    string Label,
    OverlayCurveSemantics Semantics = default)
{
    public bool IsLiveCurve => CurveKey != null;

    public override string ToString() => Label;
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
            OverlayOperation.CurveA => "A only",
            OverlayOperation.AMinusB => "A - B",
            OverlayOperation.BMinusA => "B - A",
            OverlayOperation.Sum => "A + B",
            OverlayOperation.Average => "(A + B) / 2",
            OverlayOperation.AbsoluteDifference => "|A - B|",
            OverlayOperation.Blend => "Blend A/B",
            OverlayOperation.ComplexSum => "Main ⊕ Compare (complex sum)",
            OverlayOperation.ComplexSumLoss => "Sum loss (complex − magnitude)",
            _ => "Off"
        };
    }

}
