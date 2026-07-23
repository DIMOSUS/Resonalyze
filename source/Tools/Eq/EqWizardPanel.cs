using System.ComponentModel;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
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
    EqWizardCurve? SourcePlusEq);

public partial class EqWizardPanel : UserControl
{
    private const int MaxPeqSlotCount = 32;
    private const int MinAutoTuneBandLimit = 4;
    private const int PeqColumnCount = 16;
    private const int PeqRowCount = 2;
    private const string WizardSeriesTag = "eq-wizard:curve";
    private const string WizardTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00} dB";
    // The EQ filter-response curve lives on its own right-hand axis so its gain reads
    // around 0 dB regardless of the source's level (an absolute dB SPL source puts the
    // shared left axis tens of dB away from 0, where the filter shape would be clipped).
    private const string EqGainAxisKey = "eq-wizard:gain";

    private const string FrequencyTip = "Band center frequency (Hz).";
    private const string QTip = "Band quality factor (Q) — higher Q is a narrower band.";
    private const string GainTip = "Band gain (dB). Positive boosts, negative cuts.";

    private readonly ToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 8000,
        ShowAlways = true
    };

    private readonly List<PeqSlotControl> peqSlots = new();
    private readonly EqWizardAutoTuneOrchestrator autoTuneOrchestrator = new();
    private readonly EqWizardImportExportCoordinator importExportCoordinator = new();
    private TableLayoutPanel peqSlotTable = null!;
    private PlotLabelsPanelController plotLabels = null!;
    private PlotWatermarkAnnotation hintAnnotation = null!;
    private LineAnnotation fromMarker = null!;
    private LineAnnotation toMarker = null!;
    private RectangleAnnotation rangeFill = null!;
    private int selectedBandIndex = -1;
    private int activeBandCount;
    private EqTuneStats? lastStats;
    private bool suppressRedraw;
    private bool suppressWindowClamp;
    private bool suppressGainClamp;

    // Smallest allowed gap between the From and To frequencies (Hz).
    private const decimal MinFrequencyGapHz = 1m;

    // Smallest allowed gap between the Min and Max gain limits (dB), so the range
    // never collapses or inverts.
    private const decimal MinGainGapDb = 1m;

    // Colour of the highlighted single-band contribution curve (semi-transparent).
    private static readonly OxyColor BandCurveColor = OxyColor.FromArgb(150, 255, 170, 40);

    // Right EQ-gain axis furniture: a dim white matching the white EQ filter curve.
    private static readonly OxyColor EqAxisColor = OxyColor.FromRgb(205, 205, 205);

    public EqWizardPanel()
    {
        InitializeComponent();
        InitializePlotWizard();
        InitializePeqSlotTable();
        InitializeBandsComboBox();
        InitializeBandsLimitComboBox();
        InitializeSmoothComboBox();
        InitializeSampleRateComboBox();
        // Open on Click (which fires after the mouse-up) and defer with BeginInvoke.
        // Showing a dropdown synchronously inside the mouse message is swallowed by the
        // focus change, and showing it on mouse-down lets the click's own mouse-up land
        // outside the just-opened menu and close it again.
        buttonLoadIr.Click += (_, _) => buttonLoadIr.BeginInvoke(ShowSourceMenu);
        comboBoxCalibration.SelectedIndexChanged += (_, _) => OnCalibrationChanged();
        NumericTargetOffset.ValueChanged += (_, _) => OnTargetOffsetChanged();
        NumericGain.ValueChanged += (_, _) => DrawSelectedCurves();
        checkBoxBypass.CheckedChanged += (_, _) => DrawSelectedCurves();
        checkBoxCutsOnly.CheckedChanged += (_, _) =>
        {
            // Only the next fit reads it, but orphan any in-flight one so a result
            // computed under the old setting cannot land.
            autoTuneOrchestrator.Invalidate();
            RaiseSettingsChanged();
        };
        buttonAutoTune.Click += (_, _) => AutoTune();
        buttonOverlaySettings.Click += (_, _) => OpenTargetSettings();
        buttonImport.Click += (_, _) => ImportPeq();
        buttonExport.Click += (_, _) => ExportPeq();
        numericFromHz.ValueChanged += (_, _) => FrequencyBoundChanged(fromChanged: true);
        numericToHz.ValueChanged += (_, _) => FrequencyBoundChanged(fromChanged: false);
        numericGainMin.ValueChanged += (_, _) => GainBoundChanged(minChanged: true);
        numericGainMax.ValueChanged += (_, _) => GainBoundChanged(minChanged: false);
        // Clicking away from the bands clears the highlighted single-band curve.
        Click += (_, _) => DeselectBand();
        panelPEQ.Click += (_, _) => DeselectBand();
        plotWizard.Click += (_, _) => DeselectBand();
        InitializeToolTips();
        ApplyGainRange();
    }

    // Keeps Min strictly below Max, then applies the range. Mirrors the From/To
    // frequency guard so the gain limits can never collide or invert.
    private void GainBoundChanged(bool minChanged)
    {
        if (suppressGainClamp)
        {
            return;
        }

        EnforceGainOrder(minChanged);
        ApplyGainRange();
    }

    // Enforces Min <= Max - gap by pushing the opposite bound; if that bound is at
    // its limit, the just-edited bound is pulled back instead. The suppression flag
    // stops the programmatic adjustment from re-entering this logic.
    private void EnforceGainOrder(bool minChanged)
    {
        if (numericGainMin.Value <= numericGainMax.Value - MinGainGapDb)
        {
            return;
        }

        suppressGainClamp = true;
        try
        {
            if (minChanged)
            {
                decimal desiredMax = numericGainMin.Value + MinGainGapDb;
                if (desiredMax <= numericGainMax.Maximum)
                {
                    numericGainMax.Value = desiredMax;
                }
                else
                {
                    numericGainMax.Value = numericGainMax.Maximum;
                    numericGainMin.Value = numericGainMax.Maximum - MinGainGapDb;
                }
            }
            else
            {
                decimal desiredMin = numericGainMax.Value - MinGainGapDb;
                if (desiredMin >= numericGainMin.Minimum)
                {
                    numericGainMin.Value = desiredMin;
                }
                else
                {
                    numericGainMin.Value = numericGainMin.Minimum;
                    numericGainMax.Value = numericGainMin.Minimum + MinGainGapDb;
                }
            }
        }
        finally
        {
            suppressGainClamp = false;
        }
    }

    // Pushes the Min/Max Gain limits onto every band's numeric field and fader so
    // the whole bank shares one scale, then redraws for the (possibly clamped) EQ.
    private void ApplyGainRange()
    {
        decimal minimum = numericGainMin.Value;
        decimal maximum = numericGainMax.Value;
        // SetGainRange can clamp a band's value, and that ValueChanged redraws, so
        // narrowing the range would rebuild the plot up to 32 times. Batch the whole
        // bank behind a single redraw.
        suppressRedraw = true;
        try
        {
            foreach (PeqSlotControl slot in peqSlots)
            {
                slot.SetGainRange(minimum, maximum);
            }
        }
        finally
        {
            suppressRedraw = false;
        }

        UpdateEqAxisRange();
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    // Sizes the right EQ-gain axis to the current boost/cut budget (the Min/Max Gain
    // limits) with a little headroom, so the filter curve — bounded by those limits —
    // is always fully on the axis and the axis doubles as a read-out of the budget.
    private void UpdateEqAxisRange()
    {
        if (plotWizard.Model?.Axes.FirstOrDefault(axis => axis.Key == EqGainAxisKey)
            is not LinearAxis eqAxis)
        {
            return;
        }

        // Round outward to the 6 dB major step and add one step of headroom for the
        // extra gain that overlapping bands can sum to beyond a single band's limit.
        double lower = Math.Floor((double)numericGainMin.Value / 6) * 6 - 6;
        double upper = Math.Ceiling((double)numericGainMax.Value / 6) * 6 + 6;
        eqAxis.Minimum = lower;
        eqAxis.Maximum = upper;
        eqAxis.AbsoluteMinimum = lower;
        eqAxis.AbsoluteMaximum = upper;
    }

    private void InitializeToolTips()
    {
        SetTip(buttonLoadIr,
            "Choose the curve to equalize: an impulse response (file or history), " +
            "a captured overlay slot, or a measured curve from a text file.");
        SetTip(buttonOverlaySettings,
            "Edit the target curve this mode corrects toward (isolated to the EQ " +
            "Wizard; not tied to any overlay).");
        SetTip(labelCalibration, comboBoxCalibration,
            "Microphone calibration applied to the source curve. \"Own\" re-uses the " +
            "correction stored with an imported curve; unavailable when the curve " +
            "carries no uncalibrated reference (its calibration is already baked in).");
        SetTip(labelSampleRate, comboBoxSampleRate,
            "Sample rate the fitted filters are realized at, and the rate written into " +
            "an exported profile. Locked to the source's own rate when it states one.");
        SetTip(labelTargetOffset, NumericTargetOffset,
            "Vertical offset of the target curve (dB).");
        SetTip(labelGain, NumericGain,
            "EQ preamp (dB) applied on top of all bands. Usually negative to leave " +
            "headroom for boosts.");
        SetTip(labelBands, darkComboBoxBands, "Number of PEQ bands shown.");
        SetTip(labelSmooth, comboBoxSmooth,
            "Smoothing of the source curve (1/N octave), used for display and Auto " +
            "Tune. Unavailable for an imported curve that carries no unsmoothed " +
            "reference, where it would compound the smoothing already applied.");
        SetTip(checkBoxBypass,
            "Show the curves without the EQ applied (Source + EQ equals Source).");
        SetTip(labelGainMin, numericGainMin,
            "Lowest gain (dB) every band's field and fader allow — the maximum cut. " +
            "Also bounds what Auto Tune may apply.");
        SetTip(labelGainMax, numericGainMax,
            "Highest gain (dB) every band's field and fader allow — the maximum boost. " +
            "Also bounds what Auto Tune may apply.");
        SetTip(labelBandsLimit, comboBoxBandsLimit,
            "Maximum number of bands Auto Tune may create.");
        SetTip(labelFromHz, numericFromHz,
            "Lower edge of the Auto Tune frequency window; also bounds the error metrics.");
        SetTip(labelToHz, numericToHz,
            "Upper edge of the Auto Tune frequency window; also bounds the error metrics.");
        SetTip(checkBoxCutsOnly,
            "Auto Tune only cuts, never boosts — the safe default for a car tune " +
            "(a boost cannot fill an interference null, it just burns headroom). " +
            "Uncheck to allow boosts, still limited to reliable regions: high " +
            "coherence and not inside a narrow, deep null.");
        SetTip(buttonAutoTune,
            "Automatically fit the bands and preamp so Source + EQ approaches the " +
            "target within the frequency window.");
        SetTip(buttonImport,
            "Import a PEQ profile (Equalizer APO, REW, CSV, EasyEffects, CamillaDSP).");
        SetTip(buttonExport,
            "Export the PEQ as a profile file or a printable tuning-sheet PDF.");
    }

    private void SetTip(Control label, Control control, string text)
    {
        SetTip(label, text);
        SetTip(control, text);
    }

    // Applies the tooltip to a control and its children, so it shows over composite
    // controls (DarkComboBox, DarkNumericUpDown) too.
    private void SetTip(Control control, string text)
    {
        toolTip.SetToolTip(control, text);
        foreach (Control child in control.Controls)
        {
            SetTip(child, text);
        }
    }

    internal IReadOnlyList<PeqSlotControl> PeqSlots => peqSlots;

    // The selected width, whether or not the selector currently applies. A source with
    // no unsmoothed reference ignores it (ComputeImportedCurve draws the stored points
    // as-is) rather than reading it as Off — which would persist Off over the user's
    // real preference the moment such a curve is loaded.
    private int SourceSmoothingInverseOctaves =>
        comboBoxSmooth.SelectedItem is int value ? value : 0;

    // Reports the current tuning result (or null when nothing is being tuned) to the
    // results panel. Wired by the host form; null until then.
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<EqTuneStats?>? ResultsChanged { get; set; }

    // Builds all 32 band strips once. Hiding unused strips would waste the fader
    // bank, so the whole 16x2 grid is always shown; the Bands control only chooses
    // how many are active (SetActiveBandCount), the rest stay visible but greyed.
    private void CreatePeqSlots()
    {
        peqSlotTable.SuspendLayout();
        suppressRedraw = true;
        try
        {
            for (int index = 0; index < MaxPeqSlotCount; index++)
            {
                var slot = new PeqSlotControl
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(1),
                    SlotNumber = index + 1
                };
                // Row-major: bands 1..16 fill the top row left to right, 17..32
                // the bottom row, matching how the fader bank reads.
                int row = index / PeqColumnCount;
                int column = index % PeqColumnCount;
                slot.FrequencyInput.Value =
                    slot.FrequencyInput.ClampValue(DefaultBandFrequencyHz(index));
                slot.FrequencyInput.ValueChanged += PeqBandValueChanged;
                slot.QInput.ValueChanged += PeqBandValueChanged;
                slot.GainInput.ValueChanged += PeqBandValueChanged;
                SetTip(slot.FrequencyInput, FrequencyTip);
                SetTip(slot.QInput, QTip);
                SetTip(slot.GainInput, GainTip);
                slot.Activated += (sender, _) => SelectBand((PeqSlotControl)sender!);
                peqSlots.Add(slot);
                peqSlotTable.Controls.Add(slot, column, row);
            }
        }
        finally
        {
            suppressRedraw = false;
            peqSlotTable.ResumeLayout();
        }
    }

    // ISO 266 preferred 1/3-octave centre frequencies, the ones a 31/32-band
    // graphic EQ is built on. 32 values (16 Hz .. 20 kHz) match the 32 strips
    // exactly: the standard 31-band 20 Hz..20 kHz set plus 16 Hz below it.
    private static readonly double[] IsoThirdOctaveCentersHz =
    {
        16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500,
        630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000,
        10000, 12500, 16000, 20000
    };

    // Default band centre for a strip: its ISO 1/3-octave frequency (audiophile
    // graphic-EQ default) instead of every band starting at 1 kHz.
    private static double DefaultBandFrequencyHz(int index) =>
        IsoThirdOctaveCentersHz[
            Math.Clamp(index, 0, IsoThirdOctaveCentersHz.Length - 1)];

    // The Bands control sets how many strips are active; the remaining strips stay
    // visible but disabled and are excluded from the EQ curve and the stats.
    private void SetActiveBandCount(int count)
    {
        activeBandCount = Math.Clamp(count, 1, MaxPeqSlotCount);
        for (int index = 0; index < peqSlots.Count; index++)
        {
            peqSlots[index].Enabled = index < activeBandCount;
        }

        // A band that just became inactive must not stay selected/highlighted.
        if (selectedBandIndex >= activeBandCount)
        {
            DeselectBand();
        }

        // Changing the active count changes the EQ curve, so redraw.
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    // The active band strips (the leading prefix of the always-present 32).
    private IEnumerable<PeqSlotControl> ActiveSlots => peqSlots.Take(activeBandCount);

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

    private void InitializePlotWizard()
    {
        // Mirror the axis style used by the Frequency Response and Live Spectrum
        // plots: a logarithmic 20 Hz - 20 kHz frequency axis and a dB axis.
        PlotModel model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            AbsoluteMinimum = -90,
            AbsoluteMaximum = 20,
            MajorStep = 10,
            Minimum = -80,
            Maximum = 10,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "dB",
        });

        // A dedicated right-hand axis for the EQ filter curve, centred on 0 dB so the
        // correction shape is readable whatever the source's level. It owns no gridlines
        // (the left axis draws them) and is a fixed reference, not pannable; its range
        // follows the boost/cut budget so the curve cannot fall off it. A subtle 0-line
        // marks unity gain.
        model.Axes.Add(new LinearAxis
        {
            Key = EqGainAxisKey,
            Position = AxisPosition.Right,
            MajorStep = 6,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = EqAxisColor,
            TitleColor = EqAxisColor,
            TicklineColor = EqAxisColor,
            ExtraGridlines = new[] { 0.0 },
            ExtraGridlineColor = OxyColor.FromAColor(60, OxyColors.White),
            ExtraGridlineStyle = LineStyle.Solid,
            Title = "EQ (dB)",
            IsPanEnabled = false,
            IsZoomEnabled = false
        });

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

        // Translucent green band behind the curves marking the Auto Tune range,
        // bounded by dashed guides at its edges.
        rangeFill = new RectangleAnnotation
        {
            Fill = OxyColor.FromArgb(10, 90, 210, 120),
            StrokeThickness = 0,
            Layer = AnnotationLayer.BelowSeries
        };
        model.Annotations.Add(rangeFill);
        fromMarker = CreateRangeMarker();
        toMarker = CreateRangeMarker();
        model.Annotations.Add(fromMarker);
        model.Annotations.Add(toMarker);
        fromMarker.X = (double)numericFromHz.Value;
        toMarker.X = (double)numericToHz.Value;
        rangeFill.MinimumX = fromMarker.X;
        rangeFill.MaximumX = toMarker.X;

        plotWizard.Model = model;
        UpdateEqAxisRange();
        PlotInteraction.EnableDoubleClickAxisReset(plotWizard);

        // Reuse the main plot's bottom legend so the curve list looks identical.
        plotLabels = new PlotLabelsPanelController(plotWizard, () => Mode.EqWizard);
    }

    private static LineAnnotation CreateRangeMarker() => new()
    {
        Type = LineAnnotationType.Vertical,
        Color = OxyColor.FromArgb(100, 90, 210, 120),
        StrokeThickness = 1,
        LineStyle = LineStyle.Dash,
        Layer = AnnotationLayer.AboveSeries
    };

    // Moves the range guides and shaded band to the current From/To frequencies.
    private void UpdateAutoTuneRangeMarkers()
    {
        fromMarker.X = (double)numericFromHz.Value;
        toMarker.X = (double)numericToHz.Value;
        rangeFill.MinimumX = fromMarker.X;
        rangeFill.MaximumX = toMarker.X;
        plotWizard.InvalidatePlot(false);
    }

    // Keeps From strictly below To, then refreshes the guides and windowed stats.
    private void FrequencyBoundChanged(bool fromChanged)
    {
        if (suppressWindowClamp)
        {
            return;
        }

        EnforceFrequencyOrder(fromChanged);
        OnFrequencyWindowChanged();
    }

    // Enforces From <= To - gap by pushing the opposite bound; if that bound is at
    // its limit, the just-edited bound is pulled back instead. The suppression flag
    // stops the programmatic adjustment from re-entering this logic.
    private void EnforceFrequencyOrder(bool fromChanged)
    {
        if (numericFromHz.Value <= numericToHz.Value - MinFrequencyGapHz)
        {
            return;
        }

        suppressWindowClamp = true;
        try
        {
            if (fromChanged)
            {
                decimal desiredTo = numericFromHz.Value + MinFrequencyGapHz;
                if (desiredTo <= numericToHz.Maximum)
                {
                    numericToHz.Value = desiredTo;
                }
                else
                {
                    numericToHz.Value = numericToHz.Maximum;
                    numericFromHz.Value = numericToHz.Maximum - MinFrequencyGapHz;
                }
            }
            else
            {
                decimal desiredFrom = numericToHz.Value - MinFrequencyGapHz;
                if (desiredFrom >= numericFromHz.Minimum)
                {
                    numericFromHz.Value = desiredFrom;
                }
                else
                {
                    numericFromHz.Value = numericFromHz.Minimum;
                    numericToHz.Value = numericFromHz.Minimum + MinFrequencyGapHz;
                }
            }
        }
        finally
        {
            suppressWindowClamp = false;
        }
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
        // Every input the Auto Tune fit consumes funnels through here when it
        // changes (target selection, offsets, smoothing, band edits, source
        // switches), so any redraw orphans an in-flight fit computed against
        // the previous state. Over-invalidation is safe: the stale result is
        // simply dropped and the user re-runs Auto Tune.
        autoTuneOrchestrator.Invalidate();

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
        EqWizardRenderSet render = BuildRenderSet(eq);
        UpdateSourceHint();
        // The target curve is always present, so its editor and offset are always
        // usable; the preamp only matters once there is a source to apply it to.
        buttonOverlaySettings.Enabled = true;
        NumericTargetOffset.Enabled = true;
        NumericGain.Enabled = !bypass && render.SourcePlusEq != null;
        bool showEqCurves = render.SourcePlusEq != null;
        lastStats = BuildStats(render, eq);
        ResultsChanged?.Invoke(lastStats);

        // Shade the gap between Source + EQ and the target first (red above, blue
        // below), so the curves draw on top of the two-colour deviation band.
        if (showEqCurves)
        {
            AddDeviationFill(model, render.SourcePlusEq!, render.Target);
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

        AddEqCurve(model, eq, render.Target);
        AddSelectedBandCurve(model, render.Target);

        plotLabels.Refresh();
        model.InvalidatePlot(true);
    }

    // Reads the active PEQ slots and the preamp control into a logical EQ curve.
    // Inactive strips (beyond the chosen band count) are excluded.
    private EqualizationCurve BuildEqualizationCurve()
    {
        IEnumerable<PeqBand> bands = ActiveSlots.Select(slot => new PeqBand(
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
        int filtersUsed = ActiveSlots.Count(
            slot => Math.Abs((double)slot.GainInput.Value) >= 0.05);

        double peakBoost = double.NegativeInfinity;
        double peakCut = double.PositiveInfinity;
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 256))
        {
            double gain = DigitalEqualizationResponse.MagnitudeDbAt(
                eq, frequency, EqSampleRate);
            peakBoost = Math.Max(peakBoost, gain);
            peakCut = Math.Min(peakCut, gain);
        }

        // Headroom is the margin to 0 dB: positive when the EQ never exceeds unity,
        // negative (a required attenuation) when it boosts.
        double headroom = -peakBoost;
        return new EqTuneStats(rms, maxError, filtersUsed, peakBoost, peakCut, headroom);
    }

    // Fits the PEQ bands and preamp automatically so Source + EQ approaches Target,
    // then writes the result into the controls. The fit runs on a worker thread —
    // tuning up to 32 bands takes long enough to visibly freeze the UI.
    private async void AutoTune()
    {
        // Build against a neutral EQ so the fit sees the raw source and a target
        // aligned to the source's frequencies.
        EqWizardRenderSet render = BuildRenderSet(
            new EqualizationCurve(Array.Empty<PeqBand>()));
        if (render.Source == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        var request = new EqWizardAutoTuneRequest(
            render.Source.Points.Select(point => new SignalPoint(point.X, point.Y)),
            render.Target.Points.Select(point => new SignalPoint(point.X, point.Y)),
            CreateAutoTuneOptions(),
            // Only a loopback-transfer impulse response carries coherence; an imported
            // curve has none, so boosts fall back to null-detection alone.
            loadedSource?.Coherence);

        // Only the Auto Tune button is disabled while the fit runs — the user
        // can still switch the target, offsets, smoothing, the band limit or
        // the whole history measurement. A result computed against the old
        // inputs must not be written over the new state, so anything that
        // changes the fit's inputs bumps this revision and orphans the result.
        EqualizationCurve? tuned;
        buttonAutoTune.Enabled = false;
        try
        {
            tuned = await autoTuneOrchestrator.TuneLatestAsync(request);
        }
        catch (Exception exception)
        {
            ShowFileError("Auto Tune failed.", exception);
            return;
        }
        finally
        {
            if (!IsDisposed)
            {
                buttonAutoTune.Enabled = true;
            }
        }

        if (IsDisposed || tuned == null)
        {
            return;
        }

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
            PreampMaxDb = (double)NumericGain.Maximum,
            // Per-band gain is bounded by the Min/Max Gain fields, exactly like the
            // manual faders, so Auto Tune never proposes a gain the strips reject.
            BandGainMinDb = (double)numericGainMin.Value,
            BandGainMaxDb = (double)numericGainMax.Value,
            // The wizard's output is a profile for a real DSP: the total gain
            // (preamp + bands) must not exceed 0 dB anywhere, or the profile
            // clips before the user ever sees the headroom read-out.
            TotalGainMaxDb = 0,
            SampleRateHz = EqSampleRate,
            // Cuts-only is the safe default for a car tune: never boost an
            // interference null. Unchecking it allows boosts, still gated to
            // reliable regions (high coherence, not inside a narrow deep null).
            CutsOnlyMode = checkBoxCutsOnly.Checked
        };

        // Q has no panel-level range control, so take its bounds from a band field.
        if (peqSlots.Count > 0)
        {
            PeqSlotControl slot = peqSlots[0];
            options = options with
            {
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
                    slot.FrequencyInput.Value = slot.FrequencyInput.ClampValue(band.FrequencyHz);
                    slot.QInput.Value = slot.QInput.ClampValue(band.Q);
                    slot.GainInput.Value = slot.GainInput.ClampValue(band.GainDb);
                }
                else
                {
                    // No band for this slot: leave it transparent.
                    slot.GainInput.Value = slot.GainInput.ClampValue(0);
                }
            }

            NumericGain.Value = NumericGain.ClampValue(curve.PreampDb);
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

    // The panel owns only the native dialog and UI feedback. Target resolution,
    // sample-rate-specific format setup, PDF/text dispatch and I/O errors belong
    // to the coordinator and are covered without WinForms.
    private void ExportPeq()
    {
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = importExportCoordinator.DefaultExportExtension,
            FileName = "eq",
            Filter = importExportCoordinator.ExportFilter,
            Title = "Export PEQ"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        EqualizationCurve curve = BuildEqualizationCurve();
        (double minHz, double maxHz) = GetFrequencyWindow();
        EqWizardFileResult result = importExportCoordinator.Export(
            new EqWizardExportRequest(
                dialog.FileName,
                importExportCoordinator.ResolveExportTarget(dialog.FilterIndex),
                curve,
                EqSampleRate,
                System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
                minHz,
                maxHz,
                lastStats));
        if (!result.Success)
        {
            ShowFileError("PEQ could not be exported.", result.Exception!);
        }
    }

    // Loads a PEQ using the format chosen in the dialog and applies it. Parsing
    // tolerates broken or hand-edited files, so only file access can fail here.
    private void ImportPeq()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = importExportCoordinator.ImportFilter,
            Title = "Import PEQ"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        EqWizardFileResult<EqualizationCurve> result = importExportCoordinator.Import(
            new EqWizardImportRequest(
                dialog.FileName,
                importExportCoordinator.ResolveImportTarget(dialog.FilterIndex)));
        if (!result.Success)
        {
            ShowFileError("PEQ could not be imported.", result.Exception!);
            return;
        }

        // Show the imported EQ rather than leaving it hidden behind bypass.
        checkBoxBypass.Checked = false;
        ApplyEqualizationCurve(result.Value!);
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

    private static void AddWizardSeries(
        PlotModel model,
        EqWizardCurve curve,
        string? yAxisKey = null)
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
        // Curves with no key bind to the default (left) axis; only the EQ curve names
        // the right gain axis.
        if (!string.IsNullOrEmpty(yAxisKey))
        {
            series.YAxisKey = yAxisKey;
        }

        series.Points.AddRange(curve.Points);
        model.Series.Add(series);
    }

    // Draws the EQ filter response itself (all bands, without the preamp) as a white
    // line, sampled on the baseline frequencies. Values are the raw EQ gain in dB, drawn
    // on the dedicated right-hand gain axis so the correction shape reads around 0 dB
    // whatever the source's absolute level.
    private void AddEqCurve(PlotModel model, EqualizationCurve eq, EqWizardCurve? baseline)
    {
        if (baseline is not { Points.Count: >= 2 })
        {
            return;
        }

        var eqWithoutGain = new EqualizationCurve(eq.Bands, 0);
        var points = baseline.Points
            .Select(point => new DataPoint(
                point.X,
                DigitalEqualizationResponse.MagnitudeDbAt(
                    eqWithoutGain, point.X, EqSampleRate)))
            .ToArray();
        AddWizardSeries(
            model,
            new EqWizardCurve("EQ", OxyColors.White, 1.5, LineStyle.Solid, points),
            EqGainAxisKey);
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
            .Select(point => new DataPoint(
                point.X,
                point.Y + DigitalEqualizationResponse.MagnitudeDbAt(
                    band, point.X, EqSampleRate)))
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

    // Translucent fills for the deviation band between Source + EQ and the target:
    // red where the result sits above the target, blue where it sits below.
    private static readonly OxyColor AboveTargetFill = OxyColor.FromArgb(72, 232, 80, 80);
    private static readonly OxyColor BelowTargetFill = OxyColor.FromArgb(104, 64, 176, 255);

    // Shades the area between Source + EQ and the target in two colours — above the
    // target red, below it blue — so over- and under-correction read at a glance.
    // OxyPlot's TwoColorAreaSeries only splits on a horizontal limit, not a sloped
    // curve, so two AreaSeries are clamped to the target instead: the "above" one
    // fills max(curve, target)..target (zero-width where the curve is below), the
    // "below" one min(curve, target)..target. The two curves are index-aligned on
    // the same frequencies; an exact crossing point is inserted wherever they
    // intersect so neither colour bleeds past the target line.
    private static void AddDeviationFill(
        PlotModel model,
        EqWizardCurve curve,
        EqWizardCurve target)
    {
        IReadOnlyList<DataPoint> c = curve.Points;
        IReadOnlyList<DataPoint> t = target.Points;
        int n = Math.Min(c.Count, t.Count);
        if (n < 2)
        {
            return;
        }

        var curveAug = new List<DataPoint>(n + 8);
        var targetAug = new List<DataPoint>(n + 8);
        for (int i = 0; i < n; i++)
        {
            curveAug.Add(c[i]);
            targetAug.Add(t[i]);
            if (i + 1 >= n)
            {
                continue;
            }

            double d0 = c[i].Y - t[i].Y;
            double d1 = c[i + 1].Y - t[i + 1].Y;
            // Opposite signs => the result crosses the target inside this segment.
            if (double.IsFinite(d0) && double.IsFinite(d1) && d0 * d1 < 0)
            {
                double f = d0 / (d0 - d1);
                double crossX = InterpolateLogX(c[i].X, c[i + 1].X, f);
                double crossY = t[i].Y + f * (t[i + 1].Y - t[i].Y);
                curveAug.Add(new DataPoint(crossX, crossY));
                targetAug.Add(new DataPoint(crossX, crossY));
            }
        }

        AddClampedFill(model, curveAug, targetAug, above: true, AboveTargetFill);
        AddClampedFill(model, curveAug, targetAug, above: false, BelowTargetFill);
    }

    private static void AddClampedFill(
        PlotModel model,
        IReadOnlyList<DataPoint> curve,
        IReadOnlyList<DataPoint> target,
        bool above,
        OxyColor fill)
    {
        var area = new AreaSeries
        {
            Color = OxyColors.Transparent,
            Fill = fill,
            StrokeThickness = 0,
            Tag = WizardSeriesTag
        };
        for (int i = 0; i < curve.Count; i++)
        {
            double clamped = above
                ? Math.Max(curve[i].Y, target[i].Y)
                : Math.Min(curve[i].Y, target[i].Y);
            area.Points.Add(new DataPoint(curve[i].X, clamped));
            area.Points2.Add(target[i]);
        }

        model.Series.Add(area);
    }

    // Interpolates between two frequencies at fraction f in the log domain, matching
    // how the plot's logarithmic X axis draws the segment.
    private static double InterpolateLogX(double x0, double x1, double f)
    {
        if (x0 > 0 && x1 > 0)
        {
            return Math.Exp(Math.Log(x0) + f * (Math.Log(x1) - Math.Log(x0)));
        }

        return x0 + f * (x1 - x0);
    }

    private void InitializePeqSlotTable()
    {
        peqSlotTable = new TableLayoutPanel
        {
            BackColor = panelPEQ.BackColor,
            ColumnCount = PeqColumnCount,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(2),
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
        CreatePeqSlots();
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
        comboBoxSmooth.SelectedIndexChanged += (_, _) =>
        {
            InvalidateSourceCurve();
            RaiseSettingsChanged();
            DrawSelectedCurves();
        };
        comboBoxSmooth.SelectedIndex = 0;
    }

    private void DarkComboBoxBandsSelectedIndexChanged(object? sender, EventArgs e)
    {
        int slotCount = darkComboBoxBands.SelectedItem is int selectedCount
            ? selectedCount
            : 1;
        SetActiveBandCount(slotCount);
    }
}
