using System.ComponentModel;
using System.Numerics;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed record EqWizardCurve(
    string Title,
    OxyColor Color,
    double StrokeThickness,
    LineStyle LineStyle,
    IReadOnlyList<DataPoint> Points);

// Source and SourcePlusEq are null without a source; otherwise Target is sampled on the source's frequencies.
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
    private const string PhaseTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.0} °";
    // Dense enough that a Q=20 all-pass (most of 360° in 1/20 oct) gets several points per wrap, so seam detection holds.
    private const int PhaseGridPointCount = 1500;
    // Own right-hand axis so the EQ reads around 0 dB even when a dB SPL source puts the left axis far from 0.
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
    private const string AllPassGroupDelayTip =
        "The extra group delay this all-pass piles up at its own corner — why it " +
        "works, and on a low corner its main cost. It grows with Q and falls with " +
        "frequency (≈ 4Q/ω₀ for 2nd order): around 10 ms per unit Q at 60 Hz, but " +
        "only ~0.3 ms at 2 kHz.";

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
    private LineAnnotation bandMarker = null!;
    private RectangleAnnotation rangeFill = null!;
    private EqTuneStats? lastStats;
    private bool suppressRedraw;
    private bool suppressWindowClamp;
    private bool suppressGainClamp;

    private const decimal MinFrequencyGapHz = 1m;

    private const decimal MinGainGapDb = 1m;

    private static readonly OxyColor BandCurveColor = OxyColor.FromArgb(150, 255, 170, 40);

    private static readonly OxyColor EqAxisColor = OxyColor.FromRgb(205, 205, 205);

    public EqWizardPanel()
    {
        InitializeComponent();
        // Before layout: the layout pass stretches the plot by deltas from the designed positions.
        CaptureLayoutBaseline();
        Ui.DarkScrollBars.Apply(this);
        InitializePlotWizard();
        InitializePeqSlotTable();
        InitializeBandsComboBox();
        InitializeBandsLimitComboBox();
        InitializeSmoothComboBox();
        InitializeSampleRateComboBox();
        InitializeQConventionComboBox();
        // Open on Click and defer via BeginInvoke: a synchronous show is swallowed by the focus change, and on mouse-down
        // the click's own mouse-up closes the menu.
        buttonSource.Click += (_, _) => ShowSourceMenu();
        comboBoxCalibration.SelectedIndexChanged += (_, _) => OnCalibrationChanged();
        NumericTargetOffset.ValueChanged += (_, _) => OnTargetOffsetChanged();
        // The preamp is part of the bank's undo state.
        NumericGain.ValueChanged += BankValueChanged;
        checkBoxBypass.CheckedChanged += (_, _) => DrawSelectedCurves();
        checkBoxEqPhase.CheckedChanged += (_, _) => DrawSelectedCurves();
        checkBoxEqCurve.CheckedChanged += (_, _) =>
        {
            DrawSelectedCurves();
            RaiseSettingsChanged();
        };
        buttonPhaseGate.Click += (_, _) => OpenPhaseGateDialog();
        checkBoxCutsOnly.CheckedChanged += (_, _) =>
        {
            // Orphan any in-flight fit computed under the previous setting.
            autoTuneOrchestrator.Invalidate();
            RaiseSettingsChanged();
        };
        checkBoxShelves.CheckedChanged += (_, _) =>
        {
            autoTuneOrchestrator.Invalidate();
            RaiseSettingsChanged();
        };
        buttonAutoTune.Click += (_, _) => AutoTune();
        buttonReturnToDsp.Click += (_, _) => ReturnPeqToVirtualDsp();
        buttonBackToDsp.Click += (_, _) => BackToVirtualDsp();
        buttonOverlaySettings.Click += (_, _) => ShowTargetMenu();
        buttonImport.Click += (_, _) => ImportPeq();
        buttonExport.Click += (_, _) => ExportPeq();
        buttonResetBands.Click += (_, _) => ResetBands();
        buttonUndo.Click += (_, _) => UndoBankChange();
        buttonRedo.Click += (_, _) => RedoBankChange();
        numericFromHz.ValueChanged += (_, _) => FrequencyBoundChanged(fromChanged: true);
        numericToHz.ValueChanged += (_, _) => FrequencyBoundChanged(fromChanged: false);
        numericGainMin.ValueChanged += (_, _) => GainBoundChanged(minChanged: true);
        numericGainMax.ValueChanged += (_, _) => GainBoundChanged(minChanged: false);
        numericQMax.ValueChanged += (_, _) =>
        {
            autoTuneOrchestrator.Invalidate();
            RaiseSettingsChanged();
        };
        Click += (_, _) => DeselectBand();
        panelPEQ.Click += (_, _) => DeselectBand();
        plotWizard.Click += (_, _) => DeselectBand();
        InitializeToolTips();
        ApplyGainRange();
    }

    private void GainBoundChanged(bool minChanged)
    {
        if (suppressGainClamp)
        {
            return;
        }

        EnforceGainOrder(minChanged);
        ApplyGainRange();
    }

    // Pushes the opposite bound; if it is at its limit, pulls the edited one back. The flag stops re-entry.
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

    private void ApplyGainRange()
    {
        decimal minimum = numericGainMin.Value;
        decimal maximum = numericGainMax.Value;
        // SetGainRange can clamp and redraw per band (up to 32 rebuilds); batch behind one redraw.
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

    // Last nominal range armed on the axis, to tell the panel's range from the user's zoom.
    private (double Lower, double Upper)? eqAxisNominal;

    // Budget range extended to contain the drawn curve (overlapping bands can exceed one band's limit); 0/0 = no curve.
    private void UpdateEqAxisRange(double curveMinDb = 0, double curveMaxDb = 0)
    {
        if (plotWizard.Model?.Axes.FirstOrDefault(axis => axis.Key == EqGainAxisKey)
            is not LinearAxis eqAxis)
        {
            return;
        }

        // Phase owns the whole axis at a fixed ±180°; the dB branch restores title and step.
        (double lower, double upper) = PhaseMode
            ? (-180.0, 180.0)
            : EqWizardPlotFit.EqGainAxisRange(
                (double)numericGainMin.Value,
                (double)numericGainMax.Value,
                curveMinDb,
                curveMaxDb);
        eqAxis.Title = PhaseMode ? "Phase (°)" : "EQ (dB)";
        eqAxis.MajorStep = PhaseMode ? 90 : 6;
        // Nominal is always the hard limit, but the range is re-armed only when the nominal moved, so redraws keep the user's zoom.
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
        eqAxis.Reset();
    }

    private void InitializeToolTips()
    {
        SetTip(checkBoxEqPhase,
            "Switch the plot to phase (degrees, wrapped to ±180°) — where an " +
            "all-pass band's work becomes visible, since it is flat on a magnitude " +
            "plot by definition.\r\n" +
            "With a channel handed over from Virtual DSP the plot shows the MEASURED " +
            "phase of this channel, with and without its bank, against the " +
            "neighbouring drivers as they stood: an all-pass is dialled in until the " +
            "two lie together through the crossover region.\r\n" +
            "Otherwise it shows the bank's own phase. The source, the target and the " +
            "statistics are magnitudes and leave the plot; they are unchanged when " +
            "you switch back.");
        SetTip(buttonPhaseGate,
            "The window the PHASE curves are read through, and where it opens — the " +
            "same dialog and the same settings the Virtual DSP phase view uses.\r\n" +
            "A channel handed over from that panel arrives with its gate already " +
            "placed, over every driver on screen; changing it here reads this " +
            "channel and its neighbours through the new window alike.\r\n" +
            "The magnitude curves are NOT affected: they keep the steady-state " +
            "window that decides tonal balance.");
        SetTip(buttonSource,
            "Choose the curve to equalize: an impulse response (file or history), " +
            "a captured overlay slot, or a measured curve from a text file.");
        SetTip(buttonReturnToDsp,
            "Send the edited bank (bands and preamp) back to the Virtual DSP " +
            "channel this curve came from, and switch to that tool.");
        SetTip(buttonBackToDsp,
            "Switch back to Virtual DSP WITHOUT applying: the channel keeps the " +
            "PEQ it had, and the filters stay here — exportable, or one Ctrl+Z " +
            "chain back to what the bank held before the handoff. (Simply " +
            "clicking the Virtual DSP tab instead keeps this session open for " +
            "coming back.)");
        SetTip(buttonOverlaySettings,
            "The target curve this mode corrects toward (isolated to the EQ " +
            "Wizard; not tied to any overlay): a parametric shape to edit, or a " +
            "house curve of your own imported from a text file.");
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
        SetTip(labelQMax, numericQMax,
            "Narrowest band Auto Tune may place — the highest Q it is allowed to " +
            "choose. Lower it to keep the fit on broader trends, the ones likelier " +
            "to hold across the listening area, instead of chasing a sharp peak that " +
            "may belong to where the microphone stood; the strips themselves still " +
            "accept any Q up to 20. The fit picks from a fixed ladder of Q values " +
            "(…2.0, 2.8, 4.0, 5.6, 8.0, 10.0), so the effective ceiling is the " +
            "largest of those at or below this number.");
        SetTip(labelFromHz, numericFromHz,
            "Lower edge of the Auto Tune frequency window; also bounds the error metrics.");
        SetTip(labelToHz, numericToHz,
            "Upper edge of the Auto Tune frequency window; also bounds the error metrics.");
        SetTip(checkBoxEqCurve,
            "Draw the bank's own response — the white curve on the right-hand axis, " +
            "in dB here and in degrees in Phase. Turning it off leaves the plot to " +
            "the measurement and the target; the filters keep working either way. " +
            "The right-hand axis goes with it when nothing else is left on it.");
        SetTip(checkBoxCutsOnly,
            "Auto Tune only cuts, never boosts — the safe default for a car tune " +
            "(a boost cannot fill an interference null, it just burns headroom). " +
            "Uncheck to allow boosts, still limited to reliable regions: high " +
            "coherence and not inside a narrow, deep null.");
        SetTip(checkBoxShelves,
            "Let Auto Tune fit a low and a high shelf as well as bells. A car target " +
            "is a bass shelf plus a downward tilt, and a stack of bells copies that " +
            "badly — slots spent on a trend, and ringing between the centres. A shelf " +
            "is kept only where finishing the fit with it beats finishing it without, " +
            "so a response made of resonances alone gets none. Off by default: it " +
            "changes what a fit returns, and with Cuts only unchecked a shelf can lift " +
            "a whole end of the range, so the total boost may pass Max Gain — watch " +
            "the headroom read-out.");
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

    // Applied to children too, so composite controls (DarkComboBox, DarkNumericUpDown) show it.
    private void SetTip(Control control, string text)
    {
        toolTip.SetToolTip(control, text);
        foreach (Control child in control.Controls)
        {
            SetTip(child, text);
        }
    }

    // Selected width even when the selector does not apply: reading it as Off would persist Off over the user's preference.
    private int SourceSmoothingInverseOctaves =>
        comboBoxSmooth.SelectedItem is int value ? value : 0;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<EqTuneStats?>? ResultsChanged { get; set; }

    private void InitializePlotWizard()
    {
        PlotModel model = new PlotModel();
        PlotModelStyle.ApplyChrome(model);
        PlotModelStyle.AddFrequencyAxis(model);
        // The IR bounds themselves, not copied literals that could drift from the ones a loaded source re-arms.
        EqWizardAxisRange initialRange = EqWizardPlotFit.ImpulseResponseRange;
        PlotModelStyle.AddAxis(model, new LinearAxis
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

        // EQ axis centred on 0 dB; no gridlines. Nominal range follows the boost/cut budget and is the hard pan/zoom limit.
        PlotModelStyle.AddAxis(model, new LinearAxis
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

        hintAnnotation = new PlotWatermarkAnnotation
        {
            Text = string.Empty,
            VerticalPosition = 0.66,
            TextColor = OxyColor.FromRgb(230, 184, 0),
            FontSize = 15,
            FontWeight = FontWeights.Bold
        };
        model.Annotations.Add(hintAnnotation);

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

        // Marks where the selected band sits (a low-Q bell's summit is guesswork; shelves have none).
        // No Visible on OxyPlot annotations here, so model membership is the switch.
        bandMarker = new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            Color = BandCurveColor,
            StrokeThickness = 1,
            LineStyle = LineStyle.Dot,
            Layer = AnnotationLayer.AboveSeries
        };

        plotWizard.Model = model;
        UpdateEqAxisRange();
        PlotInteraction.Enable(plotWizard);

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

    private void UpdateAutoTuneRangeMarkers()
    {
        fromMarker.X = (double)numericFromHz.Value;
        toMarker.X = (double)numericToHz.Value;
        rangeFill.MinimumX = fromMarker.X;
        rangeFill.MaximumX = toMarker.X;
        plotWizard.InvalidatePlot(false);
    }

    private void FrequencyBoundChanged(bool fromChanged)
    {
        if (suppressWindowClamp)
        {
            return;
        }

        EnforceFrequencyOrder(fromChanged);
        OnFrequencyWindowChanged();
    }

    // Pushes the opposite bound; if it is at its limit, pulls the edited one back. The flag stops re-entry.
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

    private void OnFrequencyWindowChanged()
    {
        UpdateAutoTuneRangeMarkers();
        DrawSelectedCurves();
    }

    private void DrawSelectedCurves()
    {
        // Every rate change funnels through a redraw, so the strips' GD readouts learn the rate here.
        foreach (PeqSlotControl slot in peqSlots)
        {
            slot.SampleRateHz = EqProcessorSampleRate;
        }

        // Every fit input change funnels through here, so orphan any in-flight fit (over-invalidation is safe).
        autoTuneOrchestrator.Invalidate();

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

        bool bypass = checkBoxBypass.Checked;
        EqualizationCurve eq = bypass
            ? new EqualizationCurve(Array.Empty<PeqBand>())
            : BuildEqualizationCurve();
        EqWizardRenderSet render = BuildRenderSet(eq);
        UpdateSourceHint();
        buttonOverlaySettings.Enabled = true;
        NumericTargetOffset.Enabled = true;
        NumericGain.Enabled = !bypass && render.SourcePlusEq != null;
        bool showEqCurves = render.SourcePlusEq != null;
        lastStats = BuildStats(render, eq);
        ResultsChanged?.Invoke(lastStats);

        // Phase is a mode, not an extra curve: magnitudes are hidden, but stats are still computed so they stay current.
        SetMagnitudeAxisVisible(!PhaseMode);
        if (PhaseMode)
        {
            DrawMeasuredPhaseCurves(model, eq);
        }
        else
        {
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
        }

        AddEqCurve(model, eq, render.Target);
        AddSelectedBandCurve(model, render.Target);
        UpdateSelectedBandMarker(model);
        SetEqAxisVisible(model.Series.Any(series =>
            series is XYAxisSeries { YAxisKey: EqGainAxisKey }));

        plotLabels.Refresh();
        model.InvalidatePlot(true);
    }

    // Every strip is a filter: an unwanted one is removed, not parked.
    private EqualizationCurve BuildEqualizationCurve() =>
        new(peqSlots.Select(ReadBand), (double)NumericGain.Value);

    private EqTuneStats? BuildStats(EqWizardRenderSet? render, EqualizationCurve eq)
    {
        if (render?.SourcePlusEq == null)
        {
            return null;
        }

        IReadOnlyList<DataPoint> corrected = render.SourcePlusEq.Points;
        IReadOnlyList<DataPoint> target = render.Target.Points;
        int count = Math.Min(corrected.Count, target.Count);

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
        // All-pass is always "used": its work is phase, which the gain threshold cannot see.
        int filtersUsed = peqSlots.Count(
            slot => slot.BandType.IsAllPass() ||
                Math.Abs((double)slot.GainInput.Value) >= 0.05);

        double peakBoost = double.NegativeInfinity;
        double peakCut = double.PositiveInfinity;
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 256))
        {
            double gain = DigitalEqualizationResponse.MagnitudeDbAt(
                eq, frequency, EqProcessorSampleRate);
            peakBoost = Math.Max(peakBoost, gain);
            peakCut = Math.Min(peakCut, gain);
        }

        double headroom = -peakBoost;
        return new EqTuneStats(rms, maxError, filtersUsed, peakBoost, peakCut, headroom);
    }

    // Worker thread: tuning up to 32 bands visibly freezes the UI.
    private async void AutoTune()
    {
        // Neutral EQ so the fit sees the raw source and a target aligned to its frequencies.
        EqWizardRenderSet render = BuildRenderSet(
            new EqualizationCurve(Array.Empty<PeqBand>()));
        if (render.Source == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        // The tuner replaces the whole bank, all-pass included; ask before discarding phase work aligned by ear.
        IReadOnlyList<PeqBand> allPass = CaptureBankState().Bands
            .Where(band => band.Type.IsAllPass())
            .ToList();
        bool keepAllPass = false;
        if (allPass.Count > 0)
        {
            DialogResult answer = MessageBox.Show(
                FindForm(),
                $"The bank holds {DescribeAllPassCount(allPass.Count)} the tuner " +
                "cannot fit and would replace." + Environment.NewLine +
                Environment.NewLine +
                "Keep them and tune the remaining slots around them?" +
                Environment.NewLine +
                "No replaces the whole bank with the fit.",
                "EQ Wizard",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                return;
            }

            keepAllPass = answer == DialogResult.Yes;
        }

        // Kept all-pass may leave no slots; say so rather than exceed Max Filters or replace the bank unasked.
        int reserved = keepAllPass ? allPass.Count : 0;
        if (reserved > 0 && reserved >= SelectedBandLimit)
        {
            MessageBox.Show(
                FindForm(),
                $"Keeping {DescribeAllPassCount(reserved)} leaves no room under Max " +
                $"Filters ({SelectedBandLimit}), so there is nothing for the fit to " +
                "place." + Environment.NewLine + Environment.NewLine +
                "Raise Max Filters, or run again and let the fit replace the bank.",
                "EQ Wizard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        List<SignalPoint> fitSource = FitSource(render.Source, keepAllPass ? allPass : [])
            .Select(point => new SignalPoint(point.X, point.Y))
            .ToList();
        List<SignalPoint> fitTarget = render.Target.Points
            .Select(point => new SignalPoint(point.X, point.Y))
            .ToList();

        // A wrong datum is fitted faithfully (whole window boosted/cut), so ask before, not in the headroom read-out after.
        (double windowMinHz, double windowMaxHz) = GetFrequencyWindow();
        string? levelWarning = EqTargetLevelCheck.Warning(
            EqTargetLevelCheck.TargetAboveSourceDb(fitSource, fitTarget, windowMinHz, windowMaxHz),
            checkBoxCutsOnly.Checked,
            windowMinHz,
            windowMaxHz);
        if (levelWarning != null &&
            MessageBox.Show(
                FindForm(),
                levelWarning,
                "EQ Wizard",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var request = new EqWizardAutoTuneRequest(
            fitSource,
            fitTarget,
            CreateAutoTuneOptions(reserved),
            // Loopback γ² or mic-array agreement; without either, boosts fall back to null-detection.
            loadedSource?.Coherence);

        // Inputs stay editable during the fit; any input change bumps the revision and orphans the result.
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

        checkBoxBypass.Checked = false;
        ApplyEqualizationCurve(
            keepAllPass ? WithAllPassBands(tuned, allPass) : tuned);
    }

    /// <summary>
    /// The curve the fit corrects. When kept all-pass bands meet a GATED source they are applied first: through a
    /// window an all-pass is not flat (see <see cref="EqWizardGatedPreview"/>).
    /// </summary>
    private IReadOnlyList<DataPoint> FitSource(
        EqWizardCurve source,
        IReadOnlyList<PeqBand> keptAllPass)
    {
        if (keptAllPass.Count == 0 ||
            loadedSource is not { IsGated: true } gated)
        {
            return source.Points;
        }

        // Same conversion as the source curve, so the tuner's index pairing with the target holds (see ToPlotPoints).
        return ToPlotPoints(
            EqWizardGatedPreview.Render(
                BuildGatedPreviewRequest(
                    gated, new EqualizationCurve(keptAllPass, preampDb: 0))),
            KeepsGaps(gated));
    }

    private int SelectedBandLimit =>
        comboBoxBandsLimit.SelectedItem is int limit ? limit : MaxPeqSlotCount;

    private static string DescribeAllPassCount(int count) =>
        count == 1 ? "an all-pass filter" : $"{count} all-pass filters";

    // Mirrors control limits; bands held back (kept all-pass) come off the fit's budget.
    /// <summary>The Auto Tune settings on the controls, for a fit run elsewhere (AI import) that must match this button.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal EqAutoTunePolicy CurrentAutoTunePolicy => new(
        SelectedBandLimit,
        (double)numericGainMin.Value,
        (double)numericGainMax.Value,
        (double)numericQMax.Value,
        checkBoxCutsOnly.Checked,
        checkBoxShelves.Checked);

    private EqAutoTuner.Options CreateAutoTuneOptions(int reservedBands)
    {
        // Max Filters budgets the BANK: kept bands come off it. A reserve eating the budget is refused in AutoTune; this only clamps.
        int bandLimit = SelectedBandLimit - reservedBands;

        (double minHz, double maxHz) = GetFrequencyWindow();

        // Preamp policy: cuts-only lets it move with a 0 dB ceiling; with boosts it is pinned to the user's value.
        // See docs/tech/eq-auto-tuner.md#wizard-preamp-policy.
        bool cutsOnly = checkBoxCutsOnly.Checked;
        double pinnedPreampDb = (double)NumericGain.Value;

        return new EqAutoTuner.Options
        {
            MaxBands = Math.Clamp(bandLimit, 1, MaxPeqSlotCount),
            MinFrequencyHz = minHz,
            MaxFrequencyHz = maxHz,
            PreampMinDb = cutsOnly ? (double)NumericGain.Minimum : pinnedPreampDb,
            PreampMaxDb = cutsOnly ? (double)NumericGain.Maximum : pinnedPreampDb,
            BandGainMinDb = (double)numericGainMin.Value,
            BandGainMaxDb = (double)numericGainMax.Value,
            TotalGainMaxDb = cutsOnly ? 0 : double.PositiveInfinity,
            SampleRateHz = EqProcessorSampleRate,
            CutsOnlyMode = cutsOnly,
            // Widest Q is the strips' limit (available with an empty bank); narrowest is the user's Max Q, below what strips accept.
            QMin = PeqSlotControl.MinimumQ,
            QMax = (double)numericQMax.Value,
            // Shelves are opt-in: they change the SHAPE returned, and Max Q says nothing about a shelf's knee.
            AllowShelves = checkBoxShelves.Checked
        };
    }

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

    // The panel owns only the dialog and feedback; resolution, format setup and I/O live in the coordinator.
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
            !ConfirmExportLoss(EqExportWarnings.AllPassBandsDropped(target, curve)) ||
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
                EqProcessorSampleRate,
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

    // Formats that cannot state shelves would silently export a different tune; the user is told and decides.
    private bool ConfirmShelvingBandsDropped(
        EqWizardExportTarget target,
        EqualizationCurve curve) =>
        ConfirmExportLoss(EqExportWarnings.ShelvingBandsDropped(target, curve));

    private bool ConfirmPreampDropped(
        EqWizardExportTarget target,
        EqualizationCurve curve) =>
        ConfirmExportLoss(EqExportWarnings.PreampDropped(target, curve));

    private bool ConfirmExportLoss(string? warning) =>
        warning == null ||
        MessageBox.Show(
            FindForm(),
            warning,
            "EQ Wizard",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    // Parsing tolerates broken files, so only file access can fail here.
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
        string? yAxisKey = null,
        string trackerFormat = WizardTrackerFormat)
    {
        var series = new LineSeries
        {
            Color = curve.Color,
            StrokeThickness = curve.StrokeThickness,
            LineStyle = curve.LineStyle,
            Title = curve.Title,
            Tag = WizardSeriesTag,
            TrackerFormatString = trackerFormat
        };
        if (!string.IsNullOrEmpty(yAxisKey))
        {
            series.YAxisKey = yAxisKey;
        }

        series.Points.AddRange(curve.Points);
        model.Series.Add(series);
    }

    // EQ response of all bands (no preamp) on the right axis: gain in dB, or wrapped phase in degrees in phase view.
    private void AddEqCurve(PlotModel model, EqualizationCurve eq, EqWizardCurve? baseline)
    {
        if (baseline is not { Points.Count: >= 2 })
        {
            return;
        }

        if (PhaseMode)
        {
            if (checkBoxEqCurve.Checked)
            {
                AddWizardSeries(
                    model,
                    new EqWizardCurve(
                        "EQ phase",
                        OxyColors.White,
                        1.5,
                        LineStyle.Solid,
                        PhasePoints(eq.Bands, baseline)),
                    EqGainAxisKey,
                    PhaseTrackerFormat);
            }

            // Re-armed even when not drawn: measured phase curves share this axis.
            UpdateEqAxisRange();
            return;
        }

        if (!checkBoxEqCurve.Checked)
        {
            UpdateEqAxisRange();
            return;
        }

        var eqWithoutGain = new EqualizationCurve(eq.Bands, 0);
        var points = baseline.Points
            .Select(point => new DataPoint(
                point.X,
                DigitalEqualizationResponse.MagnitudeDbAt(
                    eqWithoutGain, point.X, EqProcessorSampleRate)))
            .ToArray();
        AddWizardSeries(
            model,
            new EqWizardCurve("EQ", OxyColors.White, 1.5, LineStyle.Solid, points),
            EqGainAxisKey);

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

    // Wrapped (like every phase plot here and REW) with breaks at ±180° seams, keeping the axis fixed.
    private IReadOnlyList<DataPoint> PhasePoints(
        IReadOnlyList<PeqBand> bands,
        EqWizardCurve baseline)
    {
        BiquadCoefficients[] sections = bands
            .Where(band => !band.IsTransparent)
            .Select(band => PeqBiquad.Compute(band, EqProcessorSampleRate))
            .ToArray();
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(
            Math.Max(1, baseline.Points[0].X),
            Math.Max(2, baseline.Points[^1].X),
            PhaseGridPointCount);

        var points = new List<DataPoint>(grid.Count + 16);
        double previous = double.NaN;
        foreach (double frequency in grid)
        {
            Complex response = Complex.One;
            foreach (BiquadCoefficients section in sections)
            {
                response *= BiquadResponse.Evaluate(section, frequency, EqProcessorSampleRate);
            }

            double degrees = response.Phase * (180.0 / Math.PI);
            if (!double.IsNaN(previous) && Math.Abs(degrees - previous) > 180.0)
            {
                points.Add(new DataPoint(frequency, double.NaN));
            }

            previous = degrees;
            points.Add(new DataPoint(frequency, degrees));
        }

        return points;
    }

    // Selected band's contribution on the target (target + that band), or its own phase on the right axis in phase view.
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
        if (PhaseMode)
        {
            AddWizardSeries(
                model,
                new EqWizardCurve(
                    $"Band {slotNumber} phase",
                    BandCurveColor,
                    2,
                    LineStyle.Dash,
                    PhasePoints(new[] { band }, baseline)),
                EqGainAxisKey,
                PhaseTrackerFormat);
            return;
        }

        var points = baseline.Points
            .Select(point => new DataPoint(
                point.X,
                point.Y + DigitalEqualizationResponse.MagnitudeDbAt(
                    band, point.X, EqProcessorSampleRate)))
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

    // Separate from AddSelectedBandCurve: the frequency needs no baseline curve, and applies to both views.
    private void UpdateSelectedBandMarker(PlotModel model)
    {
        model.Annotations.Remove(bandMarker);
        if (selectedSlot == null)
        {
            return;
        }

        // No guard: the strip field cannot go below 10 Hz, so the log axis never sees zero.
        bandMarker.X = ReadBand(selectedSlot).FrequencyHz;
        model.Annotations.Add(bandMarker);
    }

    private static readonly OxyColor AboveTargetFill = OxyColor.FromArgb(72, 232, 80, 80);
    private static readonly OxyColor BelowTargetFill = OxyColor.FromArgb(104, 64, 176, 255);

    // TwoColorAreaSeries splits only on a horizontal limit, so two AreaSeries clamp to the target (curves index-aligned),
    // with exact crossings inserted so neither colour bleeds past the target.
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

    // One area per run of finite points: a NaN vertex would make the renderer close the shape across unmeasured octaves.
    private static void AddClampedFill(
        PlotModel model,
        IReadOnlyList<DataPoint> curve,
        IReadOnlyList<DataPoint> target,
        bool above,
        OxyColor fill)
    {
        AreaSeries? area = null;
        for (int i = 0; i < curve.Count; i++)
        {
            double clamped = above
                ? Math.Max(curve[i].Y, target[i].Y)
                : Math.Min(curve[i].Y, target[i].Y);
            if (!double.IsFinite(clamped) || !double.IsFinite(target[i].Y))
            {
                area = null;
                continue;
            }

            if (area == null)
            {
                area = new AreaSeries
                {
                    Color = OxyColors.Transparent,
                    Fill = fill,
                    StrokeThickness = 0,
                    Tag = WizardSeriesTag
                };
                model.Series.Add(area);
            }

            area.Points.Add(new DataPoint(curve[i].X, clamped));
            area.Points2.Add(target[i]);
        }
    }

    // Log-domain interpolation, matching the plot's log X axis.
    private static double InterpolateLogX(double x0, double x1, double f)
    {
        if (x0 > 0 && x1 > 0)
        {
            return Math.Exp(Math.Log(x0) + f * (Math.Log(x1) - Math.Log(x0)));
        }

        return x0 + f * (x1 - x0);
    }

    private void InitializeBandsLimitComboBox()
    {
        comboBoxBandsLimit.Items.Clear();
        for (int count = MinAutoTuneBandLimit; count <= MaxPeqSlotCount; count++)
        {
            comboBoxBandsLimit.Items.Add(count);
        }

        comboBoxBandsLimit.SelectedIndex = comboBoxBandsLimit.Items.Count - 1;
    }

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
