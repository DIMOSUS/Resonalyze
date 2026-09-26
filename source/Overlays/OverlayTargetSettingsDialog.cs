using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace Resonalyze;

internal sealed partial class OverlayTargetSettingsDialog : Form
{
    // Fired on every change; nothing is committed until Save, and the caller restores on Cancel.
    private readonly Action<OverlayTargetPreview>? previewChanged;
    private readonly bool isolatedTarget;
    private readonly bool includePsychoacousticSmoothing;
    private readonly bool initialized;
    // An imported shape is an extra preset-list entry; the preset behind it rides through untouched.
    private readonly ImportedTargetCurve? importedCurve;
    private readonly TargetPreset incomingPreset;
    private readonly Mode mode;

    // The preview is rebuilt on every edit; without this a zoom would not survive the next change.
    private readonly PlotViewportMemory previewViewports;
    private Color selectedColor;
    private bool suppressEvents;

    public OverlayTargetSettingsDialog(
        Mode mode,
        string name,
        int sourceSlot,
        TargetPreset preset,
        TargetCurveSpec spec,
        double toleranceDb,
        TargetDeviationMode deviationMode,
        Color color,
        double strokeThickness,
        OverlayLineStyle lineStyle,
        int opacityPercent,
        int smoothingInverseOctaves,
        IReadOnlyList<OverlaySlotOption> availableSources,
        Action<OverlayTargetPreview>? previewChanged = null,
        bool isolatedTarget = false)
    {
        this.previewChanged = previewChanged;
        this.isolatedTarget = isolatedTarget;
        includePsychoacousticSmoothing = OverlayMath.SupportsAmplitudeSpace(mode);
        selectedColor = color;
        importedCurve = spec.Imported;
        incomingPreset = preset;
        this.mode = mode;

        InitializeComponent();
        PlotInteraction.Enable(previewPlot);
        previewViewports = new PlotViewportMemory(previewPlot);
        // Palette value, not a designer literal: the two drifted apart once.
        Ui.UiStyle.ApplySurfaceButton(saveButton, Ui.UiPalette.AccentFill, Ui.UiPalette.TextOnAccent);
        PopulateControls(availableSources);
        WireEvents();
        InitializeToolTips();

        suppressEvents = true;
        nameTextBox.Text = name;
        SelectSource(sourceSlot);
        presetComboBox.SelectedItem = importedCurve != null
            ? presetComboBox.Items[0]
            : OverlayTargets.ResolvePreset(preset, spec);
        ApplySpec(spec);
        toleranceInput.Value = ClampToRange(toleranceInput, toleranceDb);
        deviationModeComboBox.SelectedItem = deviationMode;
        thicknessInput.Value = (decimal)Math.Clamp(strokeThickness, 0.5, 10);
        styleComboBox.SelectedItem = lineStyle;
        smoothingComboBox.SelectedItem = includePsychoacousticSmoothing
            ? smoothingInverseOctaves
            : Dsp.SpectrumSmoothing.EquivalentInverseOctaves(smoothingInverseOctaves);
        opacityTrackBar.Value = Math.Clamp(opacityPercent, 10, 100);
        suppressEvents = false;

        UpdateColorButton();
        UpdateOpacityLabel();
        UpdatePresetTooltip();
        UpdateShapeInputs();
        UpdatePreview();
        initialized = true;

        if (isolatedTarget)
        {
            ApplyIsolatedTargetMode();
        }
    }

    // EQ Wizard reuse: no overlay name, tolerance, deviation or opacity, so those are read-only.
    private void ApplyIsolatedTargetMode()
    {
        // Muted by hand: Windows paints a disabled TextBox grey regardless of ForeColor (2.5:1).
        nameTextBox.ReadOnly = true;
        nameTextBox.TabStop = false;
        nameTextBox.BackColor = Ui.UiPalette.ButtonDisabledBackground;
        nameTextBox.ForeColor = Ui.UiPalette.TextDisabled;
        toleranceInput.Enabled = false;
        deviationModeComboBox.Enabled = false;
        smoothingComboBox.Enabled = false;
        opacityTrackBar.Enabled = false;
    }

    public string OverlayName => nameTextBox.Text.Trim();
    public int SourceSlot => ((TargetSourceOption)sourceComboBox.SelectedItem!).Slot;
    // With the imported entry selected, the opening preset still names the parametric shape behind it.
    public TargetPreset Preset => presetComboBox.SelectedItem is TargetPreset preset
        ? preset
        : incomingPreset;
    public double ToleranceDb => (double)toleranceInput.Value;
    public TargetDeviationMode DeviationMode =>
        (TargetDeviationMode)deviationModeComboBox.SelectedItem!;
    public Color SelectedColor => selectedColor;
    public double StrokeThickness => (double)thicknessInput.Value;
    public OverlayLineStyle LineStyle => (OverlayLineStyle)styleComboBox.SelectedItem!;
    public int OpacityPercent => opacityTrackBar.Value;
    public int SmoothingInverseOctaves =>
        smoothingComboBox.SelectedItem is int value ? value : 0;

    public TargetCurveSpec Spec => new(
        (double)tiltInput.Value,
        (double)bassGainInput.Value,
        (double)bassFrequencyInput.Value,
        (double)bassWidthInput.Value,
        (double)trebleGainInput.Value,
        (double)trebleFrequencyInput.Value,
        (double)trebleWidthInput.Value,
        (double)presenceGainInput.Value,
        (double)presenceFrequencyInput.Value,
        (double)presenceWidthInput.Value)
    {
        Imported = SelectedImportedCurve
    };

    private ImportedTargetCurve? SelectedImportedCurve =>
        presetComboBox.SelectedItem is ImportedShapeOption option ? option.Curve : null;

    private void PopulateControls(IReadOnlyList<OverlaySlotOption> availableSources)
    {
        if (isolatedTarget)
        {
            sourceComboBox.Items.Add(new TargetSourceOption(0, "Loaded IR"));
            sourceComboBox.Enabled = false;
        }
        else
        {
            sourceComboBox.Items.Add(new TargetSourceOption(0, "Current measurement"));
            foreach (OverlaySlotOption source in availableSources)
            {
                sourceComboBox.Items.Add(new TargetSourceOption(
                    source.Slot,
                    $"{source.Slot}: {source.Title}"));
            }
        }

        // Stays in the list so the user can return to their house curve after trying a preset.
        if (importedCurve != null)
        {
            presetComboBox.Items.Add(new ImportedShapeOption(importedCurve));
        }

        foreach (TargetPreset value in Enum.GetValues<TargetPreset>())
        {
            presetComboBox.Items.Add(value);
        }
        presetComboBox.Format += (_, args) =>
        {
            if (args.ListItem is TargetPreset value)
            {
                args.Value = GetPresetLabel(value);
            }
            else if (args.ListItem is ImportedShapeOption option)
            {
                args.Value = option.Label;
            }
        };

        foreach (TargetDeviationMode value in Enum.GetValues<TargetDeviationMode>())
        {
            deviationModeComboBox.Items.Add(value);
        }
        deviationModeComboBox.Format += (_, args) =>
        {
            if (args.ListItem is TargetDeviationMode value)
            {
                args.Value = GetDeviationModeLabel(value);
            }
        };

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
        presetComboBox.SelectedIndexChanged += PresetChanged;

        foreach (ThemedNumericUpDown shape in ShapeInputs)
        {
            shape.ValueChanged += ParameterChanged;
        }

        nameTextBox.TextChanged += (_, _) => NotifyPreview();
        toleranceInput.ValueChanged += (_, _) => NotifyPreview();
        deviationModeComboBox.SelectedIndexChanged += (_, _) => NotifyPreview();
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
        sourceComboBox.SelectedIndexChanged += (_, _) => NotifyPreview();
    }

    private const string TiltTip =
        "Overall spectral tilt in dB per octave, pivoting at 1 kHz. Negative tilts the response downward toward high frequencies.";
    private const string BassTip =
        "Low-frequency shelf: bass lift (or cut) below the corner frequency, set by gain, corner frequency and transition width.";
    private const string TrebleTip =
        "High-frequency shelf: treble lift (or cut) above the corner frequency, set by gain, corner frequency and transition width.";
    private const string PresenceTip =
        "Presence bump (positive) or dip (negative) centered on its frequency, set by gain, center frequency and width.";
    private const string ToleranceTip =
        "Shaded ±dB tolerance band drawn around the target. Zero hides the band.";
    private const string DeviationTip =
        "Deviation curve: 'Deviation' shows measurement − target; 'EQ correction' shows target − measurement (the gain to dial into an equalizer); 'None' hides it.";

    private void InitializeToolTips()
    {
        toolTip.AutoPopDelay = 12_000;
        toolTip.InitialDelay = 400;
        toolTip.ReshowDelay = 150;

        toolTip.SetToolTip(
            sourceComboBox,
            "Curve to compare against the target: a captured slot, or the current measurement (live/last Live Spectrum trace or the main Frequency Response curve).");
        toolTip.SetToolTip(toleranceLabel, ToleranceTip);
        toolTip.SetToolTip(tiltLabel, TiltTip);
        toolTip.SetToolTip(deviationLabel, DeviationTip);
        toolTip.SetToolTip(gainHeaderLabel, "Lift or cut amount, in dB.");
        toolTip.SetToolTip(freqHeaderLabel, "Corner frequency (shelf) or center frequency (presence), in Hz.");
        toolTip.SetToolTip(widthHeaderLabel, "Transition width / bump width, in octaves.");
        toolTip.SetToolTip(bassLabel, BassTip);
        toolTip.SetToolTip(trebleLabel, TrebleTip);
        toolTip.SetToolTip(presenceLabel, PresenceTip);

        tiltInput.ApplyToolTip(toolTip, TiltTip);
        bassGainInput.ApplyToolTip(toolTip, BassTip);
        bassFrequencyInput.ApplyToolTip(toolTip, BassTip);
        bassWidthInput.ApplyToolTip(toolTip, BassTip);
        trebleGainInput.ApplyToolTip(toolTip, TrebleTip);
        trebleFrequencyInput.ApplyToolTip(toolTip, TrebleTip);
        trebleWidthInput.ApplyToolTip(toolTip, TrebleTip);
        presenceGainInput.ApplyToolTip(toolTip, PresenceTip);
        presenceFrequencyInput.ApplyToolTip(toolTip, PresenceTip);
        presenceWidthInput.ApplyToolTip(toolTip, PresenceTip);
        toleranceInput.ApplyToolTip(toolTip, ToleranceTip);
        thicknessInput.ApplyToolTip(toolTip, "Line thickness.");
        toolTip.SetToolTip(deviationModeComboBox, DeviationTip);
        toolTip.SetToolTip(
            smoothingComboBox,
            "Fractional-octave smoothing applied to the source before the deviation is computed.");
    }

    private void UpdatePresetTooltip()
    {
        if (presetComboBox.SelectedItem is TargetPreset preset)
        {
            toolTip.SetToolTip(presetComboBox, GetPresetDescription(preset));
        }
        else if (presetComboBox.SelectedItem is ImportedShapeOption option)
        {
            toolTip.SetToolTip(
                presetComboBox,
                $"{option.Curve.Describe()}\r\nFile levels at the Target Level, flat " +
                "outside its range. A preset returns to a parametric shape.");
        }
    }

    // Disabled while imported, but values kept for returning to a preset.
    private void UpdateShapeInputs()
    {
        bool parametric = SelectedImportedCurve == null;
        foreach (ThemedNumericUpDown input in ShapeInputs)
        {
            input.Enabled = parametric;
        }
    }

    private ThemedNumericUpDown[] ShapeInputs =>
    [
        tiltInput,
        bassGainInput, bassFrequencyInput, bassWidthInput,
        trebleGainInput, trebleFrequencyInput, trebleWidthInput,
        presenceGainInput, presenceFrequencyInput, presenceWidthInput
    ];

    private void ApplySpec(TargetCurveSpec spec)
    {
        tiltInput.Value = ClampToRange(tiltInput, spec.TiltDbPerOctave);
        bassGainInput.Value = ClampToRange(bassGainInput, spec.BassShelfGainDb);
        bassFrequencyInput.Value = ClampToRange(bassFrequencyInput, spec.BassShelfFrequencyHz);
        bassWidthInput.Value = ClampToRange(bassWidthInput, spec.BassShelfWidthOctaves);
        trebleGainInput.Value = ClampToRange(trebleGainInput, spec.TrebleShelfGainDb);
        trebleFrequencyInput.Value = ClampToRange(trebleFrequencyInput, spec.TrebleShelfFrequencyHz);
        trebleWidthInput.Value = ClampToRange(trebleWidthInput, spec.TrebleShelfWidthOctaves);
        presenceGainInput.Value = ClampToRange(presenceGainInput, spec.PresenceGainDb);
        presenceFrequencyInput.Value = ClampToRange(presenceFrequencyInput, spec.PresenceFrequencyHz);
        presenceWidthInput.Value = ClampToRange(presenceWidthInput, spec.PresenceWidthOctaves);
    }

    private void PresetChanged(object? sender, EventArgs e)
    {
        UpdatePresetTooltip();
        UpdateShapeInputs();
        if (suppressEvents ||
            presetComboBox.SelectedItem is not TargetPreset preset ||
            preset == TargetPreset.Custom)
        {
            UpdatePreview();
            return;
        }

        suppressEvents = true;
        ApplySpec(TargetCurveSpec.FromPreset(preset));
        suppressEvents = false;
        UpdatePreview();
    }

    private void ParameterChanged(object? sender, EventArgs e)
    {
        if (suppressEvents)
        {
            return;
        }

        suppressEvents = true;
        presetComboBox.SelectedItem = TargetPreset.Custom;
        suppressEvents = false;
        UpdatePresetTooltip();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        TargetCurveSpec spec = Spec;
        PlotModel model = PlotModelStyle.CreatePreviewModel();
        PlotModelStyle.AddAxis(model, new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 20,
            Maximum = 20_000,
            MajorGridlineStyle = OxyPlot.LineStyle.Solid,
            MinorGridlineStyle = OxyPlot.LineStyle.Dot
        });
        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "dB",
            MajorGridlineStyle = OxyPlot.LineStyle.Solid,
            MinorGridlineStyle = OxyPlot.LineStyle.Dot
        });

        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(selectedColor.R, selectedColor.G, selectedColor.B),
            StrokeThickness = 2,
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        const int steps = 240;
        for (int i = 0; i < steps; i++)
        {
            double frequency = 20.0 * Math.Pow(1_000.0, i / (steps - 1.0));
            series.Points.Add(new DataPoint(frequency, spec.Evaluate(frequency)));
        }
        model.Series.Add(series);
        previewViewports.Show(model, mode);
        NotifyPreview();
    }

    private void NotifyPreview()
    {
        if (!initialized || previewChanged == null)
        {
            return;
        }

        previewChanged(new OverlayTargetPreview(
            OverlayName,
            SourceSlot,
            Spec,
            ToleranceDb,
            DeviationMode,
            SelectedColor,
            StrokeThickness,
            LineStyle,
            OpacityPercent,
            SmoothingInverseOctaves));
    }

    private void SaveButtonClick(object? sender, EventArgs e)
    {
        CommitNumericEditors();

        if (ValidateSaveRequest(focusOnError: true))
        {
            return;
        }

        DialogResult = DialogResult.None;
    }

    private bool ValidateSaveRequest(bool focusOnError)
    {
        if (isolatedTarget ||
            (OverlayName.Length > 0 && sourceComboBox.SelectedItem != null))
        {
            return true;
        }

        System.Media.SystemSounds.Beep.Play();
        if (focusOnError)
        {
            nameTextBox.Focus();
        }

        return false;
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
        yield return toleranceInput;
        yield return tiltInput;
        yield return bassGainInput;
        yield return bassFrequencyInput;
        yield return bassWidthInput;
        yield return trebleGainInput;
        yield return trebleFrequencyInput;
        yield return trebleWidthInput;
        yield return presenceGainInput;
        yield return presenceFrequencyInput;
        yield return presenceWidthInput;
        yield return thicknessInput;
    }

    private void CommitNumericEditors()
    {
        foreach (ThemedNumericUpDown input in NumericInputs())
        {
            input.CommitText();
        }
    }

    private void SelectSource(int slot)
    {
        int index = sourceComboBox.Items
            .Cast<TargetSourceOption>()
            .Select((item, itemIndex) => (item, itemIndex))
            .Where(pair => pair.item.Slot == slot)
            .Select(pair => pair.itemIndex)
            .DefaultIfEmpty(0)
            .First();
        sourceComboBox.SelectedIndex = Math.Min(index, sourceComboBox.Items.Count - 1);
    }

    private void ColorButtonClick(object? sender, EventArgs e)
    {
        using var dialog = new ColorPickerDialog(selectedColor);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            selectedColor = dialog.SelectedColor;
            UpdateColorButton();
            UpdatePreview();
        }
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

    private static decimal ClampToRange(ThemedNumericUpDown input, double value)
    {
        return (decimal)Math.Clamp(
            value,
            (double)input.Minimum,
            (double)input.Maximum);
    }

    private static string GetPresetDescription(TargetPreset preset) => preset switch
    {
        TargetPreset.Flat =>
            "Flat reference — no tilt or shelving (studio / anechoic target).",
        TargetPreset.HarmanRoom =>
            "Room (Harman-style) — gentle ≈-0.8 dB/oct downslope with a bass lift, in the spirit of the Harman work; a common preference for home listening rooms.",
        TargetPreset.RoomGentle =>
            "Gentle room slope — slight downward tilt and a small bass lift.",
        TargetPreset.Warm =>
            "Warm — steeper downslope with a modest bass lift.",
        TargetPreset.Car =>
            "Car — in-car target: ≈+9 dB bass shelf, flat 630 Hz…5 kHz, and a gentle rolloff reaching ≈3 dB by 20 kHz.",
        TargetPreset.CarMild =>
            "Car (mild) — the same in-car shape with a moderate ≈+6 dB bass lift.",
        TargetPreset.CarBass =>
            "Car (bass) — the same in-car shape with a ≈+12 dB bass lift, for driving with road noise or for bass-forward taste.",
        TargetPreset.House =>
            "House / bass boost — flat overall with an elevated low end.",
        TargetPreset.XCurve =>
            "X-curve (cinema) — ISO 2969 / SMPTE ST 202: flat to 2 kHz, then ≈-3 dB/oct, for cinema-sized rooms.",
        TargetPreset.Smiley =>
            "Smiley — boosted bass and treble (consumer 'loudness' shape).",
        TargetPreset.BbcDip =>
            "BBC dip — a small presence cut around 2.8 kHz for a relaxed midrange.",
        _ =>
            "Custom — your own tilt, shelves and presence."
    };

    private static string GetDeviationModeLabel(TargetDeviationMode mode) => mode switch
    {
        TargetDeviationMode.Correction => "EQ correction (target − meas)",
        TargetDeviationMode.None => "None",
        _ => "Deviation (meas − target)"
    };

    private static string GetPresetLabel(TargetPreset preset) => preset switch
    {
        TargetPreset.Flat => "Flat",
        TargetPreset.HarmanRoom => "Room (Harman-style)",
        TargetPreset.RoomGentle => "Room (gentle)",
        TargetPreset.Warm => "Warm",
        TargetPreset.Car => "Car",
        TargetPreset.CarMild => "Car (mild)",
        TargetPreset.CarBass => "Car (bass)",
        TargetPreset.House => "House / bass boost",
        TargetPreset.XCurve => "X-curve (cinema)",
        TargetPreset.Smiley => "Smiley",
        TargetPreset.BbcDip => "BBC dip",
        _ => "Custom"
    };

    private sealed record TargetSourceOption(int Slot, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record ImportedShapeOption(ImportedTargetCurve Curve)
    {
        public string Label => MenuText.Trim($"Imported: {Curve.Name}");

        public override string ToString() => Label;
    }
}

// Mirrors the dialog's output so the caller renders exactly what Save would commit.
internal sealed record OverlayTargetPreview(
    string Name,
    int SourceSlot,
    TargetCurveSpec Spec,
    double ToleranceDb,
    TargetDeviationMode DeviationMode,
    Color Color,
    double StrokeThickness,
    OverlayLineStyle LineStyle,
    int OpacityPercent,
    int SmoothingInverseOctaves);
