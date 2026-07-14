using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace Resonalyze;

internal sealed partial class OverlayTargetSettingsDialog : Form
{
    // Live preview on the main plot: fired with a snapshot of the candidate settings
    // on every control change, so the target shape, tolerance band, and deviation
    // curve can be tuned against the real measurement. Nothing is committed until
    // Save; the caller restores its stored state on Cancel.
    private readonly Action<OverlayTargetPreview>? previewChanged;
    private readonly bool isolatedTarget;
    private readonly bool initialized;
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
        selectedColor = color;

        InitializeComponent();
        PopulateControls(availableSources);
        WireEvents();
        InitializeToolTips();

        suppressEvents = true;
        nameTextBox.Text = name;
        SelectSource(sourceSlot);
        presetComboBox.SelectedItem = preset;
        ApplySpec(spec);
        toleranceInput.Value = ClampToRange(toleranceInput, toleranceDb);
        deviationModeComboBox.SelectedItem = deviationMode;
        thicknessInput.Value = (decimal)Math.Clamp(strokeThickness, 0.5, 10);
        styleComboBox.SelectedItem = lineStyle;
        smoothingComboBox.SelectedItem = smoothingInverseOctaves;
        opacityTrackBar.Value = Math.Clamp(opacityPercent, 10, 100);
        suppressEvents = false;

        UpdateColorButton();
        UpdateOpacityLabel();
        UpdatePresetTooltip();
        UpdatePreview();
        initialized = true;

        if (isolatedTarget)
        {
            ApplyIsolatedTargetMode();
        }
    }

    // In the EQ Wizard's isolated reuse there is no overlay to name and no
    // tolerance band / deviation curve / opacity to render (the wizard draws its
    // own error fill), so those fields are shown read-only to avoid implying they
    // do anything. The name, colour, thickness and line style still apply.
    private void ApplyIsolatedTargetMode()
    {
        nameTextBox.Enabled = false;
        toleranceInput.Enabled = false;
        deviationModeComboBox.Enabled = false;
        smoothingComboBox.Enabled = false;
        opacityTrackBar.Enabled = false;
    }

    public string OverlayName => nameTextBox.Text.Trim();
    public int SourceSlot => ((TargetSourceOption)sourceComboBox.SelectedItem!).Slot;
    public TargetPreset Preset => (TargetPreset)presetComboBox.SelectedItem!;
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
        (double)presenceWidthInput.Value);

    private void PopulateControls(IReadOnlyList<OverlaySlotOption> availableSources)
    {
        if (isolatedTarget)
        {
            // The EQ Wizard equalizes a separately loaded IR, so the source is
            // fixed: show a single disabled placeholder instead of the slot list.
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

        // Editing the curve shape switches the preset to Custom and redraws.
        foreach (DarkNumericUpDown shape in new[]
        {
            tiltInput,
            bassGainInput, bassFrequencyInput, bassWidthInput,
            trebleGainInput, trebleFrequencyInput, trebleWidthInput,
            presenceGainInput, presenceFrequencyInput, presenceWidthInput
        })
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
    }

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
        var model = new PlotModel
        {
            PlotAreaBorderColor = OxyColors.Gray,
            TextColor = OxyColors.Gainsboro
        };
        OxyColor majorGrid = OxyColor.FromAColor(90, OxyColors.Gray);
        OxyColor minorGrid = OxyColor.FromAColor(40, OxyColors.Gray);
        model.Axes.Add(new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 20,
            Maximum = 20_000,
            TextColor = OxyColors.Gainsboro,
            TicklineColor = OxyColors.Gray,
            MajorGridlineStyle = OxyPlot.LineStyle.Solid,
            MajorGridlineColor = majorGrid,
            MinorGridlineStyle = OxyPlot.LineStyle.Dot,
            MinorGridlineColor = minorGrid
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            TextColor = OxyColors.Gainsboro,
            TicklineColor = OxyColors.Gray,
            Title = "dB",
            MajorGridlineStyle = OxyPlot.LineStyle.Solid,
            MajorGridlineColor = majorGrid,
            MinorGridlineStyle = OxyPlot.LineStyle.Dot,
            MinorGridlineColor = minorGrid
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
        previewPlot.Model = model;
        NotifyPreview();
    }

    // Live preview on the main plot; fired alongside the dialog's own mini preview
    // and by the controls the mini preview does not track (source, tolerance,
    // deviation mode, styling). Suppressed during construction, where control
    // values are still being seeded.
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
        // The isolated target has no name or source to validate.
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
        foreach (DarkNumericUpDown input in NumericInputs())
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
        colorButton.FlatAppearance.BorderColor = UiPalette.DialogBorder;
    }

    private void UpdateOpacityLabel()
    {
        opacityValueLabel.Text = $"{opacityTrackBar.Value}%";
    }

    private static decimal ClampToRange(DarkNumericUpDown input, double value)
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
            "Harman room — gentle ≈-0.8 dB/oct downslope with a bass lift; a common preference for home listening rooms.",
        TargetPreset.RoomGentle =>
            "Gentle room slope — slight downward tilt and a small bass lift.",
        TargetPreset.Warm =>
            "Warm — steeper downslope with a modest bass lift.",
        TargetPreset.Car =>
            "Car — strong bass lift and ≈-1 dB/oct slope for car cabins.",
        TargetPreset.CarMild =>
            "Car (mild) — moderate bass lift for car cabins.",
        TargetPreset.House =>
            "House / bass boost — flat overall with an elevated low end.",
        TargetPreset.XCurve =>
            "X-curve — high-frequency rolloff above ≈2.5 kHz for cinema-sized rooms.",
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
        TargetPreset.HarmanRoom => "Harman room",
        TargetPreset.RoomGentle => "Room (gentle)",
        TargetPreset.Warm => "Warm",
        TargetPreset.Car => "Car",
        TargetPreset.CarMild => "Car (mild)",
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
}

// A snapshot of the candidate settings in the target-overlay dialog, fired on every
// control change for the live preview on the main plot. Mirrors the dialog's output
// properties so the caller can render exactly what Save would commit.
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
