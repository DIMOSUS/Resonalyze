using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace Resonalyze;

internal sealed class OverlayTargetSettingsDialog : Form
{
    private readonly TextBox nameTextBox = new();
    private readonly DarkComboBox sourceComboBox = new();
    private readonly DarkComboBox presetComboBox = new();
    private readonly DarkNumericUpDown tiltInput = new();
    private readonly DarkNumericUpDown bassGainInput = new();
    private readonly DarkNumericUpDown bassFrequencyInput = new();
    private readonly DarkNumericUpDown bassWidthInput = new();
    private readonly DarkNumericUpDown trebleGainInput = new();
    private readonly DarkNumericUpDown trebleFrequencyInput = new();
    private readonly DarkNumericUpDown trebleWidthInput = new();
    private readonly DarkNumericUpDown presenceGainInput = new();
    private readonly DarkNumericUpDown presenceFrequencyInput = new();
    private readonly DarkNumericUpDown presenceWidthInput = new();
    private readonly DarkNumericUpDown toleranceInput = new();
    private readonly DarkComboBox deviationModeComboBox = new();
    private readonly Button colorButton = new();
    private readonly DarkNumericUpDown thicknessInput = new();
    private readonly DarkComboBox styleComboBox = new();
    private readonly DarkComboBox smoothingComboBox = new();
    private readonly TrackBar opacityTrackBar = new();
    private readonly Label opacityValueLabel = new();
    private readonly PlotView previewPlot = new();
    private readonly ToolTip toolTip = new();
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
        IReadOnlyList<OverlaySlotOption> availableSources)
    {
        selectedColor = color;
        InitializeDialog(availableSources);

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

    private void InitializeDialog(IReadOnlyList<OverlaySlotOption> availableSources)
    {
        SuspendLayout();
        UiStyle.ApplyDarkDialog(this, new Size(500, 800), title: "Target overlay settings");

        AddLabel("Name", 16);
        ConfigureInput(nameTextBox, new Point(20, 36), new Size(460, 24));
        nameTextBox.MaxLength = 80;

        AddLabel("Source", 68);
        ConfigureCombo(sourceComboBox, new Point(20, 88), new Size(460, 24));
        sourceComboBox.Items.Add(new TargetSourceOption(0, "Current measurement"));
        foreach (OverlaySlotOption source in availableSources)
        {
            sourceComboBox.Items.Add(new TargetSourceOption(
                source.Slot,
                $"{source.Slot}: {source.Title}"));
        }

        AddLabel("Preset", 120);
        ConfigureCombo(presetComboBox, new Point(20, 140), new Size(200, 24));
        presetComboBox.FormattingEnabled = true;
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
        presetComboBox.SelectedIndexChanged += PresetChanged;

        AddLabel("Tolerance ±dB", 120, 300, ToleranceTip);
        ConfigureNumeric(toleranceInput, new Point(300, 140), 180, 0, 12, 0.5m, 1);

        AddLabel("Tilt dB/oct", 176, 20, TiltTip);
        ConfigureNumeric(tiltInput, new Point(20, 196), 160, -6, 6, 0.1m, 1);

        AddLabel("Deviation", 176, 250, DeviationTip);
        ConfigureCombo(deviationModeComboBox, new Point(250, 196), new Size(230, 24));
        deviationModeComboBox.FormattingEnabled = true;
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

        // Compact gain / freq / width grid for the three shaping terms.
        AddLabel("Gain dB", 230, 150, "Lift or cut amount, in dB.");
        AddLabel("Freq Hz", 230, 260, "Corner frequency (shelf) or center frequency (presence), in Hz.");
        AddLabel("Width oct", 230, 370, "Transition width / bump width, in octaves.");

        AddLabel("Bass shelf", 252, 20, BassTip);
        ConfigureNumeric(bassGainInput, new Point(150, 250), 100, -12, 18, 0.5m, 1);
        ConfigureNumeric(bassFrequencyInput, new Point(260, 250), 100, 20, 500, 1, 0);
        ConfigureNumeric(bassWidthInput, new Point(370, 250), 100, 0.2m, 4, 0.1m, 1);

        AddLabel("Treble shelf", 284, 20, TrebleTip);
        ConfigureNumeric(trebleGainInput, new Point(150, 282), 100, -18, 12, 0.5m, 1);
        ConfigureNumeric(trebleFrequencyInput, new Point(260, 282), 100, 1_000, 16_000, 100, 0);
        ConfigureNumeric(trebleWidthInput, new Point(370, 282), 100, 0.2m, 4, 0.1m, 1);

        AddLabel("Presence", 316, 20, PresenceTip);
        ConfigureNumeric(presenceGainInput, new Point(150, 314), 100, -12, 12, 0.5m, 1);
        ConfigureNumeric(presenceFrequencyInput, new Point(260, 314), 100, 500, 8_000, 50, 0);
        ConfigureNumeric(presenceWidthInput, new Point(370, 314), 100, 0.2m, 3, 0.1m, 1);

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

        AddLabel("Color", 356);
        colorButton.Location = new Point(20, 376);
        colorButton.Size = new Size(122, 24);
        UiStyle.ApplySurfaceButton(colorButton, UiPalette.DialogSurfaceMuted);
        colorButton.Click += ColorButtonClick;

        AddLabel("Thickness", 356, 162);
        ConfigureNumeric(thicknessInput, new Point(162, 376), 80, 0.5m, 10, 0.5m, 1);

        AddLabel("Style", 356, 262);
        ConfigureCombo(styleComboBox, new Point(262, 376), new Size(218, 24));
        styleComboBox.DataSource = Enum.GetValues<OverlayLineStyle>();

        AddLabel("Smoothing", 414);
        ConfigureCombo(smoothingComboBox, new Point(20, 434), new Size(460, 24));
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

        AddLabel("Opacity", 470);
        opacityTrackBar.Location = new Point(14, 490);
        opacityTrackBar.Size = new Size(380, 40);
        opacityTrackBar.Minimum = 10;
        opacityTrackBar.Maximum = 100;
        opacityTrackBar.TickFrequency = 10;
        opacityTrackBar.ValueChanged += (_, _) => UpdateOpacityLabel();
        opacityValueLabel.AutoSize = true;
        opacityValueLabel.Location = new Point(410, 497);

        AddLabel("Target preview", 540);
        previewPlot.Location = new Point(20, 560);
        previewPlot.Size = new Size(460, 160);
        previewPlot.BackColor = UiPalette.DialogSurface;

        var cancelButton = OverlayDialogControls.CreateDialogButton(
            "Cancel",
            DialogResult.Cancel,
            accent: false);
        cancelButton.Location = new Point(286, 736);
        var saveButton = OverlayDialogControls.CreateDialogButton(
            "Save",
            DialogResult.OK,
            accent: true);
        saveButton.Location = new Point(386, 736);
        saveButton.Click += SaveButtonClick;

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Controls.AddRange(
        [
            nameTextBox,
            sourceComboBox,
            presetComboBox,
            toleranceInput,
            deviationModeComboBox,
            tiltInput,
            bassGainInput, bassFrequencyInput, bassWidthInput,
            trebleGainInput, trebleFrequencyInput, trebleWidthInput,
            presenceGainInput, presenceFrequencyInput, presenceWidthInput,
            colorButton,
            thicknessInput,
            styleComboBox,
            smoothingComboBox,
            opacityTrackBar,
            opacityValueLabel,
            previewPlot,
            cancelButton,
            saveButton
        ]);

        InitializeToolTips();
        FormClosed += (_, _) => toolTip.Dispose();

        OverlayDialogControls.ApplyRuntimeDpiScale(this);
        ResumeLayout(false);
        PerformLayout();
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
    }

    private void SaveButtonClick(object? sender, EventArgs e)
    {
        if (OverlayName.Length > 0 && sourceComboBox.SelectedItem != null)
        {
            return;
        }

        DialogResult = DialogResult.None;
        System.Media.SystemSounds.Beep.Play();
        nameTextBox.Focus();
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

    private void ConfigureNumeric(
        DarkNumericUpDown input,
        Point location,
        int width,
        decimal minimum,
        decimal maximum,
        decimal increment,
        int decimals)
    {
        input.Location = location;
        input.Size = new Size(width, 24);
        UiStyle.ApplyNumericUpDown(input, location, input.Size);
        input.DecimalPlaces = decimals;
        input.Increment = increment;
        input.Minimum = minimum;
        input.Maximum = maximum;
    }

    private static decimal ClampToRange(DarkNumericUpDown input, double value)
    {
        return (decimal)Math.Clamp(
            value,
            (double)input.Minimum,
            (double)input.Maximum);
    }

    private void ConfigureCombo(DarkComboBox comboBox, Point location, Size size)
    {
        UiStyle.ApplySurfaceInput(comboBox, location, size);
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    private static void ConfigureInput(Control control, Point location, Size size)
    {
        UiStyle.ApplySurfaceInput(control, location, size);
    }

    private Label AddLabel(string text, int y, int x = 20, string? tooltip = null)
    {
        Label label = UiStyle.CreateLabel(
            text,
            new Point(x, y),
            UiPalette.TextSecondary,
            Font);
        Controls.Add(label);
        if (!string.IsNullOrEmpty(tooltip))
        {
            toolTip.SetToolTip(label, tooltip);
        }

        return label;
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
