using System.ComponentModel;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

// A single curve to draw on the EQ Wizard plot, decoupled from the overlay
// machinery so the panel never reaches back into plot/overlay internals.
internal sealed record EqWizardCurve(
    string Title,
    OxyColor Color,
    double StrokeThickness,
    LineStyle LineStyle,
    IReadOnlyList<DataPoint> Points);

// Everything the EQ Wizard plot draws for the selected target: the target shape,
// its captured source curve, and the source with the current EQ applied. Source
// and SourcePlusEq are null when the target has no captured source curve. When
// present, Target is sampled on the same frequencies as the source curves.
internal sealed record EqWizardRenderSet(
    EqWizardCurve Target,
    EqWizardCurve? Source,
    EqWizardCurve? SourcePlusEq,
    double TargetOffset);

public partial class EqWizardPanel : UserControl
{
    private const int MaxPeqSlotCount = 32;
    private const int MinAutoTuneBandLimit = 4;
    private const int PeqColumnCount = 4;
    private const int PeqRowCount = 8;
    private const string WizardSeriesTag = "eq-wizard:curve";
    private const string WizardTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00} dB";

    private const string NoTargetHint =
        "No target overlays found.\n" +
        "Create a Target overlay in a Frequency Response measurement,\n" +
        "then return here to fine-tune it.";

    private readonly List<PeqSlotControl> peqSlots = new();
    private TableLayoutPanel peqSlotTable = null!;
    private PlotLabelsPanelController plotLabels = null!;
    private PlotWatermarkAnnotation hintAnnotation = null!;
    private LineAnnotation fromMarker = null!;
    private LineAnnotation toMarker = null!;
    private int selectedBandIndex = -1;
    private bool suppressTargetOffsetEvents;
    private bool suppressRedraw;

    // Colour of the highlighted single-band contribution curve (semi-transparent).
    private static readonly OxyColor BandCurveColor = OxyColor.FromArgb(150, 255, 170, 40);

    public EqWizardPanel()
    {
        InitializeComponent();
        InitializePlotWizard();
        InitializePeqSlotTable();
        InitializeBandsComboBox();
        InitializeBandsLimitComboBox();
        InitializeSmoothComboBox();
        darkComboBoxSource.SelectedIndexChanged += (_, _) => DrawSelectedCurves();
        NumericTargetOffset.ValueChanged += NumericTargetOffsetValueChanged;
        NumericGain.ValueChanged += (_, _) => DrawSelectedCurves();
        checkBoxBypass.CheckedChanged += (_, _) => DrawSelectedCurves();
        buttonAutoTune.Click += (_, _) => AutoTune();
        buttonOverlaySettings.Click += (_, _) => OpenOverlaySettings();
        buttonImport.Click += (_, _) => ImportPeq();
        buttonExport.Click += (_, _) => ExportPeq();
        numericFromHz.ValueChanged += (_, _) => OnFrequencyWindowChanged();
        numericToHz.ValueChanged += (_, _) => OnFrequencyWindowChanged();
        // Clicking away from the bands clears the highlighted single-band curve.
        Click += (_, _) => DeselectBand();
        panelPEQ.Click += (_, _) => DeselectBand();
        plotWizard.Click += (_, _) => DeselectBand();
    }

    internal IReadOnlyList<PeqSlotControl> PeqSlots => peqSlots;

    // Supplies the curves to draw for a chosen target overlay slot, EQ and source
    // smoothing (1/N octave, 0 = off). Wired by the host form; null until then.
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<int, EqualizationCurve, int, EqWizardRenderSet?>? RenderProvider { get; set; }

    private int SourceSmoothingInverseOctaves =>
        comboBoxSmooth.SelectedItem is int value ? value : 0;

    // Pushes a vertical offset for the selected target overlay back to the overlay
    // collection (slot, offset). Wired by the host form; null until then.
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<int, double>? TargetOffsetSetter { get; set; }

    // Reports the current tuning result (or null when nothing is being tuned) to the
    // results panel. Wired by the host form; null until then.
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<EqTuneStats?>? ResultsChanged { get; set; }

    // Opens the settings of the target overlay being tuned (by slot). Wired by the
    // host form; null until then.
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<int>? OverlaySettingsRequested { get; set; }

    internal TargetOverlayOption? SelectedTargetOverlay =>
        darkComboBoxSource.SelectedItem as TargetOverlayOption;

    public void SetPeqSlotCount(int slotCount)
    {
        int clampedSlotCount = Math.Clamp(slotCount, 0, MaxPeqSlotCount);
        // Rebuilding the slots invalidates any single-band selection.
        selectedBandIndex = -1;
        peqSlotTable.SuspendLayout();
        try
        {
            peqSlotTable.Controls.Clear();
            peqSlots.Clear();

            for (int index = 0; index < clampedSlotCount; index++)
            {
                var slot = new PeqSlotControl
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(2),
                    SlotNumber = index + 1
                };
                int column = index / PeqRowCount;
                int row = index % PeqRowCount;
                slot.FrequencyInput.ValueChanged += PeqBandValueChanged;
                slot.QInput.ValueChanged += PeqBandValueChanged;
                slot.GainInput.ValueChanged += PeqBandValueChanged;
                slot.Activated += (sender, _) => SelectBand((PeqSlotControl)sender!);
                peqSlots.Add(slot);
                peqSlotTable.Controls.Add(slot, column, row);
            }
        }
        finally
        {
            peqSlotTable.ResumeLayout();
        }

        // The band count itself changes the EQ curve, so redraw.
        DrawSelectedCurves();
    }

    private void PeqBandValueChanged(object? sender, EventArgs e) => DrawSelectedCurves();

    // Selects a band so its individual contribution is highlighted on the plot.
    // Selecting another band replaces the previous highlight.
    private void SelectBand(PeqSlotControl slot)
    {
        int index = peqSlots.IndexOf(slot);
        if (index < 0 || index == selectedBandIndex)
        {
            return;
        }

        selectedBandIndex = index;
        for (int i = 0; i < peqSlots.Count; i++)
        {
            peqSlots[i].SetSelected(i == index);
        }

        DrawSelectedCurves();
    }

    // Clears the single-band highlight and removes its curve from the plot.
    private void DeselectBand()
    {
        if (selectedBandIndex < 0)
        {
            return;
        }

        selectedBandIndex = -1;
        foreach (PeqSlotControl slot in peqSlots)
        {
            slot.SetSelected(false);
        }

        DrawSelectedCurves();
    }

    // Redraws the wizard for the current selection; used after the target overlay's
    // settings change externally.
    internal void RefreshCurves() => DrawSelectedCurves();

    private void OpenOverlaySettings()
    {
        TargetOverlayOption? selected = SelectedTargetOverlay;
        if (selected == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        OverlaySettingsRequested?.Invoke(selected.Slot);
    }

    internal void SetTargetOverlayOptions(IReadOnlyList<TargetOverlayOption> options)
    {
        hintAnnotation.Text = options.Count == 0 ? NoTargetHint : string.Empty;
        plotWizard.InvalidatePlot(false);

        int? previousSlot = SelectedTargetOverlay?.Slot;
        darkComboBoxSource.Items.Clear();
        foreach (TargetOverlayOption option in options)
        {
            darkComboBoxSource.Items.Add(option);
        }

        darkComboBoxSource.Enabled = options.Count > 0;
        if (options.Count == 0)
        {
            darkComboBoxSource.SelectedIndex = -1;
            return;
        }

        int selectedIndex = 0;
        if (previousSlot.HasValue)
        {
            for (int index = 0; index < options.Count; index++)
            {
                if (options[index].Slot == previousSlot.Value)
                {
                    selectedIndex = index;
                    break;
                }
            }
        }

        darkComboBoxSource.SelectedIndex = selectedIndex;
    }

    internal bool SelectTargetOverlaySlot(int slot)
    {
        for (int index = 0; index < darkComboBoxSource.Items.Count; index++)
        {
            if (darkComboBoxSource.Items[index] is TargetOverlayOption option &&
                option.Slot == slot)
            {
                darkComboBoxSource.SelectedIndex = index;
                return true;
            }
        }

        return false;
    }

    private void InitializePlotWizard()
    {
        // Mirror the axis style used by the Frequency Response and Live Spectrum
        // plots: a logarithmic 20 Hz - 20 kHz frequency axis and a dB axis.
        PlotModel model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "EQ Wizard",
            TextColor = OxyColor.FromAColor(10, OxyColors.White),
            FontSize = 80,
            FontWeight = FontWeights.Bold
        });

        // Guidance shown below the watermark only while no target overlay exists.
        hintAnnotation = new PlotWatermarkAnnotation
        {
            Text = string.Empty,
            VerticalPosition = 0.66,
            TextColor = OxyColor.FromRgb(230, 184, 0),
            FontSize = 15,
            FontWeight = FontWeights.Bold
        };
        model.Annotations.Add(hintAnnotation);

        // Translucent green guides marking the Auto Tune frequency range.
        fromMarker = CreateRangeMarker();
        toMarker = CreateRangeMarker();
        model.Annotations.Add(fromMarker);
        model.Annotations.Add(toMarker);
        fromMarker.X = (double)numericFromHz.Value;
        toMarker.X = (double)numericToHz.Value;

        plotWizard.Model = model;
        PlotInteraction.EnableDoubleClickAxisReset(plotWizard);

        // Reuse the main plot's bottom legend so the curve list looks identical.
        plotLabels = new PlotLabelsPanelController(plotWizard, () => Mode.EqWizard);
    }

    private static LineAnnotation CreateRangeMarker() => new()
    {
        Type = LineAnnotationType.Vertical,
        Color = OxyColor.FromArgb(120, 90, 210, 120),
        StrokeThickness = 2,
        LineStyle = LineStyle.Solid,
        Layer = AnnotationLayer.AboveSeries
    };

    // Moves the range guides to the current From/To frequencies.
    private void UpdateAutoTuneRangeMarkers()
    {
        fromMarker.X = (double)numericFromHz.Value;
        toMarker.X = (double)numericToHz.Value;
        plotWizard.InvalidatePlot(false);
    }

    // The frequency window drives both the guides and the windowed Tuning results,
    // so moving it updates the markers and recomputes the stats.
    private void OnFrequencyWindowChanged()
    {
        UpdateAutoTuneRangeMarkers();
        DrawSelectedCurves();
    }

    // Redraws the plot for the currently selected target overlay: its source
    // measurement (if any) and the target shape, plus the shared bottom legend.
    private void DrawSelectedCurves()
    {
        // Auto Tune applies many control changes at once; it redraws once at the end
        // instead of on every intermediate change.
        if (suppressRedraw)
        {
            return;
        }

        PlotModel? model = plotWizard.Model;
        if (model == null)
        {
            return;
        }

        RemoveWizardSeries(model);

        // Bypass shows the curves as measured: a neutral EQ makes Source + EQ equal
        // the source. The Source + EQ curve and the error fill still draw, just
        // without our equalization applied.
        bool bypass = checkBoxBypass.Checked;
        EqualizationCurve eq = bypass
            ? new EqualizationCurve(Array.Empty<PeqBand>())
            : BuildEqualizationCurve();
        TargetOverlayOption? selected = SelectedTargetOverlay;
        EqWizardRenderSet? render = selected != null
            ? RenderProvider?.Invoke(selected.Slot, eq, SourceSmoothingInverseOctaves)
            : null;
        NumericTargetOffset.Enabled = render != null;
        buttonOverlaySettings.Enabled = render != null;
        // The EQ only has an effect when there is a source curve to apply it to.
        NumericGain.Enabled = !bypass && render?.SourcePlusEq != null;
        bool showEqCurves = render?.SourcePlusEq != null;
        ResultsChanged?.Invoke(BuildStats(render, eq));
        if (render != null)
        {
            SetTargetOffsetValue(render.TargetOffset);

            // Fill the gap between Source + EQ and the target first, so the curves
            // draw on top of the shaded band.
            if (showEqCurves)
            {
                AddFillBetween(model, render.SourcePlusEq!, render.Target);
            }

            if (render.Source != null)
            {
                AddWizardSeries(model, render.Source);
            }

            AddWizardSeries(model, render.Target);
            if (showEqCurves)
            {
                AddWizardSeries(model, render.SourcePlusEq!);
            }

            AddSelectedBandCurve(model, render.Target);
        }

        plotLabels.Refresh();
        model.InvalidatePlot(true);
    }

    // Reads the PEQ slots and the preamp control into a logical EQ curve.
    private EqualizationCurve BuildEqualizationCurve()
    {
        IEnumerable<PeqBand> bands = peqSlots.Select(slot => new PeqBand(
            (double)slot.FrequencyInput.Value,
            (double)slot.QInput.Value,
            (double)slot.GainInput.Value));
        return new EqualizationCurve(bands, (double)NumericGain.Value);
    }

    // Computes the tuning read-out for the results panel: how well Source + EQ
    // matches the target, plus the EQ's own boost/cut extents and headroom.
    private EqTuneStats? BuildStats(EqWizardRenderSet? render, EqualizationCurve eq)
    {
        if (render?.SourcePlusEq == null)
        {
            return null;
        }

        IReadOnlyList<DataPoint> corrected = render.SourcePlusEq.Points;
        IReadOnlyList<DataPoint> target = render.Target.Points;
        int count = Math.Min(corrected.Count, target.Count);

        // Fit quality is measured only inside the user-defined frequency window.
        (double minHz, double maxHz) = GetFrequencyWindow();

        double sumSquares = 0;
        double maxError = 0;
        int valid = 0;
        for (int i = 0; i < count; i++)
        {
            double frequency = corrected[i].X;
            if (frequency < minHz || frequency > maxHz)
            {
                continue;
            }

            double error = target[i].Y - corrected[i].Y;
            if (!double.IsFinite(error))
            {
                continue;
            }

            sumSquares += error * error;
            maxError = Math.Max(maxError, Math.Abs(error));
            valid++;
        }

        double rms = valid > 0 ? Math.Sqrt(sumSquares / valid) : 0;
        int filtersUsed = peqSlots.Count(
            slot => Math.Abs((double)slot.GainInput.Value) >= 0.05);

        double peakBoost = double.NegativeInfinity;
        double peakCut = double.PositiveInfinity;
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 256))
        {
            double gain = eq.MagnitudeDbAt(frequency);
            peakBoost = Math.Max(peakBoost, gain);
            peakCut = Math.Min(peakCut, gain);
        }

        // Headroom is the margin to 0 dB: positive when the EQ never exceeds unity,
        // negative (a required attenuation) when it boosts.
        double headroom = -peakBoost;
        return new EqTuneStats(rms, maxError, filtersUsed, peakBoost, peakCut, headroom);
    }

    // Fits the PEQ bands and preamp automatically so Source + EQ approaches Target,
    // then writes the result into the controls.
    private void AutoTune()
    {
        TargetOverlayOption? selected = SelectedTargetOverlay;
        // Pass a neutral EQ so the render gives the raw source and a target aligned
        // to the source's frequencies.
        EqWizardRenderSet? render = selected != null
            ? RenderProvider?.Invoke(
                selected.Slot,
                new EqualizationCurve(Array.Empty<PeqBand>()),
                SourceSmoothingInverseOctaves)
            : null;
        if (render?.Source == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        var source = render.Source.Points
            .Select(point => new SignalPoint(point.X, point.Y))
            .ToList();
        var target = render.Target.Points
            .Select(point => new SignalPoint(point.X, point.Y))
            .ToList();

        EqualizationCurve tuned = EqAutoTuner.Tune(source, target, CreateAutoTuneOptions());
        // A freshly tuned EQ should be audible/visible, so leave bypass off.
        checkBoxBypass.Checked = false;
        ApplyEqualizationCurve(tuned);
    }

    // Mirrors the control limits so the fit only proposes values the controls accept.
    private EqAutoTuner.Options CreateAutoTuneOptions()
    {
        int bandLimit = comboBoxBandsLimit.SelectedItem is int limit
            ? limit
            : MaxPeqSlotCount;

        (double minHz, double maxHz) = GetFrequencyWindow();

        var options = new EqAutoTuner.Options
        {
            MaxBands = Math.Clamp(bandLimit, 1, MaxPeqSlotCount),
            MinFrequencyHz = minHz,
            MaxFrequencyHz = maxHz,
            PreampMinDb = (double)NumericGain.Minimum,
            PreampMaxDb = (double)NumericGain.Maximum
        };

        if (peqSlots.Count > 0)
        {
            PeqSlotControl slot = peqSlots[0];
            options = options with
            {
                BandGainMinDb = (double)slot.GainInput.Minimum,
                BandGainMaxDb = (double)slot.GainInput.Maximum,
                QMin = (double)slot.QInput.Minimum,
                QMax = (double)slot.QInput.Maximum
            };
        }

        return options;
    }

    private void ApplyEqualizationCurve(EqualizationCurve curve)
    {
        suppressRedraw = true;
        try
        {
            int bandCount = Math.Clamp(curve.Bands.Count, 1, MaxPeqSlotCount);
            darkComboBoxBands.SelectedIndex = bandCount - 1;

            for (int i = 0; i < peqSlots.Count; i++)
            {
                PeqSlotControl slot = peqSlots[i];
                if (i < curve.Bands.Count)
                {
                    PeqBand band = curve.Bands[i];
                    slot.FrequencyInput.Value = Clamp(slot.FrequencyInput, band.FrequencyHz);
                    slot.QInput.Value = Clamp(slot.QInput, band.Q);
                    slot.GainInput.Value = Clamp(slot.GainInput, band.GainDb);
                }
                else
                {
                    // No band for this slot: leave it transparent.
                    slot.GainInput.Value = Clamp(slot.GainInput, 0);
                }
            }

            NumericGain.Value = Clamp(NumericGain, curve.PreampDb);
        }
        finally
        {
            suppressRedraw = false;
        }

        DrawSelectedCurves();
    }

    // The user-defined Auto Tune frequency window (From/To). Bounds are ordered and
    // kept at least 1 Hz apart so callers never get an inverted or empty range.
    private (double MinHz, double MaxHz) GetFrequencyWindow()
    {
        double fromHz = (double)numericFromHz.Value;
        double toHz = (double)numericToHz.Value;
        double minHz = Math.Min(fromHz, toHz);
        double maxHz = Math.Max(fromHz, toHz);
        if (maxHz - minHz < 1)
        {
            maxHz = minHz + 1;
        }

        return (minHz, maxHz);
    }

    // Rounds to the control's precision and clamps to its range.
    private static decimal Clamp(DarkNumericUpDown control, double value)
    {
        decimal rounded = (decimal)Math.Round(value, control.DecimalPlaces);
        return Math.Clamp(rounded, control.Minimum, control.Maximum);
    }

    // Writes the current PEQ (preamp + bands) to a simple text file.
    private void ExportPeq()
    {
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "txt",
            FileName = "eq.txt",
            Filter = "PEQ text (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Export PEQ"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            System.IO.File.WriteAllText(
                dialog.FileName,
                PeqTextFile.Format(BuildEqualizationCurve()));
        }
        catch (Exception exception)
        {
            ShowFileError("PEQ could not be exported.", exception);
        }
    }

    // Loads a PEQ from a text file and applies it. Parsing tolerates broken or
    // hand-edited files, so only file access can fail here.
    private void ImportPeq()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "PEQ text (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Import PEQ"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        EqualizationCurve curve;
        try
        {
            curve = PeqTextFile.Parse(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception exception)
        {
            ShowFileError("PEQ could not be imported.", exception);
            return;
        }

        // Show the imported EQ rather than leaving it hidden behind bypass.
        checkBoxBypass.Checked = false;
        ApplyEqualizationCurve(curve);
    }

    private void ShowFileError(string message, Exception exception)
    {
        MessageBox.Show(
            FindForm(),
            $"{message}{Environment.NewLine}{exception.Message}",
            "EQ Wizard",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void NumericTargetOffsetValueChanged(object? sender, EventArgs e)
    {
        if (suppressTargetOffsetEvents)
        {
            return;
        }

        TargetOverlayOption? selected = SelectedTargetOverlay;
        if (selected == null)
        {
            return;
        }

        TargetOffsetSetter?.Invoke(selected.Slot, (double)NumericTargetOffset.Value);
        DrawSelectedCurves();
    }

    // Reflects the overlay's stored offset in the control without re-triggering the
    // change handler (which would push the value straight back).
    private void SetTargetOffsetValue(double offset)
    {
        decimal clamped = Math.Clamp(
            (decimal)offset,
            NumericTargetOffset.Minimum,
            NumericTargetOffset.Maximum);
        if (NumericTargetOffset.Value == clamped)
        {
            return;
        }

        suppressTargetOffsetEvents = true;
        try
        {
            NumericTargetOffset.Value = clamped;
        }
        finally
        {
            suppressTargetOffsetEvents = false;
        }
    }

    private static void RemoveWizardSeries(PlotModel model)
    {
        for (int index = model.Series.Count - 1; index >= 0; index--)
        {
            if (Equals(model.Series[index].Tag, WizardSeriesTag))
            {
                model.Series.RemoveAt(index);
            }
        }
    }

    private static void AddWizardSeries(PlotModel model, EqWizardCurve curve)
    {
        var series = new LineSeries
        {
            Color = curve.Color,
            StrokeThickness = curve.StrokeThickness,
            LineStyle = curve.LineStyle,
            Title = curve.Title,
            Tag = WizardSeriesTag,
            TrackerFormatString = WizardTrackerFormat
        };
        series.Points.AddRange(curve.Points);
        model.Series.Add(series);
    }

    // Draws the highlighted band's individual contribution relative to the target
    // curve (target with only that one band applied), so its shape, width and gain
    // stand out against where the response should land.
    private void AddSelectedBandCurve(PlotModel model, EqWizardCurve? baseline)
    {
        if (selectedBandIndex < 0 ||
            selectedBandIndex >= peqSlots.Count ||
            baseline is not { Points.Count: >= 2 })
        {
            return;
        }

        PeqSlotControl slot = peqSlots[selectedBandIndex];
        var band = new PeqBand(
            (double)slot.FrequencyInput.Value,
            (double)slot.QInput.Value,
            (double)slot.GainInput.Value);

        var points = baseline.Points
            .Select(point => new DataPoint(point.X, point.Y + band.MagnitudeDbAt(point.X)))
            .ToArray();
        AddWizardSeries(
            model,
            new EqWizardCurve(
                $"Band {selectedBandIndex + 1}",
                BandCurveColor,
                2,
                LineStyle.Dash,
                points));
    }

    // Shades the area between two aligned curves (Source + EQ and the target),
    // visualising the residual error after applying the EQ.
    private static void AddFillBetween(
        PlotModel model,
        EqWizardCurve first,
        EqWizardCurve second)
    {
        OxyColor color = first.Color;
        var area = new AreaSeries
        {
            Color = OxyColors.Transparent,
            Fill = OxyColor.FromArgb(48, color.R, color.G, color.B),
            StrokeThickness = 0,
            Tag = WizardSeriesTag
        };
        area.Points.AddRange(first.Points);
        area.Points2.AddRange(second.Points);
        model.Series.Add(area);
    }

    private void InitializePeqSlotTable()
    {
        peqSlotTable = new TableLayoutPanel
        {
            BackColor = panelPEQ.BackColor,
            ColumnCount = PeqColumnCount,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(4),
            RowCount = PeqRowCount
        };

        for (int column = 0; column < PeqColumnCount; column++)
        {
            peqSlotTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / PeqColumnCount));
        }
        for (int row = 0; row < PeqRowCount; row++)
        {
            peqSlotTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / PeqRowCount));
        }

        peqSlotTable.Click += (_, _) => DeselectBand();
        panelPEQ.Controls.Add(peqSlotTable);
    }

    private void InitializeBandsComboBox()
    {
        darkComboBoxBands.Items.Clear();
        for (int count = 1; count <= MaxPeqSlotCount; count++)
        {
            darkComboBoxBands.Items.Add(count);
        }

        darkComboBoxBands.SelectedIndexChanged += DarkComboBoxBandsSelectedIndexChanged;
        darkComboBoxBands.SelectedIndex = 0;
    }

    // The band limit caps how many bands Auto Tune may create (4..32, default 4).
    private void InitializeBandsLimitComboBox()
    {
        comboBoxBandsLimit.Items.Clear();
        for (int count = MinAutoTuneBandLimit; count <= MaxPeqSlotCount; count++)
        {
            comboBoxBandsLimit.Items.Add(count);
        }

        // Default to the maximum band count.
        comboBoxBandsLimit.SelectedIndex = comboBoxBandsLimit.Items.Count - 1;
    }

    // Source smoothing options (1/N octave) shared with the overlay smoothing.
    private void InitializeSmoothComboBox()
    {
        foreach (int value in OverlaySmoothing.SupportedInverseOctaves)
        {
            comboBoxSmooth.Items.Add(value);
        }

        comboBoxSmooth.Format += (_, args) =>
        {
            if (args.ListItem is int value)
            {
                args.Value = OverlaySmoothing.GetLabel(value);
            }
        };
        comboBoxSmooth.SelectedIndexChanged += (_, _) => DrawSelectedCurves();
        comboBoxSmooth.SelectedIndex = 0;
    }

    private void DarkComboBoxBandsSelectedIndexChanged(object? sender, EventArgs e)
    {
        int slotCount = darkComboBoxBands.SelectedItem is int selectedCount
            ? selectedCount
            : 1;
        SetPeqSlotCount(slotCount);
    }
}
