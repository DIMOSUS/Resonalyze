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

    private const string FrequencyTip =
        "Band center frequency (Hz). On a shelf this is the middle of the " +
        "transition, where the response has reached half the shelf's gain — not " +
        "the corner where it flattens out.";
    private const string QTip =
        "Band quality factor (Q). On a bell, higher Q is a narrower band. On a " +
        "shelf it is the knee instead: 0.7 is the steepest shelf that still rises " +
        "evenly, and above that the response overshoots before settling.";
    private const string GainTip =
        "Band gain (dB). Positive boosts, negative cuts. A shelf reaches this gain " +
        "beyond its transition and half of it at the center frequency.";

    private readonly WrappingToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 8000,
        ShowAlways = true
    };

    private readonly EqWizardAutoTuneOrchestrator autoTuneOrchestrator = new();
    private readonly EqWizardImportExportCoordinator importExportCoordinator = new();
    private PlotLabelsPanelController plotLabels = null!;
    private PlotWatermarkAnnotation hintAnnotation = null!;
    private LineAnnotation fromMarker = null!;
    private LineAnnotation toMarker = null!;
    private RectangleAnnotation rangeFill = null!;
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
        InitializeQConventionComboBox();
        // Open on Click (which fires after the mouse-up) and defer with BeginInvoke.
        // Showing a dropdown synchronously inside the mouse message is swallowed by the
        // focus change, and showing it on mouse-down lets the click's own mouse-up land
        // outside the just-opened menu and close it again.
        buttonSource.Click += (_, _) => buttonSource.BeginInvoke(ShowSourceMenu);
        comboBoxCalibration.SelectedIndexChanged += (_, _) => OnCalibrationChanged();
        NumericTargetOffset.ValueChanged += (_, _) => OnTargetOffsetChanged();
        // The preamp is part of the filter bank's undo state, so it goes through
        // the same coalescing path as a band field.
        NumericGain.ValueChanged += BankValueChanged;
        checkBoxBypass.CheckedChanged += (_, _) => DrawSelectedCurves();
        checkBoxCutsOnly.CheckedChanged += (_, _) =>
        {
            // Only the next fit reads it, but orphan any in-flight one so a
            // result computed under the previous setting cannot land.
            autoTuneOrchestrator.Invalidate();
            RaiseSettingsChanged();
        };
        buttonAutoTune.Click += (_, _) => AutoTune();
        buttonReturnToDsp.Click += (_, _) => ReturnPeqToVirtualDsp();
        buttonOverlaySettings.Click += (_, _) => OpenTargetSettings();
        buttonImport.Click += (_, _) => ImportPeq();
        buttonExport.Click += (_, _) => ExportPeq();
        buttonResetBands.Click += (_, _) => ResetBands();
        buttonUndo.Click += (_, _) => UndoBankChange();
        buttonRedo.Click += (_, _) => RedoBankChange();
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

    // The nominal range last armed onto the EQ-gain axis, so a redraw can tell "the
    // range this panel chose" from "the view the user zoomed to".
    private (double Lower, double Upper)? eqAxisNominal;

    // Sizes the right EQ-gain axis so it reads as the boost/cut budget yet still contains
    // the drawn filter curve, whose overlapping bands can sum well past a single band's
    // limit. The curve extent (its actual min/max in dB) extends the budget-based range;
    // 0/0 means no curve is drawn yet, leaving the budget range alone. See
    // EqWizardPlotFit.EqGainAxisRange.
    private void UpdateEqAxisRange(double curveMinDb = 0, double curveMaxDb = 0)
    {
        if (plotWizard.Model?.Axes.FirstOrDefault(axis => axis.Key == EqGainAxisKey)
            is not LinearAxis eqAxis)
        {
            return;
        }

        (double lower, double upper) = EqWizardPlotFit.EqGainAxisRange(
            (double)numericGainMin.Value,
            (double)numericGainMax.Value,
            curveMinDb,
            curveMaxDb);
        // The nominal range is always the hard pan/zoom limit — the curve holds
        // nothing beyond it. The RANGE, though, is re-armed only when the nominal
        // itself moved (a budget edit, a curve outgrowing a snap step): the axis
        // zooms and pans now, and this runs on every redraw, so re-arming each
        // time would throw away the zoom the user just set — the same rule the
        // Virtual DSP impulse view follows for its time axis.
        eqAxis.AbsoluteMinimum = lower;
        eqAxis.AbsoluteMaximum = upper;
        if (eqAxisNominal is { } previous &&
            Math.Abs(previous.Lower - lower) < 1e-9 &&
            Math.Abs(previous.Upper - upper) < 1e-9)
        {
            return;
        }

        eqAxisNominal = (lower, upper);
        eqAxis.Minimum = lower;
        eqAxis.Maximum = upper;
        // Drops any user override along with the stale range: a new nominal means
        // the curve or the budget no longer fits the view the user had.
        eqAxis.Reset();
    }

    private void InitializeToolTips()
    {
        SetTip(buttonSource,
            "Choose the curve to equalize: an impulse response (file or history), " +
            "a captured overlay slot, or a measured curve from a text file.");
        SetTip(buttonOverlaySettings,
            "Edit the target curve this mode corrects toward (isolated to the EQ " +
            "Wizard; not tied to any overlay).");
        SetTip(labelCalibration, comboBoxCalibration,
            "Microphone calibration applied to the source curve. \"Own\" re-uses the " +
            "correction stored with an imported curve; unavailable when the curve " +
            "arrived without one (a text import), where its calibration is already " +
            "baked in and cannot be undone.");
        SetTip(labelSampleRate, comboBoxSampleRate,
            "Sample rate the fitted filters are realized at, and the rate written into " +
            "an exported profile. Locked to the source's own rate when it states one.");
        SetTip(labelTargetOffset, NumericTargetOffset,
            "Vertical offset of the target curve (dB).");
        SetTip(labelGain, NumericGain,
            "EQ preamp (dB) applied on top of all bands. Usually negative to leave " +
            "headroom for boosts.");
        SetTip(labelBands, darkComboBoxBands,
            "How many PEQ filters the bank holds. Picking a number creates or trims " +
            "the whole bank at once, spreading new filters over the ISO third-octave " +
            "centres; the + tile adds them one at a time instead.");
        SetTip(buttonResetBands,
            "Clear the whole filter bank: no filters, preamp 0 dB. The source, the " +
            "target and the Auto Tune settings are kept.");
        SetTip(buttonUndo,
            "Undo the last change to the filter bank — a band edit, an added or " +
            "removed filter, a reorder, an import or an Auto Tune (Ctrl+Z). The " +
            "source, the target and the Auto Tune settings are not part of it.");
        SetTip(buttonRedo, "Redo the last undone change to the filter bank (Ctrl+Y).");
        SetTip(labelSmooth, comboBoxSmooth,
            "Smoothing of the source curve (1/N octave), used for display and Auto " +
            "Tune. Unavailable for an imported curve that was already smoothed when it " +
            "was captured, or that never said — smoothing it again would compound it.");
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
        SetTip(labelQConvention, comboBoxQConvention,
            "How the DSP you are tuning defines Q, named as REW names it. RBJ " +
            "(bandwidth = Fc/Q) is the cookbook convention, independent of gain, used " +
            "by Equalizer APO, CamillaDSP, REW Generic/Extended, Audiotec Fischer " +
            "(HELIX/MATCH/BRAX), Audison/Hertz, Mosconi and miniDSP. Symmetric " +
            "(Zölzer/DAFX) widens a band as it deepens, boost and cut alike — over " +
            "twice as wide at 15 dB; used by AMP Panacea, Behringer DCX2496, Rockford " +
            "Fosgate 3Sixty.3, Hypex and rePhase. Classic widens a boost the same way " +
            "but narrows a cut instead, and is rare — the JL Audio TwK-88 is the one " +
            "processor documented for it. Only the tuning sheets are restated — this " +
            "one directly, Virtual DSP's by pre-selecting the question it asks as it " +
            "exports; the fit, the curve on screen and the exported profiles stay RBJ. If you " +
            "do not know your DSP's convention, measure it: one band at +12 and again " +
            "at -12 dB, and compare the two bandwidths.");
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

    private void InitializePlotWizard()
    {
        // Mirror the axis style used by the Frequency Response and Live Spectrum
        // plots: a logarithmic 20 Hz - 20 kHz frequency axis and a dB axis.
        PlotModel model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        // The starting bounds ARE the impulse-response ones, not a copy of their
        // numbers: an IR is what the wizard opens on, and a second set of
        // literals here is a set that can drift away from the one a loaded
        // source re-arms the axis with.
        EqWizardAxisRange initialRange = EqWizardPlotFit.ImpulseResponseRange;
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            AbsoluteMinimum = initialRange.AbsoluteMinimum,
            AbsoluteMaximum = initialRange.AbsoluteMaximum,
            MajorStep = 10,
            Minimum = initialRange.Minimum,
            Maximum = initialRange.Maximum,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "dB",
        });

        // A dedicated right-hand axis for the EQ filter curve, centred on 0 dB so the
        // correction shape is readable whatever the source's level. It owns no gridlines
        // (the left axis draws them). Its NOMINAL range follows the boost/cut budget so
        // the curve cannot fall off it, and that range is the hard pan/zoom limit —
        // within it the axis zooms and pans like any other (hover it and scroll, or
        // drag), for reading a correction's fine structure without the whole budget's
        // height. A subtle 0-line marks unity gain.
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
            Title = "EQ (dB)"
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
        PlotInteraction.Enable(plotWizard);

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

    // Reads the PEQ strips and the preamp control into a logical EQ curve. Every
    // strip in the bank is a filter — an unwanted one is removed, not parked.
    private EqualizationCurve BuildEqualizationCurve() =>
        new(peqSlots.Select(ReadBand), (double)NumericGain.Value);

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

        // The preamp policy differs by mode. Cuts-only: the auto preamp may move
        // within the control's range — it aligns to the least-excess point and can
        // only lower the curve — and the 0 dB total-gain ceiling keeps the profile
        // clip-free. Boosts allowed: the preamp belongs to the user. Auto-centring
        // it would put broadband gain where the Target Level datum belongs, and the
        // ceiling would then "compensate" fitted boosts by dropping the preamp
        // AFTER the fit — bands placed against one level but realised far lower, so
        // the whole curve fell by the peak boost instead of the window rising to
        // the target. Pinning min = max = the current value collapses every preamp
        // decision in the tuner to a no-op: bands carry the full correction within
        // Max Gain, and any positive total gain is the user's explicit choice,
        // reported by the headroom read-out.
        bool cutsOnly = checkBoxCutsOnly.Checked;
        double pinnedPreampDb = (double)NumericGain.Value;

        return new EqAutoTuner.Options
        {
            MaxBands = Math.Clamp(bandLimit, 1, MaxPeqSlotCount),
            MinFrequencyHz = minHz,
            MaxFrequencyHz = maxHz,
            PreampMinDb = cutsOnly ? (double)NumericGain.Minimum : pinnedPreampDb,
            PreampMaxDb = cutsOnly ? (double)NumericGain.Maximum : pinnedPreampDb,
            // Per-band gain is bounded by the Min/Max Gain fields, exactly like the
            // manual faders, so Auto Tune never proposes a gain the strips reject.
            BandGainMinDb = (double)numericGainMin.Value,
            BandGainMaxDb = (double)numericGainMax.Value,
            TotalGainMaxDb = cutsOnly ? 0 : double.PositiveInfinity,
            SampleRateHz = EqSampleRate,
            // Cuts-only is the safe default for a car tune: never boost an
            // interference null. Unchecking it allows boosts, still gated to
            // reliable regions (high coherence, not inside a narrow deep null).
            CutsOnlyMode = cutsOnly,
            // Q has no panel-level range control; the strips' own limits are the
            // range, and reading them from the control class rather than from a
            // strip keeps them available when the bank is empty.
            QMin = PeqSlotControl.MinimumQ,
            QMax = PeqSlotControl.MaximumQ
        };
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
        EqWizardExportTarget target =
            importExportCoordinator.ResolveExportTarget(dialog.FilterIndex);
        if (!ConfirmShelvingBandsDropped(target, curve) ||
            !ConfirmPreampDropped(target, curve))
        {
            return;
        }

        (double minHz, double maxHz) = GetFrequencyWindow();
        EqWizardFileResult result = importExportCoordinator.Export(
            new EqWizardExportRequest(
                dialog.FileName,
                target,
                curve,
                EqSampleRate,
                System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
                minHz,
                maxHz,
                lastStats,
                TargetDspQConvention));
        if (!result.Success)
        {
            ShowFileError("PEQ could not be exported.", result.Exception!);
        }
    }

    // A format that cannot state our shelves would otherwise export a file that is
    // quietly not the tune on screen. The user is told exactly what would be left
    // out and decides; the coordinator drops them either way.
    private bool ConfirmShelvingBandsDropped(
        EqWizardExportTarget target,
        EqualizationCurve curve)
    {
        int dropped = EqWizardImportExportCoordinator.CountShelvingBandsDroppedBy(
            target, curve);
        if (dropped == 0)
        {
            return true;
        }

        string filters = dropped == 1 ? "shelving filter" : $"{dropped} shelving filters";
        return MessageBox.Show(
            FindForm(),
            $"{target.Name} cannot carry a shelving filter the way this EQ defines " +
            $"one, so the {filters} would be left out." +
            Environment.NewLine + Environment.NewLine +
            "The exported profile will hold the peaking filters and the preamp only, " +
            "and will not match the curve on screen. Export anyway?",
            "EQ Wizard",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    // A format whose layout has no preamp slot exports the bands only, so the whole
    // curve lands on the device that many dB off the tune on screen — and, unlike a
    // dropped band, nothing in the file says so. Tell the user the number to enter
    // in the device's own gain control; they decide.
    private bool ConfirmPreampDropped(
        EqWizardExportTarget target,
        EqualizationCurve curve)
    {
        double preampDb = EqWizardImportExportCoordinator.PreampDroppedBy(target, curve);
        if (preampDb == 0)
        {
            return true;
        }

        string gain = FormattableString.Invariant($"{preampDb:+0.0;-0.0} dB");
        // The preamp is signed: leaving out a cut makes the export louder than the
        // curve on screen, leaving out a boost makes it quieter. Say which.
        string direction = preampDb < 0 ? "louder" : "quieter";
        return MessageBox.Show(
            FindForm(),
            $"{target.Name} has no place for the preamp, so the {gain} would be left " +
            $"out and the exported bands alone are that much {direction} than the tune " +
            "on screen." +
            Environment.NewLine + Environment.NewLine +
            $"Enter {gain} in the channel's own gain control on the device after " +
            "importing the bands. Export anyway?",
            "EQ Wizard",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
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

        // Fit the right axis to the curve just built (the summed band response), so a
        // stack of overlapping bands taller than one band's limit is not clipped.
        double curveMin = 0;
        double curveMax = 0;
        foreach (DataPoint point in points)
        {
            if (!double.IsFinite(point.Y))
            {
                continue;
            }

            curveMin = Math.Min(curveMin, point.Y);
            curveMax = Math.Max(curveMax, point.Y);
        }

        UpdateEqAxisRange(curveMin, curveMax);
    }

    // Draws the highlighted band's individual contribution relative to the target
    // curve (target with only that one band applied), so its shape, width and gain
    // stand out against where the response should land.
    private void AddSelectedBandCurve(PlotModel model, EqWizardCurve? baseline)
    {
        if (selectedSlot == null || baseline is not { Points.Count: >= 2 })
        {
            return;
        }

        int slotNumber = peqSlots.IndexOf(selectedSlot) + 1;
        if (slotNumber < 1)
        {
            return;
        }

        PeqBand band = ReadBand(selectedSlot);
        var points = baseline.Points
            .Select(point => new DataPoint(
                point.X,
                point.Y + DigitalEqualizationResponse.MagnitudeDbAt(
                    band, point.X, EqSampleRate)))
            .ToArray();
        AddWizardSeries(
            model,
            new EqWizardCurve(
                $"Band {slotNumber}",
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
}
