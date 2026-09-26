using System.ComponentModel;
using Resonalyze.Dsp;

namespace Resonalyze;

// The panel binds its controls to an EqWizardSession: handlers write the session and redraw; Present* methods write the
// controls back without raising their handlers. The rules live in the session and its readers (see EqWizardSession).
public partial class EqWizardPanel : UserControl
{
    private const int PeqColumnCount = 16;
    private const int PeqRowCount = 2;

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

    private readonly EqWizardSession session = new();
    private readonly EqWizardAutoTuneOrchestrator autoTuneOrchestrator = new();
    private readonly EqWizardImportExportCoordinator importExportCoordinator = new();
    private readonly EqWizardPlot plot = new();
    private readonly EqDoubleSkirtCheck.Cache doubleSkirtCheck = new();
    private PlotLabelsPanelController plotLabels = null!;
    // Set while the panel writes its own controls, so their handlers do not write the value back.
    private bool presenting;

    public EqWizardPanel()
    {
        InitializeComponent();
        // Before layout: the layout pass stretches the plot by deltas from the designed positions.
        CaptureLayoutBaseline();
        Ui.ThemedScrollBars.Apply(this);
        ApplyFieldRanges();
        InitializePlotWizard();
        InitializePeqSlotTable();
        InitializeBandsComboBox();
        InitializeBandsLimitComboBox();
        InitializeBoostsComboBox();
        InitializeSmoothComboBox();
        InitializeSampleRateComboBox();
        InitializeQConventionComboBox();
        // Open on Click and defer via BeginInvoke: a synchronous show is swallowed by the focus change, and on mouse-down
        // the click's own mouse-up closes the menu.
        buttonSource.Click += (_, _) => ShowSourceMenu();
        comboBoxCalibration.SelectedIndexChanged += (_, _) => OnCalibrationChanged();
        NumericTargetOffset.ValueChanged += (_, _) => OnTargetOffsetChanged();
        // The preamp is part of the bank's undo state.
        NumericGain.ValueChanged += (_, _) => PreampValueChanged();
        // What is shown, not what is fitted: a running Auto Tune stays valid.
        checkBoxBypass.CheckedChanged += (_, _) =>
        {
            if (!presenting)
            {
                session.SetBypass(checkBoxBypass.Checked);
                Redraw(orphanFit: false);
            }
        };
        checkBoxEqPhase.CheckedChanged += (_, _) =>
        {
            if (!presenting)
            {
                session.SetPhaseMode(checkBoxEqPhase.Checked);
                Redraw(orphanFit: false);
            }
        };
        checkBoxEqCurve.CheckedChanged += (_, _) =>
        {
            if (!presenting)
            {
                session.SetShowEqCurve(checkBoxEqCurve.Checked);
                Redraw(orphanFit: false);
            }
        };
        buttonPhaseGate.Click += (_, _) => OpenPhaseGateDialog();
        checkBoxShelves.CheckedChanged += (_, _) =>
        {
            if (!presenting)
            {
                autoTuneOrchestrator.Invalidate();
                session.SetAllowShelves(checkBoxShelves.Checked);
            }
        };
        checkBoxCrossoverTarget.CheckedChanged += (_, _) =>
        {
            if (!presenting)
            {
                autoTuneOrchestrator.Invalidate();
                session.SetCrossoverInTarget(checkBoxCrossoverTarget.Checked);
                PresentWindow();
                Redraw();
            }
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
            if (!presenting)
            {
                autoTuneOrchestrator.Invalidate();
                session.SetAutoTuneMaxQ(numericQMax.Value);
            }
        };
        Click += (_, _) => DeselectBand();
        panelPEQ.Click += (_, _) => DeselectBand();
        // A press that took a handle is not a click on empty graph.
        plotWizard.MouseDown += (_, _) => handlePressed = false;
        plotWizard.Click += (_, _) =>
        {
            if (!handlePressed)
            {
                DeselectBand();
            }
        };
        InitializeToolTips();
        PresentFitSettings();
        PresentCrossoverTarget();
        PresentViewSettings();
        PresentTargetOffset();
        PresentGainRange();
        Redraw();
    }

    /// <summary>The wizard's state, for the host and tests; the panel is its only writer from the UI.</summary>
    internal EqWizardSession Session => session;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<EqTuneStats?>? ResultsChanged { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<string?>? WarningChanged { get; set; }

    /// <summary>Raised after a change the settings file keeps (see <see cref="EqWizardSession.SettingsChanged"/>).</summary>
    internal event Action? SettingsChanged
    {
        add => session.SettingsChanged += value;
        remove => session.SettingsChanged -= value;
    }

    /// <summary>The Auto Tune settings on the controls, for a fit run elsewhere (AI import) that must match this button.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal EqAutoTunePolicy CurrentAutoTunePolicy => EqWizardFit.Policy(session);

    // The fields take their ranges from the limits the session holds its values to.
    private void ApplyFieldRanges()
    {
        NumericTargetOffset.ApplyFieldRange(EqWizardLimits.TargetOffset);
        NumericGain.ApplyFieldRange(EqWizardLimits.Preamp);
        numericFromHz.ApplyFieldRange(EqWizardLimits.WindowFrequency);
        numericToHz.ApplyFieldRange(EqWizardLimits.WindowFrequency);
        numericGainMin.ApplyFieldRange(EqWizardLimits.GainMinimum);
        numericGainMax.ApplyFieldRange(EqWizardLimits.GainMaximum);
        numericQMax.ApplyFieldRange(EqWizardLimits.AutoTuneMaxQ);
    }

    private void Present(Action write)
    {
        bool outer = presenting;
        presenting = true;
        try
        {
            write();
        }
        finally
        {
            presenting = outer;
        }
    }

    private void PresentFitSettings() => Present(() =>
    {
        numericFromHz.Value = session.WindowFromHz;
        numericToHz.Value = session.WindowToHz;
        numericGainMin.Value = session.GainMinDb;
        numericGainMax.Value = session.GainMaxDb;
        numericQMax.Value = session.AutoTuneMaxQ;
        comboBoxBoosts.SelectedIndex = Array.IndexOf(BoostChoices, session.Boosts);
        checkBoxShelves.Checked = session.AllowShelves;
        comboBoxBandsLimit.SelectedItem = session.BandLimit;
    });

    // Its own presenter: the box follows the SOURCE (only a chain with a crossover has one), not just the fit fields.
    private void PresentCrossoverTarget() => Present(() =>
    {
        checkBoxCrossoverTarget.Checked = session.CrossoverInTarget;
        checkBoxCrossoverTarget.Enabled = session.TargetCrossover != null;
    });

    private void PresentViewSettings() => Present(() =>
    {
        checkBoxBypass.Checked = session.Bypass;
        checkBoxEqPhase.Checked = session.PhaseMode;
        checkBoxEqCurve.Checked = session.ShowEqCurve;
    });

    private void PresentWindow()
    {
        Present(() =>
        {
            numericFromHz.Value = session.WindowFromHz;
            numericToHz.Value = session.WindowToHz;
        });
        plot.ShowWindow(session);
        plotWizard.InvalidatePlot(false);
    }

    private void GainBoundChanged(bool minChanged)
    {
        if (presenting)
        {
            return;
        }

        bool clamped = minChanged
            ? session.SetGainMin(numericGainMin.Value)
            : session.SetGainMax(numericGainMax.Value);
        PresentGainRange();
        // A clamped gain is an edit like a moved fader: it lands as its own undo step.
        if (clamped)
        {
            ArmBankEditTimer();
        }

        Redraw();
    }

    // Every strip's gain field follows Max Cut / Max Boost; the EQ axis's nominal range follows the budget.
    private void PresentGainRange()
    {
        Present(() =>
        {
            numericGainMin.Value = session.GainMinDb;
            numericGainMax.Value = session.GainMaxDb;
            for (int index = 0; index < Math.Min(peqSlots.Count, session.Bank.Bands.Count); index++)
            {
                peqSlots[index].SetGainRange(session.GainMinDb, session.GainMaxDb);
                WriteBand(peqSlots[index], session.Bank.Bands[index]);
            }
        });
        plot.RefreshEqAxis(session);
    }

    private void FrequencyBoundChanged(bool fromChanged)
    {
        if (presenting)
        {
            return;
        }

        if (fromChanged)
        {
            session.SetWindowFrom(numericFromHz.Value);
        }
        else
        {
            session.SetWindowTo(numericToHz.Value);
        }

        PresentWindow();
        Redraw();
    }

    private void InitializePlotWizard()
    {
        plot.ShowWindow(session);
        plotWizard.Model = plot.Model;
        plot.RefreshEqAxis(session);
        PlotInteraction.Enable(plotWizard);
        WireBandHandles();

        plotLabels = new PlotLabelsPanelController(plotWizard, () => Mode.EqWizard);
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
            "Auto Tune also keeps its boosts together under it.");
        SetTip(labelBandsLimit, comboBoxBandsLimit,
            "Maximum number of bands Auto Tune may create.");
        SetTip(labelQMax, numericQMax,
            "Narrowest band Auto Tune may place (the highest Q). Lower keeps the fit " +
            "on broad trends that hold across the seat, not a peak at one mic spot; " +
            "the strips themselves accept any Q up to 20.");
        SetTip(labelFromHz, numericFromHz,
            "Lower edge of the Auto Tune frequency window; also bounds the error metrics.");
        SetTip(labelToHz, numericToHz,
            "Upper edge of the Auto Tune frequency window; also bounds the error metrics.");
        SetTip(checkBoxEqCurve,
            "Draw the bank's own response — the white curve on the right-hand axis, " +
            "in dB here and in degrees in Phase. Turning it off leaves the plot to " +
            "the measurement and the target; the filters keep working either way. " +
            "The right-hand axis goes with it when nothing else is left on it.");
        SetTip(labelBoosts, comboBoxBoosts,
            "What Auto Tune may boost. Refill cuts: a boost only puts back what " +
            "its own cuts dug, so the EQ never rises above 0 dB. Off: cuts only. " +
            "Allowed: boosts fill dips too, outside narrow deep nulls.");
        SetTip(checkBoxCrossoverTarget,
            "Take the channel's own crossover into the target, so the fit follows " +
            "the filter's slope instead of stopping at the passband: the acoustic " +
            "roll-off is matched to the crossover you chose. Off leaves the slopes " +
            "alone. From / To follow the box while you have not typed them yourself.");
        SetTip(checkBoxShelves,
            "Let Auto Tune fit a low and a high shelf as well as bells; a shelf is " +
            "kept only where it beats the fit without one. With boosts Allowed a " +
            "shelf can lift a whole end past Max Gain — watch the headroom read-out.");
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

    // Applied to children too, so composite controls (ThemedComboBox, ThemedNumericUpDown) show it.
    private void SetTip(Control control, string text)
    {
        toolTip.SetToolTip(control, text);
        foreach (Control child in control.Controls)
        {
            SetTip(child, text);
        }
    }

    private int? SelectedBandIndex
    {
        get
        {
            int index = selectedSlot == null ? -1 : peqSlots.IndexOf(selectedSlot);
            return index >= 0 ? index : null;
        }
    }

    /// <summary>Redraws from the session. Any input change the user makes funnels through here, so it orphans a running fit.</summary>
    /// <param name="orphanFit">False for a redraw that changed no input of the fit: a landed render, a view toggle, a band
    /// selection. Orphaned, the fit runs on to its end and lands nothing.</param>
    private void Redraw(bool orphanFit = true)
    {
        // Every rate change funnels through a redraw, so the strips' GD readouts learn the rate here.
        foreach (PeqSlotControl slot in peqSlots)
        {
            slot.SampleRateHz = session.ProcessorSampleRateHz;
        }

        if (orphanFit)
        {
            autoTuneOrchestrator.Invalidate();
        }

        EqualizationCurve eq = EqWizardRender.DisplayedEq(session);
        // Not before the handle exists: a handoff installs while hidden, and a render landing in the creation pump draws
        // into a half-created control (see OnVisibleChanged).
        if (IsHandleCreated &&
            session.SourceCurve is { Points.Count: >= 2 } &&
            session.Previews.RequestGatedPreview(eq) is { } preview)
        {
            _ = RedrawAfterAsync(preview);
        }

        EqWizardRenderSet render = EqWizardRender.RenderSet(session, eq);
        buttonOverlaySettings.Enabled = true;
        NumericTargetOffset.Enabled = true;
        NumericGain.Enabled = !session.Bypass && render.SourcePlusEq != null;
        ResultsChanged?.Invoke(EqWizardRender.Stats(session, render, eq));
        WarningChanged?.Invoke(doubleSkirtCheck.Warning(
            session.Target.Spec,
            session.TargetCrossover,
            session.CrossoverInTarget,
            session.ProcessorSampleRateHz));

        EqWizardPhaseCurves? phase = null;
        if (session.PhaseMode && session.PhaseContext != null)
        {
            phase = session.Previews.PhaseCurves();
            if (IsHandleCreated && session.Previews.RequestPhaseCurve(eq) is { } rendering)
            {
                _ = RedrawAfterAsync(rendering);
            }
        }

        plot.Draw(session, eq, render, SelectedBandIndex, phase);
        plotLabels.Refresh();
        plot.Model.InvalidatePlot(true);
    }

    // Shows what landed, and starts the render a dropped request or an invalidation left waiting.
    private async Task RedrawAfterAsync(Task rendering)
    {
        await rendering;
        if (!IsDisposed && IsHandleCreated)
        {
            Redraw(orphanFit: false);
        }
    }

    /// <summary>Gated previews wait for the handle (see <see cref="Redraw"/>); showing the panel creates it, so a source installed before that starts rendering here.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && IsHandleCreated && session.Source is { IsGated: true })
        {
            Redraw(orphanFit: false);
        }
    }

    // Worker thread: tuning up to 32 bands visibly freezes the UI.
    private async void AutoTune()
    {
        (EqWizardCurve? source, EqWizardCurve target) = EqWizardRender.FitCurves(session);
        if (source == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        // Locked bands stay without asking. The tuner replaces the rest, all-pass included; ask before discarding phase
        // work aligned by ear.
        IReadOnlyList<PeqBand> locked = EqWizardFit.LockedBands(session);
        IReadOnlyList<PeqBand> allPass = EqWizardFit.AllPassBands(session);
        bool keepAllPass = false;
        if (allPass.Count > 0)
        {
            DialogResult answer = MessageBox.Show(
                FindForm(),
                $"The bank holds {EqWizardFit.DescribeAllPassCount(allPass.Count)} the tuner " +
                "cannot fit and would replace." + Environment.NewLine +
                Environment.NewLine +
                "Keep them and tune the remaining slots around them?" +
                Environment.NewLine +
                (locked.Count == 0
                    ? "No replaces the whole bank with the fit."
                    : "No lets the fit replace them; locked filters stay either way."),
                "EQ Wizard",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                return;
            }

            keepAllPass = answer == DialogResult.Yes;
        }

        // Kept bands may leave no slots; say so rather than exceed Max Filters or replace the bank unasked.
        List<PeqBand> kept = [.. locked, .. keepAllPass ? allPass : []];
        int reserved = kept.Count;
        if (reserved > 0 && reserved >= session.BandLimit)
        {
            MessageBox.Show(
                FindForm(),
                $"Keeping {EqWizardFit.DescribeKeptCount(kept)} leaves no room under Max " +
                $"Filters ({session.BandLimit}), so there is nothing for the fit to " +
                "place." + Environment.NewLine + Environment.NewLine +
                (locked.Count > 0
                    ? "Raise Max Filters or unlock a filter."
                    : "Raise Max Filters, or run again and let the fit replace the bank."),
                "EQ Wizard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        List<SignalPoint> fitSource = EqWizardFit.FitSource(session, source, kept)
            .Select(point => new SignalPoint(point.X, point.Y))
            .ToList();
        List<SignalPoint> fitTarget = target.Points
            .Select(point => new SignalPoint(point.X, point.Y))
            .ToList();

        if (EqWizardFit.NoMeasuredDataRefusal(session, fitSource, fitTarget) is { } refusal)
        {
            MessageBox.Show(
                FindForm(),
                refusal,
                "EQ Wizard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string? levelWarning = EqWizardFit.LevelWarning(session, fitSource, fitTarget);
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
            EqWizardFit.Options(session, reserved),
            // Loopback γ² or mic-array agreement; without either, boosts fall back to null-detection.
            session.Source?.Coherence);

        // Inputs stay editable during the fit; any input change orphans the result (see Redraw).
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
        ApplyEqualizationCurve(EqWizardFit.Finish(tuned, kept));
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

        EqualizationCurve curve = session.Bank.Curve;
        EqWizardExportTarget target =
            importExportCoordinator.ResolveExportTarget(dialog.FilterIndex);
        // Formats that cannot state shelves, all-pass or a preamp would silently export a different tune; the user decides.
        if (!ConfirmExportLoss(EqExportWarnings.ShelvingBandsDropped(target, curve)) ||
            !ConfirmExportLoss(EqExportWarnings.AllPassBandsDropped(target, curve)) ||
            !ConfirmExportLoss(EqExportWarnings.PreampDropped(target, curve)))
        {
            return;
        }

        EqWizardFileResult result = importExportCoordinator.Export(
            EqWizardExportRequest.For(session, dialog.FileName, target));
        if (!result.Success)
        {
            ShowFileError("PEQ could not be exported.", result.Exception!);
        }
    }

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

    private void InitializeBandsLimitComboBox()
    {
        comboBoxBandsLimit.Items.Clear();
        for (int count = EqWizardLimits.MinAutoTuneBandLimit; count <= EqWizardLimits.MaxBands; count++)
        {
            comboBoxBandsLimit.Items.Add(count);
        }

        comboBoxBandsLimit.SelectedIndex = comboBoxBandsLimit.Items.Count - 1;
        comboBoxBandsLimit.SelectedIndexChanged += (_, _) =>
        {
            if (!presenting && comboBoxBandsLimit.SelectedItem is int limit)
            {
                // A fit under the old budget could land more filters than the field now allows.
                autoTuneOrchestrator.Invalidate();
                session.SetBandLimit(limit);
            }
        };
    }

    // In the combo's order; the text is what the box shows.
    private static readonly EqAutoTuneBoosts[] BoostChoices =
    {
        EqAutoTuneBoosts.Off,
        EqAutoTuneBoosts.RefillOwnCuts,
        EqAutoTuneBoosts.Allowed
    };

    private void InitializeBoostsComboBox()
    {
        comboBoxBoosts.Items.Clear();
        comboBoxBoosts.Items.AddRange(new object[] { "Off", "Refill cuts", "Allowed" });
        comboBoxBoosts.SelectedIndex = Array.IndexOf(BoostChoices, session.Boosts);
        comboBoxBoosts.SelectedIndexChanged += (_, _) =>
        {
            if (!presenting && comboBoxBoosts.SelectedIndex >= 0)
            {
                // Orphan any in-flight fit computed under the previous setting.
                autoTuneOrchestrator.Invalidate();
                session.SetBoosts(BoostChoices[comboBoxBoosts.SelectedIndex]);
            }
        };
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
            if (!presenting &&
                comboBoxSmooth.SelectedItem is int value &&
                session.SetSourceSmoothing(value))
            {
                Redraw();
            }
        };
        comboBoxSmooth.SelectedIndex = 0;
    }
}
