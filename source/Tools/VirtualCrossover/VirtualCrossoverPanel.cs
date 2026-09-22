using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze;

/// <summary>
/// Virtual DSP: measured transfer IRs run through per-channel DSP chains and summed as complex
/// responses, predicting the combined output. See docs/tech/virtual-dsp-panel.md.
/// </summary>
public partial class VirtualCrossoverPanel : UserControl
{
    private const int SaveDebounceMilliseconds = 2_000;

    private readonly System.Windows.Forms.Timer saveTimer = new()
    {
        Interval = SaveDebounceMilliseconds
    };

    private readonly VirtualCrossoverSession session = new();
    private readonly VirtualCrossoverHybrid hybridReader;
    private readonly VirtualCrossoverWarnings warnings;
    private readonly AcousticViewBuilder viewBuilder;
    private readonly AgentSessionReader agentReader;
    private readonly VirtualCrossoverAudition audition;
    private readonly VirtualCrossoverSideLock sideLock = new();

    // Bumped by every bind; lets an EQ Wizard handoff refuse to return into a replaced project.
    private long projectGeneration;

    private readonly VirtualCrossoverProcessingCoordinator processingCoordinator = new();
    private readonly VirtualCrossoverMetrics metrics;
    private readonly WrappingToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 12_000,
        ShowAlways = true
    };

    // The Auto commands read this and stay disabled until the redraw that fills it settles.
    private GatePlacementVerdict? gatePlacement;
    private VirtualCrossoverAcousticPlot acousticPlot = null!;
    private VirtualCrossoverDspChainPlot dspChainPlot = null!;
    private bool suppressProjectEvents;

    // Single-flight redraw; see docs/tech/virtual-dsp-panel.md#redraw-scheduling.
    private Task? redrawTask;
    private bool redrawPending;
    private bool savePending;
    private bool reportedSaveFailure;
    // Captured once: the toggle is recoloured as a reminder, which the muting helper would memorize.
    private Color hybridToggleColor;

    public VirtualCrossoverPanel()
    {
        InitializeComponent();
        hybridReader = new VirtualCrossoverHybrid(session);
        warnings = new VirtualCrossoverWarnings(session);
        viewBuilder = new AcousticViewBuilder(session, hybridReader);
        // While controls stand where the designer put them: the layout pass stretches plots by deltas on this.
        CaptureLayoutBaseline();
        Ui.ThemedScrollBars.Apply(channelListPanel);
        Ui.ThemedScrollBars.Apply(this);
        SetChannelCount(DefaultChannelCount);

        checkBoxShowSum.ForeColor = UiPalette.CurveNeutral;
        labelSumLoss.ForeColor = UiPalette.CurveTarget;
        targetToggleColor = checkBoxShowTarget.ForeColor;
        hybridToggleColor = checkBoxHybrid.ForeColor;

        metrics = VirtualCrossoverMetrics.Through(
            processingCoordinator,
            () => session.MagnitudeGate,
            oppositeSide: false,
            channel => session.Calibration.For(channel));
        agentReader = new AgentSessionReader(session, processingCoordinator, metrics, hybridReader);
        junctionTune = new VirtualCrossoverJunctionTuneApply(session, agentReader);
        audition = new VirtualCrossoverAudition(session, processingCoordinator, metrics, hybridReader);
        acousticPlot = new VirtualCrossoverAcousticPlot(
            mainPlotView, AcousticViewBuilder.NoSourcesHint, CurrentAcousticView());
        dspChainPlot = new VirtualCrossoverDspChainPlot(dspPlotView, CurrentDspPlotMode());
        mainPlotView.Paint += (_, _) => AppProfiler.FrameMark("vdsp-main");
        dspPlotView.Paint += (_, _) => AppProfiler.FrameMark("vdsp-dsp");
        InitializeGroupViewComboBox();
        InitializeSmoothingComboBox();
        InitializeSumLossComboBox();
        WirePanelEvents();
        InitializeToolTips();

        buttonAutoDelay.Click += (_, _) => AutoAlignDelay();
        buttonAi.Click += (_, _) => ShowAgentMenu();
        buttonAutoSetup.Click += (_, _) => OpenAutoSetupWizard();
        buttonTuneJunction.Click += async (_, _) => await ShowJunctionTuneDialogAsync().ConfigureAwait(true);
        buttonDspProcessor.Click += (_, _) => OpenDspProcessorDialog();
        buttonTools.Click += (_, _) => ShowToolsMenu();
        buttonExport.Click += async (_, _) => await ExportTuningSheetAsync();
        buttonPhaseGate.Click += async (_, _) => await OpenPhaseGateDialogAsync();
        buttonTargetSettings.Click += (_, _) => ShowTargetMenu();
        buttonSessionImport.Click += async (_, _) => await ImportSessionAsync();
        buttonSessionExport.Click += (_, _) => ExportSession();
        buttonAddChannel.Click += (_, _) => AddChannel();
        buttonRemoveChannel.Click += (_, _) => RemoveChannel();
        buttonResetChannels.Click += async (_, _) => await ResetChannelsAsync();
        buttonCopyLeftToRight.Click += (_, _) => CopySideSettings(fromRight: false);
        buttonCopyRightToLeft.Click += (_, _) => CopySideSettings(fromRight: true);
        checkBoxSideLock.CheckedChanged += (_, _) => OnSideLockChanged();
        // The designer ticks the box before this handler exists, so engage the lock by hand.
        OnSideLockChanged();

        saveTimer.Tick += (_, _) => FlushProject();
        // The designer file owns Dispose.
        Disposed += (_, _) =>
        {
            FlushProject();
            processingCoordinator.Dispose();
            saveTimer.Dispose();
            toolTip.Dispose();
        };
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal MeasurementHistoryService? HistoryService { get; set; }

    /// <summary>The tune this panel edits, for the host's tests and tools; the panel is its only writer.</summary>
    internal VirtualCrossoverSession Session => session;

    /// <summary>Saves the curve as a Captured FR overlay; returns the slot, null when all are taken.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<string, OverlayPoint[], int?>? OverlayCaptureRequested { get; set; }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<string, string>? MetricChanged { get; set; }

    /// <summary>Warning line for the host: text, tooltip, colour; empty text hides it.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<string, string, Color>? WarningChanged { get; set; }

    /// <summary>Raised when this tool's Target dialog edited the shared curve; the host writes it back to the EQ Wizard.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<EqTargetCurve>? TargetCurveChanged { get; set; }

    /// <summary>The result returns through <see cref="TryApplyPeqFromWizard"/>.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<VirtualDspEqHandoffRequest>? EditPeqInWizardRequested { get; set; }

    /// <summary>The kernel returns through <see cref="TryApplyFirFromConstructor"/>.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<FirConstructorHandoffRequest>? EditFirInConstructorRequested { get; set; }

    /// <summary>History entry id when it still exists, else the file path; at least one is non-null.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<Guid?, string?>? OpenSourceInAnalyzersRequested { get; set; }

    // The door every edit of the tune leaves by.
    private void SaveAndRedraw()
    {
        ScheduleSave();
        RedrawAll();
    }

    private void ScheduleSave()
    {
        // Every change passes through here, so the side lock reads here, ahead of the redraw and the save.
        sideLock.Follow(session.Channels.Select(channel => channel.Pair), session.ActiveSideRight);
        // Until the first show starts the stored load the project is the constructor's placeholder, and saving it would
        // replace the stored session: a panel built and disposed unseen, as tests and harnesses do, must write nothing.
        if (!initialized)
        {
            return;
        }

        savePending = true;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void FlushProject()
    {
        saveTimer.Stop();
        if (!savePending)
        {
            return;
        }

        savePending = false;
        try
        {
            session.Project.Save();
            reportedSaveFailure = false;
        }
        catch (Exception exception)
        {
            // Reported once per session: a debounced save failing silently lost whole tuning sessions.
            System.Diagnostics.Debug.WriteLine(
                $"Virtual DSP project save failed: {exception}");
            if (!reportedSaveFailure && !IsDisposed && IsHandleCreated)
            {
                reportedSaveFailure = true;
                ShowError(
                    "Virtual DSP settings are not being saved. Changes will be lost when " +
                    "the application closes.",
                    exception.Message);
            }
        }
    }

    private void WirePanelEvents()
    {
        checkBoxShowSum.CheckedChanged += (_, _) => OnViewChanged();
        checkBoxHybrid.CheckedChanged += (_, _) =>
        {
            // Its colour carries the unused-capture reminder, so it follows the tick, not only coverage.
            RefreshHybridAvailability();
            OnViewChanged();
        };
        checkBoxShowTarget.CheckedChanged += (_, _) => OnViewChanged();
        numericTargetLevel.ValueChanged += (_, _) => OnViewChanged();
        // Radios fire on check and uncheck; act only on the checked one.
        radioViewMagnitude.CheckedChanged += (_, _) =>
        {
            if (radioViewMagnitude.Checked) OnViewModeChanged();
        };
        radioViewPhase.CheckedChanged += (_, _) =>
        {
            if (radioViewPhase.Checked) OnViewModeChanged();
        };
        radioViewImpulse.CheckedChanged += (_, _) =>
        {
            if (radioViewImpulse.Checked) OnViewModeChanged();
        };
        radioViewGroupDelay.CheckedChanged += (_, _) =>
        {
            if (radioViewGroupDelay.Checked) OnViewModeChanged();
        };
        radioViewStep.CheckedChanged += (_, _) =>
        {
            if (radioViewStep.Checked) OnViewModeChanged();
        };
        comboBoxSmoothing.SelectedIndexChanged += (_, _) => OnViewChanged();
        comboBoxSumLoss.SelectedIndexChanged += (_, _) => OnViewChanged();
        comboBoxGroupView.SelectedIndexChanged += (_, _) =>
        {
            // Groups is magnitude-only (no group phase/impulse): move the view radio visibly instead of falling back.
            if (VirtualCrossoverGroupViews.DrawsGroupSums(SelectedGroupView) &&
                !radioViewMagnitude.Checked)
            {
                radioViewMagnitude.Checked = true;
            }

            UpdateViewDependentControls();
            OnViewChanged();
        };
        comboBoxCalibration.SelectedIndexChanged += (_, _) => OnCalibrationChanged();
        // DSP-mode radios span two containers, so exclusivity is wired by hand. Clear the other container FIRST
        // so OnDspPlotModeChanged never sees two checked; the cleared radios' handlers ignore Checked == false.
        radioDspMagnitude.CheckedChanged += (_, _) =>
        {
            if (radioDspMagnitude.Checked) OnChainDspModeChecked();
        };
        radioDspPhase.CheckedChanged += (_, _) =>
        {
            if (radioDspPhase.Checked) OnChainDspModeChecked();
        };
        radioDspCorrelation.CheckedChanged += (_, _) =>
        {
            if (radioDspCorrelation.Checked)
            {
                radioDspMagnitude.Checked = false;
                radioDspPhase.Checked = false;
                radioDspGroupDelay.Checked = false;
                OnDspPlotModeChanged();
            }
        };
        // Coherence shares Correlation's container; only the chain trio needs clearing.
        radioDspCoherence.CheckedChanged += (_, _) =>
        {
            if (radioDspCoherence.Checked)
            {
                radioDspMagnitude.Checked = false;
                radioDspPhase.Checked = false;
                radioDspGroupDelay.Checked = false;
                OnDspPlotModeChanged();
            }
        };
        comboBoxCorrelationPair.SelectedIndexChanged +=
            (_, _) => OnCorrelationPairChanged();
        radioDspGroupDelay.CheckedChanged += (_, _) =>
        {
            if (radioDspGroupDelay.Checked) OnChainDspModeChecked();
        };
        radioSideRight.CheckedChanged += (_, _) => OnActiveSideChanged();
    }

    private void RedrawAll()
    {
        using var _ = AppProfiler.Zone("VirtualDSP.RedrawAll");
        if (suppressProjectEvents)
        {
            return;
        }

        // Cheap and idempotent: lets a rate that moved with the measurements reach the blocks.
        RefreshProcessorRowAvailability();
        RequestRedraw();
    }

    // UI thread only, so the flag and task handle need no synchronization.
    private void RequestRedraw()
    {
        // The one UI-thread place to refresh the snapshot the worker builds read.
        session.MagnitudeGate = session.Gate.MagnitudeGate(
            session.GateFor(!session.ActiveSideRight).StoredOffsetMs,
            comboBoxSmoothing.SelectedItem is int smoothing ? smoothing : 12);

        // A running FFT may finish, but the coordinator will neither cache nor publish it.
        processingCoordinator.Invalidate();
        if (redrawTask is { IsCompleted: false })
        {
            redrawPending = true;
            return;
        }

        redrawTask = RunRedrawLoopAsync();
        RefreshAutoActionsEnabled();
    }

    // Auto crossover/delay read the processed set, so they wait until the panel settles.
    private void RefreshAutoActionsEnabled()
    {
        if (IsDisposed)
        {
            return;
        }

        bool busy = loadingProject
            || pendingSourceLoads > 0
            || redrawTask is { IsCompleted: false };
        buttonAutoSetup.Enabled = !busy;
        buttonTuneJunction.Enabled = !busy;
        buttonAutoDelay.Enabled = !busy;
        // Gathered at one revision; an import would be overwritten by a load in progress.
        buttonAi.Enabled = !busy && !agentBusy;
        // Starting mid-redraw would race the invalidation and render nothing.
        buttonTools.Enabled = !busy;
    }

    // Revision, cancellation and cache ownership live in the coordinator; this only applies results to OxyPlot.
    private async Task RunRedrawLoopAsync()
    {
        do
        {
            redrawPending = false;
            try
            {
                await RedrawMainPlotAsync();
                if (!mainPlotView.IsDisposed)
                {
                    RedrawDspPlot();
                }
            }
            catch (Exception exception)
            {
                // Best-effort: keep the last good frame.
                System.Diagnostics.Debug.WriteLine(
                    $"Virtual DSP redraw failed: {exception}");
            }
        }
        while (redrawPending && !mainPlotView.IsDisposed);

        redrawTask = null;
        RefreshAutoActionsEnabled();
    }

    private sealed record ProcessedRender(
        long Revision,
        List<ProcessedChannel> Channels);

    // The coordinator never reads controls or mutable settings after this awaits (snapshots are copies).
    private async Task<ProcessedRender?> ProcessChannelsAsync()
    {
        // Tracy zones are thread-bound LIFO: no zone may span an await.
        long revision = processingCoordinator.CurrentRevision;
        var snapshots = new List<VirtualCrossoverChannelSnapshot>();
        var bindings = new Dictionary<
            int,
            (VirtualCrossoverChannel Channel,
                OxyColor Color,
                MeasuredBand Band,
                CalibrationFile? OwnCalibration)>();
        using (AppProfiler.Zone("VirtualDSP.SnapshotChannels"))
        {
            for (int i = 0; i < session.Channels.Count; i++)
            {
                VirtualCrossoverChannel channel = session.Channels[i];
                VirtualCrossoverChannelState state = channel.SideState(channel.ActiveRight);
                if (!channel.Pair.Enabled ||
                    state.ProcessingSource is not { } source)
                {
                    continue;
                }

                DspChannelChain chain = channel.Pair.Bypass
                    ? DspChannelChain.Identity
                    : channel.Settings.ToChain(channel.Pair.Zone);
                snapshots.Add(new VirtualCrossoverChannelSnapshot(
                    i,
                    new ProcessingSlotId(
                        i,
                        !channel.Pair.Mono && channel.ActiveRight),
                    source,
                    state.SampleRate,
                    session.ProcessorSampleRateHz,
                    chain));
                bindings.Add(
                    i,
                    (channel,
                        VirtualCrossoverColors.Channel(i),
                        state.MeasuredBand,
                        state.MicrophoneCalibrationCurve));
            }
        }

        VirtualCrossoverRenderResult? render =
            await processingCoordinator.ProcessAsync(
                new VirtualCrossoverProcessingSnapshot(revision, snapshots));
        if (render == null)
        {
            return null;
        }

        var processed = new List<ProcessedChannel>(render.Channels.Count);
        foreach (VirtualCrossoverProcessedChannel result in render.Channels)
        {
            (VirtualCrossoverChannel channel,
                OxyColor color,
                MeasuredBand band,
                CalibrationFile? ownCalibration) = bindings[result.Id];
            processed.Add(new ProcessedChannel(
                channel,
                result.ImpulseResponse,
                result.PeakIndex,
                result.SampleRate,
                color,
                result.ValidRange,
                band,
                ownCalibration));
        }
        return new ProcessedRender(render.Revision, processed);
    }

    private ProcessedRender? lastProcessedRender;

    private void ShowError(string message, string details)
    {
        MessageBox.Show(
            FindForm(),
            $"{message}{Environment.NewLine}{Environment.NewLine}{details}",
            "Virtual DSP",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
