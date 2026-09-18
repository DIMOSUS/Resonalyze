using System.Numerics;
using System.Text;
using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.History;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>
/// Virtual DSP: measured transfer IRs run through per-channel DSP chains and summed as complex
/// responses, predicting the combined output. See docs/tech/virtual-dsp-panel.md.
/// </summary>
public partial class VirtualCrossoverPanel : UserControl
{
    private const int SaveDebounceMilliseconds = 2_000;

    // Min 2: the sum metric needs two channels; max = the project format's capacity.
    private const int MinChannelCount = 2;
    private const int MaxChannelCount = VirtualCrossoverProjectFile.MaximumChannelCount;
    private const int DefaultChannelCount = 3;

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

    private readonly EqWizardImportExportCoordinator peqExport = new();

    // Bumped by every bind; lets an EQ Wizard handoff refuse to return into a replaced project.
    private long projectGeneration;

    // VirtualCrossoverChannel is UI-free; only the binding methods look up controls.
    private readonly Dictionary<VirtualCrossoverChannel, VirtualCrossoverChannelControl>
        channelControls = new();
    private readonly VirtualCrossoverProcessingCoordinator processingCoordinator = new();
    private readonly VirtualCrossoverMetrics metrics;
    private readonly WrappingToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 12_000,
        ShowAlways = true
    };

    // Describes the sheet being printed, not the project, so it is session state.
    private PeqQConvention? sheetQConvention;
    // The Auto commands read this and stay disabled until the redraw that fills it settles.
    private GatePlacementVerdict? gatePlacement;
    private VirtualCrossoverAcousticPlot acousticPlot = null!;
    private VirtualCrossoverDspChainPlot dspChainPlot = null!;
    private bool initialized;

    // Awaited by anything that replaces the project; never faulted.
    private Task storedProjectLoad = Task.CompletedTask;
    private bool suppressProjectEvents;

    // Single-flight redraw; see docs/tech/virtual-dsp-panel.md#redraw-scheduling.
    private Task? redrawTask;
    private bool redrawPending;
    private bool savePending;
    private bool reportedSaveFailure;
    private bool loadingProject;
    private int pendingSourceLoads;

    private EqTargetCurve? targetCurve;
    private ContextMenuStrip? targetMenu;

    // Kept so a toggle muted for a view has its live colour to return to.
    private Color targetToggleColor;

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
        buttonDspProcessor.Click += (_, _) => OpenDspProcessorDialog();
        buttonCaptureOverlay.Click += async (_, _) => await CaptureSumToOverlayAsync();
        buttonExport.Click += async (_, _) => await ExportTuningSheetAsync();
        buttonPhaseGate.Click += async (_, _) => await OpenPhaseGateDialogAsync();
        buttonTargetSettings.Click += (_, _) => ShowTargetMenu();
        buttonSessionImport.Click += async (_, _) => await ImportSessionAsync();
        buttonSessionExport.Click += (_, _) => ExportSession();
        buttonAudition.Click += async (_, _) => await AuditionTrackAsync();
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

    private Func<string?, CalibrationFile?>? calibrationResolver;
    private IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries = [];

    private Func<VirtualCrossoverSessionCalibration, string?>? calibrationAdder;

    // A curve the bound project carries that no configured entry matches; offered as its own item.
    private VirtualCrossoverSessionCalibration? sessionCalibration;

    private VirtualCrossoverCalibrationNotice pendingCalibrationNotice;

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

    /// <summary>Shared EQ target pushed by the host; an equal value is ignored (no redraw).</summary>
    internal void SetTargetCurve(EqTargetCurve value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (targetCurve == value)
        {
            return;
        }

        targetCurve = value;
        targetToggleColor = value.Color;
        StoreTargetInProject(value);
        UpdateTargetToggleLook();
        if (checkBoxShowTarget.Checked && radioViewMagnitude.Checked)
        {
            RedrawAll();
        }
    }

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

    /// <summary>Called whenever the tab becomes active; the first call loads the saved project.</summary>
    internal void OnPanelShown()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        storedProjectLoad = LoadProjectSafelyAsync();
    }

    // Kept as a task so an import arriving right after can await it.
    private async Task LoadProjectSafelyAsync()
    {
        try
        {
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadOrDefault();
            await ApplyProjectAsync(loaded, imported: false);
            NotifyIfProjectBackedUp(loaded.BackupNoticePath);
            NotifyIfMigrationCostAFilter(loaded.MigrationNoticeText);
        }
        catch (Exception exception)
        {
            // Silently opening on defaults would invite re-tuning over a discarded project.
            System.Diagnostics.Debug.WriteLine(
                $"Virtual DSP project load failed: {exception}");
            if (!IsDisposed && IsHandleCreated)
            {
                ShowError(
                    "The saved Virtual DSP project could not be loaded, so the tool " +
                    "opened with defaults. The file on disk has not been changed.",
                    exception.Message);
            }
        }
    }

    // A migration that dropped a filter is announced at load, before the next save makes it the file.
    private void NotifyIfMigrationCostAFilter(string? notice)
    {
        if (notice == null || IsDisposed)
        {
            return;
        }

        MessageBox.Show(
            this,
            notice,
            "Virtual DSP",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void NotifyIfProjectBackedUp(string? backupPath)
    {
        if (backupPath == null || IsDisposed)
        {
            return;
        }

        MessageBox.Show(
            this,
            "The saved Virtual DSP session could not be opened, so it was moved " +
            $"aside to:\r\n\r\n{backupPath}\r\n\r\nA fresh session was started; your " +
            "previous file is preserved there for recovery.",
            "Virtual DSP",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // Re-resolving sources takes seconds. The whole tree is disabled because a load rebuilds the blocks.
    private void SetProjectLoading(bool loading)
    {
        if (IsDisposed)
        {
            return;
        }

        loadingProject = loading;
        UseWaitCursor = loading;
        Enabled = !loading;
        if (loading)
        {
            acousticPlot.ShowHint(AcousticViewBuilder.LoadingHint);
            MetricChanged?.Invoke("Loading\r\nsession…", string.Empty);
        }
    }

    // imported: a session file may carry calibration ids from another machine.
    private async Task ApplyProjectAsync(VirtualCrossoverProjectFile newProject, bool imported)
    {
        SetProjectLoading(true);
        try
        {
            await BindProjectAsync(newProject, imported);
        }
        finally
        {
            // Before the redraw, so the final frame is the real plot, not the loading note.
            SetProjectLoading(false);
            RedrawAll();
        }
    }

    private async Task BindProjectAsync(VirtualCrossoverProjectFile newProject, bool imported)
    {
        // Read before the swap: a legacy session naming an unknown calibration keeps the panel's selection.
        string? previousCalibrationId =
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration);
        VirtualCrossoverSessionCalibration? previousSession = sessionCalibration;
        session.Project = newProject;
        session.RelinkDirectory = null;
        // The previous import's undo would restore into settings nobody displays.
        agentUndo = null;
        // Channel objects are reused across binds; this tells an EQ Wizard handoff which project it came from.
        projectGeneration++;
        SetChannelCount(session.Project.Pairs.Count);

        suppressProjectEvents = true;
        try
        {
            comboBoxSumLoss.SelectedItem = session.Project.SumLossWindowMode;
            // Intent only: captures attach as sources resolve, and HybridRequested also needs coverage.
            checkBoxHybrid.Checked = session.Project.ShowHybridCurves;
            checkBoxShowTarget.Checked = session.Project.ShowTargetCurve;
            numericTargetLevel.Value =
                numericTargetLevel.ClampValue(session.Project.TargetLevelDb);
            // Each newer view flag is written beside the older one it falls back to.
            radioViewStep.Checked = session.Project.ShowStepView;
            radioViewImpulse.Checked =
                !session.Project.ShowStepView && session.Project.ShowImpulseView;
            radioViewGroupDelay.Checked =
                !session.Project.ShowStepView && !session.Project.ShowImpulseView &&
                session.Project.ShowGroupDelayView;
            radioViewPhase.Checked =
                !session.Project.ShowStepView && !session.Project.ShowImpulseView &&
                !session.Project.ShowGroupDelayView && session.Project.ShowPhaseView;
            radioViewMagnitude.Checked =
                !session.Project.ShowStepView && !session.Project.ShowImpulseView &&
                !session.Project.ShowGroupDelayView && !session.Project.ShowPhaseView;
            // After the radios: the Sum toggle is remembered per view.
            ApplySumToggleForView();
            ApplyProjectTarget();
            radioSideRight.Checked = session.Project.ActiveSideRight;
            radioSideLeft.Checked = !session.Project.ActiveSideRight;
            acousticPlot.ConfigureForView(CurrentAcousticView());
            comboBoxSmoothing.SelectedItem =
                OverlaySmoothing.IsValid(session.Project.SmoothingCode)
                    ? session.Project.SmoothingCode
                    : 12;
            comboBoxGroupView.SelectedItem = session.Project.GroupView;
            if (VirtualCrossoverGroupViews.DrawsGroupSums(session.Project.GroupView))
            {
                radioViewMagnitude.Checked = true;
            }
            radioDspMagnitude.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.Magnitude;
            radioDspPhase.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.Phase;
            radioDspGroupDelay.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.GroupDelay;
            radioDspCorrelation.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.Correlation;
            radioDspCoherence.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.Coherence;
            comboBoxCorrelationPair.Enabled = JunctionPlotModeSelected() &&
                comboBoxCorrelationPair.Items.Count > 0;

            // Before filling blocks: it re-pins their height once instead of per block.
            RefreshProcessorRowAvailability();
            for (int i = 0; i < session.Channels.Count; i++)
            {
                session.Channels[i].Pair = session.Project.Pairs[i];
                session.Channels[i].ActiveRight = session.Project.ActiveSideRight;
                ApplySettingsToControl(session.Channels[i]);
            }
        }
        finally
        {
            suppressProjectEvents = false;
        }

        // Restart the lock from the loaded pairs, or the first edit is recorded as a starting state.
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));

        // Selector events were silenced above; refresh the view-muted controls from the landed state.
        UpdateViewDependentControls();

        BindCalibrationSelection(imported, previousCalibrationId, previousSession);

        await session.RestoreSourcesAsync(
            (channel, rightSide) => ResolveSourceAsync(channel, rightSide, showErrors: false),
            UpdateSourceButton);

        // After sources (an array brings one): settle the averaging method once and redraw the buttons drawn earlier.
        if (session.SettleSpatialAverageMode())
        {
            foreach (VirtualCrossoverChannel channel in session.Channels)
            {
                RefreshSpatialAverageStatus(channel);
            }

            RefreshHybridAvailability();
            ScheduleSave();
        }

        UpdateSideRadioTexts();
        // Again after sources: a project with no rate takes the measurements', and the phase read-out solves at it.
        RefreshProcessorRowAvailability();
    }

    // The door every edit of the tune leaves by.
    private void SaveAndRedraw()
    {
        ScheduleSave();
        RedrawAll();
    }

    private void ScheduleSave()
    {
        // Every change passes through here, so the side lock reads here, ahead of the redraw and the save.
        sideLock.Follow(session.Channels.Select(channel => channel.Pair), session.Project.ActiveSideRight);
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

    /// <summary>Called again whenever the configured calibrations change.</summary>
    internal void ConfigureCalibration(
        Func<string?, CalibrationFile?> resolver,
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        Func<VirtualCrossoverSessionCalibration, string?>? addToList = null)
    {
        calibrationResolver = resolver;
        calibrationEntries = entries;
        calibrationAdder = addToList ?? calibrationAdder;
        ReconcileCalibrationSelection();
    }

    // Selection stays (marked if gone); a session curve hands over to a configured entry holding the same
    // curve. The project's stored form follows.
    private void ReconcileCalibrationSelection()
    {
        string? selectedId = comboBoxCalibration.Items.Count > 0
            ? MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration)
            : session.Project.CalibrationId;
        if (sessionCalibration is { } carried && calibrationResolver is { } resolve)
        {
            MicrophoneCalibrationEntry? same = calibrationEntries.FirstOrDefault(entry =>
                entry.Available &&
                CalibrationFile.SameCurve(resolve(entry.Id), carried.Curve));
            if (same != null)
            {
                if (VirtualCrossoverCalibrationSelection.IsSession(selectedId))
                {
                    selectedId = same.Id;
                }

                sessionCalibration = null;
            }
        }

        ApplyCalibrationSelection(selectedId);
        // Only once initialized: before that the project is a placeholder and would overwrite the real autosave.
        if (PersistCalibrationSelection() && initialized)
        {
            ScheduleSave();
        }
    }

    // The project's curve decides, its id is a hint; an unresolved legacy id keeps the panel's choice.
    private void BindCalibrationSelection(
        bool imported,
        string? previousSelectedId,
        VirtualCrossoverSessionCalibration? previousSession)
    {
        Func<string?, CalibrationFile?> resolve = calibrationResolver ?? (_ => null);
        VirtualCrossoverCalibrationDecision decision =
            VirtualCrossoverCalibrationSelection.Resolve(
                session.Project.CalibrationId,
                session.Project.Calibration,
                imported,
                calibrationEntries,
                resolve,
                previousSelectedId,
                previousSession);
        sessionCalibration = decision.Session;
        pendingCalibrationNotice = decision.Notice;
        ApplyCalibrationSelection(decision.SelectedId);
        PersistCalibrationSelection();
    }

    // An entry no longer configured stays, marked, so the stored preference survives the rebuild.
    private void ApplyCalibrationSelection(string? selectedId)
    {
        suppressProjectEvents = true;
        try
        {
            MicrophoneCalibrationComboHelper.Configure(
                comboBoxCalibration,
                selectedId,
                CalibrationEntriesWithSession());
        }
        finally
        {
            suppressProjectEvents = false;
        }

        ResolveCalibration();
        RedrawAll();
    }

    private IReadOnlyList<MicrophoneCalibrationEntry> CalibrationEntriesWithSession() =>
        VirtualCrossoverCalibrationSelection.EntriesWith(calibrationEntries, sessionCalibration);

    private CalibrationFile? ResolveSelectedCalibration(string? calibrationId) =>
        VirtualCrossoverCalibrationSelection.IsSession(calibrationId)
            ? sessionCalibration?.Curve
            : calibrationResolver?.Invoke(calibrationId);

    // The name is the entry's, not an id's: the session's own curve has no id the wizard could resolve.
    private void ResolveCalibration()
    {
        string? selected =
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration);
        bool own = VirtualCrossoverCalibrationSelection.IsOwn(selected);
        session.Calibration = new VirtualCrossoverCalibrationPolicy(
            own,
            own ? null : ResolveSelectedCalibration(selected),
            selected == null
                ? null
                : CalibrationEntriesWithSession()
                    .FirstOrDefault(entry =>
                        string.Equals(entry.Id, selected, StringComparison.OrdinalIgnoreCase))
                    ?.Name);
    }

    // Resolved through the same path as the curves, so the file carries what the plot shows. True when changed.
    private bool PersistCalibrationSelection()
    {
        (string? id, VirtualCrossoverCalibrationSettings? calibration) =
            VirtualCrossoverCalibrationSelection.Persist(
                MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration),
                sessionCalibration,
                calibrationEntries,
                calibrationResolver ?? (_ => null),
                session.Project.CalibrationId,
                session.Project.Calibration);
        bool changed =
            !string.Equals(id, session.Project.CalibrationId, StringComparison.OrdinalIgnoreCase) ||
            !SameStoredCurve(calibration, session.Project.Calibration);
        session.Project.CalibrationId = id;
        session.Project.Calibration = calibration;
        return changed;
    }

    private static bool SameStoredCurve(
        VirtualCrossoverCalibrationSettings? left,
        VirtualCrossoverCalibrationSettings? right) =>
        ReferenceEquals(left, right) ||
        (left != null && right != null &&
            string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
            string.Equals(left.FileName, right.FileName, StringComparison.Ordinal) &&
            CalibrationFile.SameCurve(left.ToCalibrationFile(), right.ToCalibrationFile()));

    private void OnCalibrationChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        PersistCalibrationSelection();
        ResolveCalibration();
        SaveAndRedraw();
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

    // Each side keeps its own processed-IR cache, so switching is cheap.
    private void OnActiveSideChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        bool rightSide = radioSideRight.Checked;
        session.Project.ActiveSideRight = rightSide;
        suppressProjectEvents = true;
        try
        {
            foreach (VirtualCrossoverChannel channel in session.Channels)
            {
                channel.ActiveRight = rightSide;
                ApplySettingsToControl(channel);
            }
        }
        finally
        {
            suppressProjectEvents = false;
        }

        UpdateSideRadioTexts();
        SaveAndRedraw();
    }

    // The source is never copied (each side has its own measurement); mono pairs are not offered.
    private void CopySideSettings(bool fromRight)
    {
        List<VirtualCrossoverChannel> candidates = session.Channels
            .Where(channel => !channel.Pair.Mono)
            .ToList();
        if (candidates.Count == 0)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        List<string> labels = candidates
            .Select(channel =>
            {
                string source = channel.SideSettings(fromRight).DisplayName;
                return string.IsNullOrWhiteSpace(source)
                    ? channel.Name
                    : $"{channel.Name} — {source}";
            })
            .ToList();
        using var dialog = new VirtualCrossoverCopySideDialog(fromRight, labels);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.SelectedIndices.Count == 0 ||
            dialog.Scope.IsEmpty)
        {
            return;
        }

        VirtualCrossoverCopyScope scope = dialog.Scope;
        bool targetSideShown = session.Project.ActiveSideRight == !fromRight;
        foreach (int index in dialog.SelectedIndices)
        {
            VirtualCrossoverChannel channel = candidates[index];
            scope.Copy(channel.SideSettings(fromRight), channel.SideSettings(!fromRight));
            if (targetSideShown)
            {
                ApplySettingsToControl(channel);
            }
        }

        SaveAndRedraw();
    }

    // Engaging copies nothing; see docs/tech/virtual-dsp-panel.md#side-lock. On by default, not stored.
    private void OnSideLockChanged()
    {
        if (checkBoxSideLock.Checked)
        {
            sideLock.Engage(session.Channels.Select(channel => channel.Pair));
        }
        else
        {
            sideLock.Release();
        }
    }

    // ● has at least one source, ○ none.
    private void UpdateSideRadioTexts()
    {
        bool leftAny = session.Channels.Any(channel =>
            channel.SideState(false).TransferImpulseResponse != null);
        bool rightAny = session.Channels.Any(channel =>
            !channel.Pair.Mono &&
            channel.SideState(true).TransferImpulseResponse != null);
        radioSideLeft.Text = leftAny ? "L ●" : "L ○";
        radioSideRight.Text = rightAny ? "R ●" : "R ○";
    }

    private VirtualCrossoverChannel CreateChannel(int index)
    {
        // Keep the designer size (DPI-scaled); raw pixels would clip on high DPI.
        var control = new VirtualCrossoverChannelControl
        {
            BackColor = UiPalette.PanelSurface,
            Font = new Font("Segoe UI", 9F),
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(0, 0, 0, 6),
            ChannelName = ChannelNameFor(index),
            // Before joining the list: the rows change its height and a re-pin makes the list jump.
            PhaseControlShown = session.Project.ResolveDspPhaseControl(),
            FirControlShown = session.Project.ResolveDspFirFilters(),
            ProcessorSampleRateHz = session.ProcessorSampleRateHz
        };

        control.SetAccentColor(VirtualCrossoverColors.ChannelAccent(index));

        // Per block, not in the constructor: blocks added later need tooltips too.
        control.ApplyTooltips(toolTip);

        var channel = new VirtualCrossoverChannel(ChannelNameFor(index))
        {
            // Read on demand: the processor can change at any time.
            ProcessorSampleRateProvider = () => session.ProcessorSampleRateHz
        };
        channelControls[channel] = control;
        control.SettingsChanged += (_, _) => OnChannelSettingsChanged(channel);
        control.SourceClicked += (_, _) => ShowSourceMenu(channel);
        control.SpatialAverageClicked += (_, _) => ShowSpatialAverageMenu(channel);
        control.PeqMenuClicked += (_, _) => ShowPeqMenu(channel);
        control.FirClicked += (_, _) => ShowFirMenu(channel);
        control.CollapsedChanged += (_, _) => OnChannelCollapsedChanged(channel);
        control.MoveUpClicked += (_, _) => MoveChannel(channel, -1);
        control.MoveDownClicked += (_, _) => MoveChannel(channel, +1);
        return channel;
    }

    // The channel object moves with its resolved IRs; only position-derived letter, colour and pair order are rewritten.
    private void MoveChannel(VirtualCrossoverChannel channel, int delta)
    {
        int at = session.Channels.IndexOf(channel);
        int to = at + delta;
        if (at < 0 || to < 0 || to >= session.Channels.Count)
        {
            return;
        }

        var order = Enumerable.Range(0, session.Channels.Count).ToList();
        (order[at], order[to]) = (order[to], order[at]);
        ApplyChannelOrder(order);
        SaveAndRedraw();
    }

    /// <summary><c>order[newIndex]</c> is the block's current position.</summary>
    /// <remarks>The project's pairs are permuted by the same indices, not rebuilt from the channels: they are bound
    /// only once a project is applied. The pair list is the whole persisted order.</remarks>
    private void ApplyChannelOrder(IReadOnlyList<int> order)
    {
        List<VirtualCrossoverChannel> reordered =
            order.Select(index => session.Channels[index]).ToList();
        session.Channels.Clear();
        session.Channels.AddRange(reordered);
        if (session.Project.Pairs.Count == order.Count)
        {
            List<VirtualCrossoverChannelPairSettings> pairs =
                order.Select(index => session.Project.Pairs[index]).ToList();
            session.Project.Pairs.Clear();
            session.Project.Pairs.AddRange(pairs);
        }

        channelListPanel.SuspendLayout();
        for (int i = 0; i < session.Channels.Count; i++)
        {
            VirtualCrossoverChannel channel = session.Channels[i];
            VirtualCrossoverChannelControl control = ControlFor(channel);
            channel.Name = ChannelNameFor(i);
            control.ChannelName = channel.Name;
            control.SetAccentColor(VirtualCrossoverColors.ChannelAccent(i));
            channelListPanel.Controls.SetChildIndex(control, i);
        }

        channelListPanel.ResumeLayout(performLayout: true);
        UpdateChannelButtons();
    }

    private VirtualCrossoverChannelControl ControlFor(VirtualCrossoverChannel channel) =>
        channelControls[channel];

    // The processing rate keys the coordinator cache, so a change re-runs every channel.
    private void OpenDspProcessorDialog()
    {
        // The dialog gets the real measured rate, zero included, never a default.
        using var dialog = new DspProcessorDialog(
            session.ProcessorProfile,
            session.Project.DspProcessorRateFollowsMeasurements,
            session.MeasuredSampleRateHz ?? 0,
            session.Project.DspProcessorPhaseControl,
            session.Project.DspProcessorFirFilters)
        {
            Notes = session.Project.AiNotes
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        // Notes alone are a save, never a re-run.
        string? notes = dialog.Notes;
        bool notesChanged = !string.Equals(notes, session.Project.AiNotes, StringComparison.Ordinal);
        if (notesChanged)
        {
            session.Project.AiNotes = notes;
            ScheduleSave();
        }

        DspProcessorProfile profile = dialog.Profile;
        // Compare intent, not numbers: "follow measurements" equals 48 kHz only until they are replaced.
        bool follows = dialog.FollowsMeasurements;
        // Confirming stores the shown phase answer, so a later model change cannot remove a control in use.
        bool phaseControl = dialog.PhaseControl;
        bool phaseControlChanged = session.Project.DspProcessorPhaseControl != phaseControl;
        bool firFilters = dialog.FirFilters;
        bool firFiltersChanged = session.Project.DspProcessorFirFilters != firFilters;
        if (profile == session.ProcessorProfile && follows == session.Project.DspProcessorRateFollowsMeasurements &&
            !phaseControlChanged && !firFiltersChanged)
        {
            return;
        }

        session.Project.DspProcessorPhaseControl = phaseControl;
        session.Project.DspProcessorFirFilters = firFilters;
        session.Project.SetDspProcessor(profile, follows);
        // A device without phase control (or FIR) drops them: left in place they would bend curves with no field on screen.
        int clearedRotations = session.Project.ClearUnavailablePhaseRotations();
        int clearedFirFilters = session.Project.ClearUnavailableFirFilters();
        if (clearedRotations > 0 || clearedFirFilters > 0)
        {
            foreach (VirtualCrossoverChannel channel in session.Channels)
            {
                ApplySettingsToControl(channel);
            }
        }

        RefreshProcessorRowAvailability();

        SaveAndRedraw();
        var notices = new List<string>();
        if (clearedRotations > 0)
        {
            notices.Add(
                $"{clearedRotations} channel side" +
                (clearedRotations == 1 ? " had" : "s had") +
                " a phase rotation dialled in, and this processor has no such " +
                "control.\r\n\r\nThe angle" +
                (clearedRotations == 1 ? " was" : "s were") +
                " cleared: left in place it would go on bending the curves with " +
                "nothing on screen to explain it, and the tuning sheet would go on " +
                "naming a control this device does not have.");
        }
        if (clearedFirFilters > 0)
        {
            notices.Add(
                $"{clearedFirFilters} channel side" +
                (clearedFirFilters == 1 ? " had" : "s had") +
                " a FIR filter loaded, and this processor has no FIR stage.\r\n\r\n" +
                "The kernel" + (clearedFirFilters == 1 ? " was" : "s were") +
                " detached: left in place it would go on shaping the curves with " +
                "nothing on screen to explain it, and the tuning sheet would go on " +
                "naming a file this device cannot take.");
        }
        if (notices.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join("\r\n\r\n", notices),
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static string ChannelNameFor(int index) =>
        VirtualCrossoverSheet.ChannelName(index);

    // Does not touch the project; callers own persistence.
    private void SetChannelCount(int count)
    {
        count = Math.Clamp(count, MinChannelCount, MaxChannelCount);

        while (session.Channels.Count > count)
        {
            VirtualCrossoverChannel removed = session.Channels[^1];
            // Invalidate BEFORE detaching: a pending source load then refuses to write back into the disposed control.
            removed.Invalidate();
            session.Channels.RemoveAt(session.Channels.Count - 1);
            VirtualCrossoverChannelControl control = ControlFor(removed);
            channelControls.Remove(removed);
            channelListPanel.Controls.Remove(control);
            control.Dispose();
        }

        while (session.Channels.Count < count)
        {
            VirtualCrossoverChannel added = CreateChannel(session.Channels.Count);
            session.Channels.Add(added);
            channelListPanel.Controls.Add(ControlFor(added));
        }

        UpdateChannelButtons();
    }

    private void UpdateChannelButtons()
    {
        buttonAddChannel.Enabled = session.Channels.Count < MaxChannelCount;
        buttonRemoveChannel.Enabled = session.Channels.Count > MinChannelCount;
        for (int i = 0; i < session.Channels.Count; i++)
        {
            ControlFor(session.Channels[i]).SetMoveAvailability(i > 0, i < session.Channels.Count - 1);
        }
    }

    private void AddChannel()
    {
        if (session.Channels.Count >= MaxChannelCount)
        {
            return;
        }

        var pair = new VirtualCrossoverChannelPairSettings();
        session.Project.Pairs.Add(pair);
        SetChannelCount(session.Channels.Count + 1);
        VirtualCrossoverChannel added = session.Channels[^1];
        added.Pair = pair;
        added.ActiveRight = session.Project.ActiveSideRight;
        ApplySettingsToControl(added);

        SaveAndRedraw();
    }

    private void RemoveChannel()
    {
        if (session.Channels.Count <= MinChannelCount)
        {
            return;
        }

        SetChannelCount(session.Channels.Count - 1);
        if (session.Project.Pairs.Count > session.Channels.Count)
        {
            session.Project.Pairs.RemoveRange(
                session.Channels.Count, session.Project.Pairs.Count - session.Channels.Count);
        }

        SaveAndRedraw();
    }

    /// <summary>Resets to first-run defaults by binding a fresh project (one definition of "default").</summary>
    /// <remarks>The selected calibration and the shared target curve survive; see docs/tech/virtual-dsp-panel.md#reset.</remarks>
    private async Task ResetChannelsAsync()
    {
        // Wait for the load, or the reset binds a project the pending one replaces.
        await storedProjectLoad;
        if (IsDisposed)
        {
            return;
        }

        if (MessageBox.Show(
                FindForm(),
                "Reset every channel block to its default: the measurements come " +
                "off, the crossovers, delays, gains, polarity, PEQ banks and zones " +
                "go back to their defaults, and the list returns to " +
                $"{DefaultChannelCount} blocks." +
                Environment.NewLine + Environment.NewLine +
                "The panel's own settings go with them — target level, smoothing, " +
                "the gate, the Show view and the stereo scene offset. The " +
                "microphone calibration and the shared EQ target curve stay." +
                Environment.NewLine + Environment.NewLine +
                "The current session is copied to" + Environment.NewLine +
                VirtualCrossoverProjectFile.ResetBackupPath() +
                Environment.NewLine +
                "first, so Load session… brings it back. That copy is overwritten " +
                "by the next reset — for a tune worth keeping, export it with Save " +
                "session first." +
                Environment.NewLine + Environment.NewLine +
                "Reset now?",
                "Virtual DSP",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        // Back up the project in memory (the autosave lags a debounce), and only after the question.
        (_, string? backupError) = session.Project.SaveResetBackup();
        if (backupError != null && MessageBox.Show(
                FindForm(),
                "The copy of the current session could not be written:" +
                Environment.NewLine + backupError +
                Environment.NewLine + Environment.NewLine +
                "Resetting now would discard the tune with nothing to bring it " +
                "back from. Reset anyway?",
                "Virtual DSP",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        await ApplyProjectAsync(new VirtualCrossoverProjectFile(), imported: false);
        // The autosave makes the reset the state the tool opens on.
        ScheduleSave();
    }

    // Layout only: persist, no recompute or redraw.
    private void OnChannelCollapsedChanged(VirtualCrossoverChannel channel)
    {
        if (suppressProjectEvents)
        {
            return;
        }

        channel.Pair.Collapsed = ControlFor(channel).Collapsed;
        ScheduleSave();
    }

    private void OnChannelSettingsChanged(VirtualCrossoverChannel channel)
    {
        if (suppressProjectEvents)
        {
            return;
        }

        // Zone belongs to the pair: store before the mono branch, which may repaint the old zone (Center forces Mono).
        channel.Pair.Zone = ControlFor(channel).SelectedZone;

        // A mono pair answers with the left side, so values read under the old binding must not be written through the new one.
        bool wasMono = channel.Pair.Mono;
        bool monoNow = ControlFor(channel).MonoCheckBox.Checked;
        if (wasMono != monoNow && channel.ActiveRight)
        {
            channel.Pair.Mono = monoNow;
            suppressProjectEvents = true;
            try
            {
                ApplySettingsToControl(channel);
            }
            finally
            {
                suppressProjectEvents = false;
            }
        }
        else
        {
            ReadControlIntoSettings(channel);
        }

        if (wasMono != monoNow)
        {
            if (monoNow)
            {
                // Drop the unreachable right runtime; the right settings survive for unchecking.
                channel.PhysicalSideState(true).Clear();
            }
            else
            {
                // Re-resolve through validation rather than resurfacing the slot's old cache.
                ReresolveRightSide(channel);
            }

            UpdateSideRadioTexts();
        }

        SaveAndRedraw();
    }

    // Guarded async void: called from a synchronous handler.
    private async void ReresolveRightSide(VirtualCrossoverChannel channel)
    {
        try
        {
            channel.PhysicalSideState(true).Clear();
            await ResolveSourceAsync(channel, rightSide: true, showErrors: false);
            // The channel or panel may be gone after the disk read.
            if (IsDisposed || !channelControls.ContainsKey(channel))
            {
                return;
            }

            UpdateSourceButton(channel);
            UpdateSideRadioTexts();
            RedrawAll();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Virtual DSP right-side re-resolve failed: {exception}");
        }
    }

    private void OnViewModeChanged()
    {
        acousticPlot.ConfigureForView(CurrentAcousticView());
        UpdateViewDependentControls();
        // Before the write-back, or OnViewChanged copies the old view's answer into the toggle.
        ApplySumToggleForView();
        OnViewChanged();
    }

    // Mutes toggles on views that cannot draw the curve; see docs/tech/virtual-dsp-panel.md#view-dependent-controls.
    private void UpdateViewDependentControls()
    {
        comboBoxSmoothing.Enabled = !radioViewImpulse.Checked && !radioViewStep.Checked;
        Ui.UiStyle.SetTextEnabledLook(
            checkBoxShowSum, !radioViewImpulse.Checked, interactive: true);
        // Uses intent, not Enabled: during a load Enabled reads false through the parent and the label would stay muted.
        bool lossQuoted =
            VirtualCrossoverGroupViews.LossChainZone(SelectedGroupView) != null;
        comboBoxSumLoss.Enabled = lossQuoted;
        Ui.UiStyle.SetTextEnabledLook(labelSumLoss, lossQuoted);
        bool groupSums = VirtualCrossoverGroupViews.DrawsGroupSums(SelectedGroupView);
        if (groupSums)
        {
            Ui.UiStyle.SetTextEnabledLook(checkBoxShowSum, false, interactive: true);
        }

        Ui.UiStyle.SetTextEnabledLook(radioViewPhase, !groupSums, interactive: true);
        Ui.UiStyle.SetTextEnabledLook(radioViewImpulse, !groupSums, interactive: true);
        Ui.UiStyle.SetTextEnabledLook(radioViewGroupDelay, !groupSums, interactive: true);
        Ui.UiStyle.SetTextEnabledLook(radioViewStep, !groupSums, interactive: true);
        RefreshHybridAvailability();
        UpdateTargetToggleLook();
        numericTargetLevel.Enabled = radioViewMagnitude.Checked;
    }

    // Disabled CheckBox text is near-black on this theme, so mute by hand; not via SetTextEnabledLook, which
    // memorizes a colour that follows the shared target.
    private void UpdateTargetToggleLook()
    {
        bool magnitude = radioViewMagnitude.Checked;
        checkBoxShowTarget.ForeColor =
            magnitude ? targetToggleColor : Ui.UiPalette.TextDisabled;
        checkBoxShowTarget.AutoCheck = magnitude;
        checkBoxShowTarget.TabStop = magnitude;
    }

    // One answer per view; the impulse view has no sum, writes nothing and shows the magnitude answer.
    private void ApplySumToggleForView()
    {
        bool suppressed = suppressProjectEvents;
        suppressProjectEvents = true;
        try
        {
            checkBoxShowSum.Checked = radioViewPhase.Checked
                ? session.Project.ShowSumCurveOnPhase
                : radioViewGroupDelay.Checked
                    ? session.Project.ShowSumCurveOnGroupDelay
                    : radioViewStep.Checked
                        ? session.Project.ShowSumCurveOnStep
                        : session.Project.ShowSumCurve;
        }
        finally
        {
            suppressProjectEvents = suppressed;
        }
    }

    private void OnViewChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        if (radioViewPhase.Checked)
        {
            session.Project.ShowSumCurveOnPhase = checkBoxShowSum.Checked;
        }
        else if (radioViewGroupDelay.Checked)
        {
            session.Project.ShowSumCurveOnGroupDelay = checkBoxShowSum.Checked;
        }
        else if (radioViewStep.Checked)
        {
            session.Project.ShowSumCurveOnStep = checkBoxShowSum.Checked;
        }
        else if (radioViewMagnitude.Checked)
        {
            session.Project.ShowSumCurve = checkBoxShowSum.Checked;
        }

        session.Project.SumLossWindowMode = SelectedSumLossWindow;
        session.Project.ShowHybridCurves = checkBoxHybrid.Checked;
        session.Project.ShowTargetCurve = checkBoxShowTarget.Checked;
        session.Project.TargetLevelDb = (double)numericTargetLevel.Value;
        // Newer view flags are written beside older ones so an older build opens the nearest view.
        session.Project.ShowPhaseView = radioViewPhase.Checked || radioViewGroupDelay.Checked;
        session.Project.ShowImpulseView = radioViewImpulse.Checked || radioViewStep.Checked;
        session.Project.ShowGroupDelayView = radioViewGroupDelay.Checked;
        session.Project.ShowStepView = radioViewStep.Checked;
        session.Project.SetSmoothingCode(comboBoxSmoothing.SelectedItem is int value
            ? value
            : 12);
        session.Project.GroupView = SelectedGroupView;
        SaveAndRedraw();
    }

    private void ApplySettingsToControl(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        VirtualCrossoverChannelControl control = ControlFor(channel);
        control.RunBatchUpdate(() =>
        {
            control.GainInput.Value = control.GainInput.ClampValue(settings.GainDb);
            control.DelayInput.Value = control.DelayInput.ClampValue(settings.DelayMs);
            control.InvertCheckBox.Checked = settings.InvertPolarity;
            control.CrossoverKindComboBox.SelectedItem = settings.CrossoverKind;
            // Family first: it repopulates the slope list.
            control.HighPassFamilyComboBox.SelectedItem = settings.HighPassEdge.Family;
            control.HighPassFrequencyInput.Value = control.HighPassFrequencyInput
                .ClampValue(settings.HighPassEdge.FrequencyHz);
            control.HighPassSlopeComboBox.SelectedItem = settings.HighPassEdge.SlopeDbPerOctave;
            control.HighPassRippleInput.Value = control.HighPassRippleInput
                .ClampValue(settings.HighPassEdge.RippleDb);
            control.LowPassFamilyComboBox.SelectedItem = settings.LowPassEdge.Family;
            control.LowPassFrequencyInput.Value = control.LowPassFrequencyInput
                .ClampValue(settings.LowPassEdge.FrequencyHz);
            control.LowPassSlopeComboBox.SelectedItem = settings.LowPassEdge.SlopeDbPerOctave;
            control.LowPassRippleInput.Value = control.LowPassRippleInput
                .ClampValue(settings.LowPassEdge.RippleDb);
            control.PhaseInput.Value = control.PhaseInput
                .ClampValue(settings.PhaseRotationDegrees);
            // Block-wide settings come off the pair, so they read the same on either side.
            control.ShowRawCheckBox.Checked = channel.Pair.ShowRawCurve;
            control.ShowProcessedCheckBox.Checked = channel.Pair.ShowProcessedCurve;
            control.BypassCheckBox.Checked = channel.Pair.Bypass;
            control.ZoneComboBox.SelectedItem = channel.Pair.Zone;
            control.MonoCheckBox.Checked = channel.Pair.Mono;
            control.Muted = !channel.Pair.Enabled;
            control.Collapsed = channel.Pair.Collapsed;
        });

        UpdateSourceButton(channel);
        UpdatePeqReadouts(channel);
        UpdateFirReadout(channel);
    }

    // Also runs on redraw (catches a rate that follows replaced measurements); only a real change reaches the layout.
    private void RefreshProcessorRowAvailability()
    {
        bool phaseShown = session.Project.ResolveDspPhaseControl();
        bool firShown = session.Project.ResolveDspFirFilters();
        int rate = session.ProcessorSampleRateHz;
        // Before the early return, so newly added or loaded pairs get the FIR rate too (EffectiveCrossover reads it).
        foreach (VirtualCrossoverChannel channel in session.Channels)
        {
            channel.Pair.Left.FirRunSampleRateHz = rate;
            channel.Pair.Right.FirRunSampleRateHz = rate;
        }

        bool changed = channelControls.Values.Any(control =>
            control.PhaseControlShown != phaseShown ||
            control.FirControlShown != firShown ||
            control.ProcessorSampleRateHz != rate);
        if (!changed)
        {
            return;
        }

        channelListPanel.SuspendLayout();
        foreach (VirtualCrossoverChannelControl control in channelControls.Values)
        {
            control.ProcessorSampleRateHz = rate;
            control.PhaseControlShown = phaseShown;
            control.FirControlShown = firShown;
        }

        channelListPanel.ResumeLayout(performLayout: true);
    }

    private void ReadControlIntoSettings(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        VirtualCrossoverChannelControl control = ControlFor(channel);
        settings.GainDb = (double)control.GainInput.Value;
        settings.DelayMs = (double)control.DelayInput.Value;
        settings.InvertPolarity = control.InvertCheckBox.Checked;
        settings.CrossoverKind = control.SelectedCrossoverKind;
        settings.HighPassEdge = control.HighPassEdge;
        settings.LowPassEdge = control.LowPassEdge;
        settings.PhaseRotationDegrees = (double)control.PhaseInput.Value;
        channel.Pair.ShowRawCurve = control.ShowRawCheckBox.Checked;
        channel.Pair.ShowProcessedCurve = control.ShowProcessedCheckBox.Checked;
        channel.Pair.Enabled = !control.Muted;
        channel.Pair.Bypass = control.BypassCheckBox.Checked;
        channel.Pair.Zone = control.SelectedZone;
        channel.Pair.Mono = control.MonoCheckBox.Checked;
    }

    private void ShowSourceMenu(VirtualCrossoverChannel channel)
    {
        var menu = new ContextMenuStrip();

        ToolStripMenuItem chooseFileItem = new("Choose file...");
        chooseFileItem.Click += async (_, _) => await ChooseSourceFileAsync(channel);
        menu.Items.Add(chooseFileItem);

        ToolStripMenuItem historyItem = new("History");
        PopulateHistoryMenu(historyItem, channel);
        menu.Items.Add(historyItem);

        menu.Items.Add(new ToolStripSeparator());

        // Enabled only when the reference still resolves.
        ToolStripMenuItem openItem = new("Open in analyzers");
        openItem.ToolTipText =
            "Load this side's measurement into the analysis modes\r\n" +
            "(lands on Frequency Response) — the full toolset on the\r\n" +
            "very measurement this channel is tuned on.";
        (Guid? entryId, string? filePath) = ResolveAnalyzerReference(channel.Settings);
        openItem.Enabled =
            OpenSourceInAnalyzersRequested != null && (entryId != null || filePath != null);
        openItem.Click += (_, _) =>
        {
            // Re-resolved at click time: the file can vanish while the menu is open.
            (Guid? id, string? path) = ResolveAnalyzerReference(channel.Settings);
            if (id != null || path != null)
            {
                OpenSourceInAnalyzersRequested?.Invoke(id, path);
            }
        };
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem clearItem = new("Clear");
        clearItem.Enabled = channel.Settings.HasSource;
        clearItem.Click += (_, _) => ClearSource(channel);
        menu.Items.Add(clearItem);

        DropDownMenu.ShowUnder(ControlFor(channel).SourceButton, menu);
    }

    // History entry first (survives file moves), else the located file path; (null, null) when nothing resolves.
    private (Guid? HistoryEntryId, string? FilePath) ResolveAnalyzerReference(
        VirtualCrossoverChannelSettings settings)
    {
        Guid? entryId =
            settings.HistoryEntryId is { } id && HistoryService?.FindById(id) != null
                ? id
                : null;
        return (entryId, session.Locate(settings.SourceFilePath, settings.SourceRelativePath));
    }

    private void PopulateHistoryMenu(ToolStripMenuItem historyItem, VirtualCrossoverChannel channel)
    {
        IReadOnlyList<MeasurementHistoryEntry> entries =
            HistoryService?.Entries ?? Array.Empty<MeasurementHistoryEntry>();
        if (entries.Count == 0)
        {
            historyItem.Enabled = false;
            return;
        }

        foreach (MeasurementHistoryEntry entry in entries)
        {
            ToolStripMenuItem entryItem = new(entry.FileNameOrDisplayName)
            {
                Tag = entry.Id
            };
            entryItem.Click += async (_, _) =>
            {
                if (entryItem.Tag is Guid entryId)
                {
                    await SelectHistoryEntryAsync(channel, entryId);
                }
            };
            historyItem.DropDownItems.Add(entryItem);
        }
    }

    private async Task ChooseSourceFileAsync(VirtualCrossoverChannel channel)
    {
        // Capture the concrete slot, settings and revision NOW: side, Mono or a session import can change during the load.
        // See docs/tech/virtual-dsp-panel.md#source-loading.
        bool rightSide = channel.ActiveRight;
        VirtualCrossoverChannelState targetState = channel.SideState(rightSide);
        VirtualCrossoverChannelSettings targetSettings = channel.SideSettings(rightSide);
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            RestoreDirectory = true,
            Title = $"Choose channel {channel.SideLabel(rightSide)} impulse response"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        int revision = targetState.BeginSourceLoad();
        pendingSourceLoads++;
        RefreshAutoActionsEnabled();
        try
        {
            ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(dialog.FileName);
            if (IsDisposed)
            {
                return;
            }

            MeasurementHistorySnapshot snapshot = MeasurementHistoryService.CreateSnapshot(file);
            if (TryAssignSource(targetState, revision, snapshot, SourceConflictPolicy.Prompt))
            {
                OnSourceAssigned(
                    channel,
                    targetSettings,
                    new VirtualCrossoverSourceReference(
                        Path.GetFileName(dialog.FileName),
                        dialog.FileName,
                        HistoryEntryId: null));
            }
        }
        catch (Exception exception)
        {
            ShowError("Failed to load the impulse response.", exception.Message);
        }
        finally
        {
            pendingSourceLoads--;
            RefreshAutoActionsEnabled();
        }
    }

    private async Task SelectHistoryEntryAsync(VirtualCrossoverChannel channel, Guid entryId)
    {
        bool rightSide = channel.ActiveRight;
        VirtualCrossoverChannelState targetState = channel.SideState(rightSide);
        VirtualCrossoverChannelSettings targetSettings = channel.SideSettings(rightSide);
        int revision = targetState.BeginSourceLoad();
        pendingSourceLoads++;
        RefreshAutoActionsEnabled();
        try
        {
            MeasurementHistoryEntry? entry = HistoryService?.FindById(entryId);
            MeasurementHistorySnapshot? snapshot = HistoryService == null
                ? null
                : await HistoryService.GetSnapshotAsync(entryId);
            if (entry == null || snapshot == null || IsDisposed)
            {
                return;
            }

            if (TryAssignSource(targetState, revision, snapshot, SourceConflictPolicy.Prompt))
            {
                OnSourceAssigned(
                    channel,
                    targetSettings,
                    new VirtualCrossoverSourceReference(
                        entry.FileNameOrDisplayName,
                        entry.SourceFilePath,
                        entryId));
            }
        }
        catch (Exception exception)
        {
            ShowError("Failed to load the history entry.", exception.Message);
        }
        finally
        {
            pendingSourceLoads--;
            RefreshAutoActionsEnabled();
        }
    }

    private enum SourceConflictPolicy
    {
        Prompt,
        RejectSilently
    }

    // Shared by interactive pickers and silent restore; only the conflict policy differs.
    // targetState is the caller's pre-await capture; the revision refuses a landing the slot has moved past.
    private bool TryAssignSource(
        VirtualCrossoverChannelState targetState,
        int sourceRevision,
        MeasurementHistorySnapshot snapshot,
        SourceConflictPolicy policy)
    {
        if (targetState.SourceRevision != sourceRevision)
        {
            return false;
        }

        if (ResolvedVirtualDspSource.FromSnapshot(snapshot) is not { } resolved)
        {
            if (policy == SourceConflictPolicy.Prompt)
            {
                ShowError(
                    "This measurement cannot be summed.",
                    "The virtual crossover sums loopback-referenced responses: it " +
                    "needs a transfer IR whose arrival is the tract's real delay. " +
                    "This one either has no transfer IR, or was imported from a " +
                    "recorded sweep and carries no absolute time. Re-measure with a " +
                    "loopback channel configured.");
            }

            return false;
        }

        // One sample rate per project: mixed rates are refused, checked against every resolved side.
        List<(VirtualCrossoverChannel Channel, bool RightSide, VirtualCrossoverChannelState State)> others =
            session.ResolvedSidesExcept(targetState).ToList();
        VirtualCrossoverSourceRules.Decision decision = VirtualCrossoverSourceRules.Evaluate(
            hasTransferIr: true,
            candidateSampleRate: resolved.SampleRate,
            otherResolvedSampleRates: others.Select(item => item.State.SampleRate));
        if (decision == VirtualCrossoverSourceRules.Decision.RejectSampleRateMismatch)
        {
            if (policy == SourceConflictPolicy.Prompt)
            {
                int projectSampleRate = others[0].State.SampleRate;
                ShowError(
                    $"This measurement is {resolved.SampleRate} Hz, but the project " +
                    $"already uses {projectSampleRate} Hz.",
                    "All channels in a Virtual DSP project must share one sample " +
                    "rate. Clear the existing channel sources first to switch the " +
                    "project to a different rate.");
            }

            return false;
        }

        resolved.ApplyTo(targetState);
        return true;
    }

    // A silent restore skips this: the reference is stored and the bind refreshes at the end.
    private void OnSourceAssigned(
        VirtualCrossoverChannel channel,
        VirtualCrossoverChannelSettings settings,
        VirtualCrossoverSourceReference reference)
    {
        reference.ApplyTo(settings);
        session.SettleSpatialAverageMode();
        UpdateSourceButton(channel);
        UpdateSideRadioTexts();
        SaveAndRedraw();
    }

    private void ClearSource(VirtualCrossoverChannel channel)
    {
        ClearSourceCore(channel, channel.ActiveRight);
        SaveAndRedraw();
    }

    private void ClearSourceCore(VirtualCrossoverChannel channel, bool rightSide)
    {
        channel.SideState(rightSide).Clear();
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        settings.DisplayName = string.Empty;
        settings.SourceFilePath = null;
        settings.HistoryEntryId = null;
        // Without a measurement the spatial average refines nothing; a kept reference would only warn.
        settings.SpatialAveragePath = null;
        settings.SpatialAverageRelativePath = null;
        UpdateSourceButton(channel);
        UpdateSideRadioTexts();
    }

    // History entry, then file path, then beside an imported session; a missing source leaves the side unresolved.
    private async Task ResolveSourceAsync(
        VirtualCrossoverChannel channel, bool rightSide, bool showErrors)
    {
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        // Before the measurement's early exit: an average can come back while the source is still missing.
        ResolveSpatialAverage(settings, state);
        if (!settings.HasSource)
        {
            return;
        }

        // Rapid mono toggles leave several resolves airborne; only the latest (uncleared) one may land.
        int revision = state.BeginSourceLoad();
        pendingSourceLoads++;
        RefreshAutoActionsEnabled();
        try
        {
            (MeasurementHistorySnapshot? snapshot, string? relocatedPath) =
                await LoadSnapshotFromReferenceAsync(settings);
            if (snapshot != null &&
                TryAssignSource(
                    state, revision, snapshot, SourceConflictPolicy.RejectSilently) &&
                relocatedPath != null)
            {
                // Pin the relocated path only if the measurement landed: a stored path always wins, so pinning a refused file
                // would stop the next relink from searching the user's folder.
                settings.SourceFilePath = relocatedPath;
            }
        }
        catch (Exception exception) when (!showErrors)
        {
            _ = exception;
        }
        finally
        {
            pendingSourceLoads--;
            RefreshAutoActionsEnabled();
        }
    }

    // RelocatedPath is pinned by the caller only on acceptance: the autosave has no session file beside it to search again.
    private async Task<(MeasurementHistorySnapshot? Snapshot, string? RelocatedPath)>
        LoadSnapshotFromReferenceAsync(VirtualCrossoverChannelSettings settings)
    {
        if (settings.HistoryEntryId is { } entryId && HistoryService != null)
        {
            MeasurementHistorySnapshot? snapshot =
                await HistoryService.GetSnapshotAsync(entryId);
            if (snapshot != null)
            {
                return (snapshot, null);
            }
        }

        if (session.Locate(settings.SourceFilePath, settings.SourceRelativePath) is { } path)
        {
            ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(path);
            return (
                MeasurementHistoryService.CreateSnapshot(file),
                string.Equals(path, settings.SourceFilePath, StringComparison.Ordinal)
                    ? null
                    : path);
        }

        return (null, null);
    }

    private void UpdateSourceButton(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelControl control = ControlFor(channel);
        // Every path refreshing a source can change whether the channel has an average.
        RefreshSpatialAverageStatus(channel);
        RefreshHybridAvailability();
        string? name = channel.Settings.DisplayName;
        bool resolved = channel.TransferImpulseResponse != null;
        control.SourceButton.Text = string.IsNullOrWhiteSpace(name)
            ? "Source..."
            : resolved ? name : $"⚠ {name}";
        // As measured, from the raw transfer IR; Invert is a separate virtual stage.
        control.SetMeasuredPolarity(
            channel.TransferImpulseResponse is { } ir
                ? VirtualCrossoverAnalysis.EstimatePolarity(ir)
                : PolarityEstimate.Unknown);
        toolTip.SetToolTip(
            control.SourceButton,
            resolved
                ? channel.Settings.SourceFilePath ?? name
                : "Pick the channel's measurement: a saved impulse-response\r\n" +
                  "file or a history entry.\r\n" +
                  "Requires a loopback transfer IR.");
    }

    // Rebuilt per click: enabled states follow channel state.
    private void ShowPeqMenu(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings peqSettings = channel.Settings;
        bool hasPeq =
            peqSettings.PeqBands.Count > 0 || peqSettings.PeqPreampDb != 0;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Load from file…", null, (_, _) => LoadPeq(channel));
        var saveItem = new ToolStripMenuItem(
            "Save to file…",
            null,
            (_, _) => SavePeq(channel))
        {
            Enabled = hasPeq,
            ToolTipText =
                "Write this channel's bank out as an EQ profile (or a tuning\r\n" +
                "sheet PDF) — the whole tune can be built here without a file,\r\n" +
                "so this is where it leaves for the hardware."
        };
        menu.Items.Add(saveItem);
        menu.Items.Add(new ToolStripSeparator());

        bool hasMeasurement =
            channel.SideState(channel.ActiveRight).TransferImpulseResponse != null;
        // A bypassed block draws raw here, so say before the trip that the wizard's curve differs.
        bool bypassed = channel.Pair.Bypass;
        var editItem = new ToolStripMenuItem(
            bypassed
                ? "Edit in EQ Wizard (chain — block is bypassed)"
                : "Edit in EQ Wizard",
            null,
            (_, _) => RequestPeqHandoff(channel, withChain: true))
        {
            Enabled = hasMeasurement,
            ToolTipText = "Tune this channel's PEQ in the EQ Wizard against its\r\n" +
                "response through the DSP chain with the PEQ itself bypassed,\r\n" +
                "windowed as this plot windows it. A Return button brings\r\n" +
                "the result back to this channel." +
                (bypassed
                    ? "\r\nThis block is BYPASSED, so the plot is drawing its raw\r\n" +
                      "response — the wizard will show the chain instead, which is\r\n" +
                      "what the PEQ is for once bypass comes off."
                    : string.Empty)
        };
        menu.Items.Add(editItem);
        var editRawItem = new ToolStripMenuItem(
            "Edit raw in EQ Wizard",
            null,
            (_, _) => RequestPeqHandoff(channel, withChain: false))
        {
            Enabled = hasMeasurement,
            ToolTipText = "The same handoff against the raw measurement — the\r\n" +
                "driver before the DSP chain, as the Raw curve draws it."
        };
        menu.Items.Add(editRawItem);

        menu.Items.Add(new ToolStripSeparator());
        var clearItem = new ToolStripMenuItem(
            "Clear",
            null,
            (_, _) => ClearPeq(channel))
        {
            Enabled = hasPeq
        };
        menu.Items.Add(clearItem);

        DropDownMenu.ShowUnder(ControlFor(channel).PeqMenuButton, menu);
    }

    // The gate mirrors the magnitude view: shared template, active pin, last redraw's anchor.
    private void RequestPeqHandoff(VirtualCrossoverChannel channel, bool withChain)
    {
        if (EditPeqInWizardRequested is not { } requested)
        {
            return;
        }

        VirtualDspEqHandoffRequest? request = BuildPeqHandoffRequest(
            channel, withChain, HandoffSpatialAverage(channel, channel.ActiveRight));
        if (request != null)
        {
            requested(request);
        }
    }

    // Null when the side has no measurement.
    private VirtualDspEqHandoffRequest? BuildPeqHandoffRequest(
        VirtualCrossoverChannel channel,
        bool withChain,
        (LiveCaptureDocument? Capture, double OffsetDb) spatialAverage,
        // Written to the panel only once the fit has landed.
        double? targetLevelDb = null)
    {
        MagnitudeGateSnapshot snapshot = session.MagnitudeGate;
        // Only a render describing the CURRENT settings may place the window; stale -> the builder reads the channel's front.
        int? renderAnchor =
            lastProcessedRender is { Channels.Count: >= 2 } render &&
            processingCoordinator.IsCurrent(render.Revision)
                ? ProcessedChannels.SharedStartAnchorIndex(render.Channels)
                : null;
        VirtualDspEqHandoffRequest request;
        try
        {
            request = VirtualDspEqHandoff.Build(
                channel,
                channel.ActiveRight,
                withChain,
                session.ProcessorProfile,
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                renderAnchor,
                CapturePhaseContext(channel),
                targetLevelDb ?? (double)numericTargetLevel.Value,
                (double)numericTargetLevel.Minimum,
                (double)numericTargetLevel.Maximum,
                snapshot.SmoothingInverseOctaves,
                // The wizard pins what the panel rendered with, including per-channel Own calibration.
                session.Calibration.For(channel.SideState(channel.ActiveRight)),
                session.Calibration.NameFor(channel.SideState(channel.ActiveRight)),
                session.Calibration.SpatialAverageFor(),
                projectGeneration,
                spatialAverage.Capture,
                spatialAverage.OffsetDb,
                HybridRequested &&
                    session.SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray &&
                    spatialAverage.Capture == null);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return request;
    }

    /// <summary>Other drivers as processed IRs (not drawn curves, so the wizard can re-gate), plus window and τ.</summary>
    /// <remarks>Resolved over the set the wizard draws (<see cref="ProcessedChannels.PhaseNeighbourhood"/>); null when the render is stale.</remarks>
    private EqWizardPhaseContext? CapturePhaseContext(VirtualCrossoverChannel channel)
    {
        if (lastProcessedRender is not { } render ||
            !processingCoordinator.IsCurrent(render.Revision))
        {
            return null;
        }

        List<ProcessedChannel> drawn =
            ProcessedChannels.PhaseNeighbourhood(render.Channels, channel);
        int index = drawn.FindIndex(item => ReferenceEquals(item.Channel, channel));
        if (index < 0)
        {
            return null;
        }

        // Off the snapshot: a concurrent import rebinds channels and the live rate reads zero.
        int sampleRate = drawn[0].SampleRate;
        double referenceOffsetMs = session.Gate.ReferenceOffsetMs(drawn, sampleRate);
        double detrendMs = session.Gate.CommonDetrendMs(drawn, referenceOffsetMs, sampleRate);
        List<double> offsets = session.Gate.PerCurveOffsets(drawn, referenceOffsetMs, sampleRate);

        return new EqWizardPhaseContext(
            // Curves render as Manual against one τ for the whole set, but the user's detrend mode must arrive intact.
            session.Gate.Settings(
                referenceOffsetMs,
                session.Gate.DetrendMode,
                detrendMs),
            offsets[index],
            detrendMs,
            session.Gate.PinnedOffsetMs is not null,
            // The source responses travel too, so the wizard re-resolves placements the same way when its window changes.
            PlacementChannel.From(drawn[index]),
            sampleRate,
            drawn[index].Color,
            drawn
                .Select((item, position) => (item, position))
                .Where(entry => entry.position != index)
                .Select(entry => new EqWizardPhaseNeighbour(
                    entry.item.Channel.Name,
                    entry.item.Color,
                    PlacementChannel.From(entry.item),
                    offsets[entry.position]))
                .ToList());
    }

    /// <summary>False, writing nothing, when the channel is gone (removed or replaced by an import).</summary>
    internal bool TryApplyPeqFromWizard(
        VirtualDspEqReturnToken token,
        EqualizationCurve curve,
        double targetLevelDb)
    {
        MagnitudeGateSnapshot snapshot = session.MagnitudeGate;
        if (!VirtualDspEqHandoff.TryApplyReturn(
                session.Channels,
                token,
                curve,
                projectGeneration,
                // Per side: under Own the panel holds no single calibration, and null would refuse every return.
                session.Calibration.For(token.Channel.SideState(token.RightSide)),
                session.Calibration.SpatialAverageFor(),
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                (double)numericTargetLevel.Value,
                // Same decision the handoff recorded, so an in-flight redraw cannot turn a valid return into a refusal.
                HybridHandoffCapture(token.Channel, token.RightSide),
                session.ProcessorSampleRateHz))
        {
            return false;
        }

        // The guard above proved the level is still the wizard's starting point, so writing it overwrites nothing.
        if (!((double)numericTargetLevel.Value).Equals(targetLevelDb))
        {
            numericTargetLevel.Value = numericTargetLevel.ClampValue(targetLevelDb);
        }

        UpdatePeqReadouts(token.Channel);
        SaveAndRedraw();
        return true;
    }

    // Same coordinator and formats as the EQ Wizard: this is the door to the hardware, not a second exporter.
    private void SavePeq(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        var curve = new EqualizationCurve(settings.PeqBands, settings.PeqPreampDb);
        string side = channel.Pair.Mono ? "mono" : channel.ActiveRight ? "R" : "L";
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = peqExport.DefaultExportExtension,
            FileName = $"channel-{channel.Name}-{side}",
            Filter = peqExport.ExportFilter,
            RestoreDirectory = true,
            Title = $"Save channel {channel.Name} PEQ"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        EqWizardExportTarget target = peqExport.ResolveExportTarget(dialog.FilterIndex);
        if (!ConfirmPeqExportLoss(EqExportWarnings.ShelvingBandsDropped(target, curve)) ||
            !ConfirmPeqExportLoss(EqExportWarnings.AllPassBandsDropped(target, curve)) ||
            !ConfirmPeqExportLoss(EqExportWarnings.PreampDropped(target, curve)))
        {
            return;
        }

        (double minHz, double maxHz) =
            VirtualDspEqHandoff.PassbandFor(settings) ?? (20.0, 20_000.0);
        EqWizardFileResult result = peqExport.Export(
            new EqWizardExportRequest(
                dialog.FileName,
                target,
                curve,
                // Stated for the device's rate and Q convention, not the measurement rate.
                session.ProcessorSampleRateHz,
                $"Channel {channel.Name} ({side})",
                minHz,
                maxHz,
                // Bands were not necessarily fitted here; no invented statistics.
                Stats: null,
                session.ProcessorProfile.QConvention));
        if (!result.Success)
        {
            ShowError("PEQ could not be exported.", result.Exception!.Message);
        }
    }

    private bool ConfirmPeqExportLoss(string? warning) =>
        warning == null ||
        MessageBox.Show(
            FindForm(),
            warning,
            "Virtual DSP",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    private void LoadPeq(VirtualCrossoverChannel channel)
    {
        IReadOnlyList<IEqProfileFormat> formats = EqProfileFormats.Importable;
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = EqFormatFileDialogs.BuildFilter(formats),
            Title = $"Load channel {channel.Name} PEQ"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        IEqProfileFormat chosen =
            EqFormatFileDialogs.ResolveFormat(formats, dialog.FilterIndex)!;
        EqualizationCurve curve;
        try
        {
            // An unrecognised file would silently clear the channel's PEQ below.
            if (!chosen.TryImport(File.ReadAllText(dialog.FileName), out curve))
            {
                ShowError(
                    "PEQ could not be imported.",
                    $"No equalizer settings were found. Check that the file really is a " +
                    $"{chosen.Name} profile.");
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError("PEQ could not be imported.", exception.Message);
            return;
        }

        channel.Settings.PeqBands = curve.Bands
            .Take(EqualizationCurve.MaxBandCount)
            .ToList();
        channel.Settings.PeqPreampDb = curve.PreampDb;
        channel.Settings.PeqSourceName = Path.GetFileName(dialog.FileName);
        UpdatePeqReadouts(channel);
        SaveAndRedraw();
    }

    private void ClearPeq(VirtualCrossoverChannel channel)
    {
        channel.Settings.PeqBands = new List<PeqBand>();
        channel.Settings.PeqPreampDb = 0;
        channel.Settings.PeqSourceName = null;
        UpdatePeqReadouts(channel);
        SaveAndRedraw();
    }

    // Rebuilt per click like the PEQ menu. The kernel lives in the session; constructor and files are its ways in and out.
    private void ShowFirMenu(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(
            settings.HasFir ? "Open in FIR Constructor…" : "Design in FIR Constructor…",
            null,
            (_, _) => RequestFirHandoff(channel))
        {
            Enabled = EditFirInConstructorRequested != null,
            ToolTipText =
                "Design a linear-phase low-pass, high-pass or band-pass kernel for this\r\n" +
                "side and return it here. A kernel imported from a file opens as it is;\r\n" +
                "any change in the constructor replaces it with a designed one."
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(
            settings.HasFir ? "Import FIR filter (replace)…" : "Import FIR filter…",
            null,
            (_, _) => ImportFir(channel));
        var exportItem = new ToolStripMenuItem("Export FIR filter…", null, (_, _) => ExportFir(channel))
        {
            Enabled = settings.HasFir,
            ToolTipText =
                "Write this channel's kernel out as a 32-bit float WAV or a text file\r\n" +
                "(one coefficient per line), at the processor's rate — the kernel is\r\n" +
                "kept in the session, so this is where it leaves for the hardware."
        };
        menu.Items.Add(exportItem);
        menu.Items.Add(new ToolStripSeparator());
        var clearItem = new ToolStripMenuItem("Clear", null, (_, _) => ClearFir(channel))
        {
            Enabled = settings.HasFir
        };
        menu.Items.Add(clearItem);
        DropDownMenu.ShowUnder(ControlFor(channel).FirButton, menu);
    }

    private void ImportFir(VirtualCrossoverChannel channel)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = FirFilterFiles.ImportFileDialogFilter,
            Title = $"Import channel {channel.Name} FIR filter"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        FirFilter kernel;
        try
        {
            // Not a kernel: the assignment below would silently drop the existing one.
            kernel = FirFilterFiles.Load(dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowError("FIR filter could not be imported.", exception.Message);
            return;
        }

        VirtualCrossoverChannelSettings settings = channel.Settings;
        settings.Fir = kernel;
        settings.FirSourceName = Path.GetFileName(dialog.FileName);
        // A file is taps only, not a designed crossover.
        settings.FirDesign = null;
        UpdateFirReadout(channel);
        SaveAndRedraw();
    }

    private void ExportFir(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        if (settings.Fir is not { } kernel)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "wav",
            Filter = FirFilterFiles.ExportFileDialogFilter,
            FileName = settings.FirDesign is { } design
                ? $"{channel.Name} FIR {FirCrossoverDescription.Short(design)}"
                : Path.GetFileNameWithoutExtension(settings.FirSourceName) is { Length: > 0 } stem
                    ? stem
                    : $"{channel.Name} FIR",
            OverwritePrompt = true,
            Title = $"Export channel {channel.Name} FIR filter"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            // Taps mean the processor's rate in this session.
            FirFilterFiles.Save(
                dialog.FileName,
                kernel,
                session.ProcessorSampleRateHz,
                settings.FirSourceName,
                settings.FirDesign is { } designed ? FirCrossoverDescription.Long(designed) : null);
        }
        catch (Exception exception)
        {
            ShowError("FIR filter could not be exported.", exception.Message);
        }
    }

    private void ClearFir(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        settings.Fir = null;
        settings.FirSourceName = null;
        settings.FirDesign = null;
        UpdateFirReadout(channel);
        SaveAndRedraw();
    }

    private void UpdateFirReadout(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        ControlFor(channel).SetFir(settings.Fir, settings.FirSourceName, settings.FirDesign);
    }

    private void RequestFirHandoff(VirtualCrossoverChannel channel)
    {
        if (EditFirInConstructorRequested is not { } requested)
        {
            return;
        }

        requested(FirConstructorHandoff.Build(
            channel, channel.ActiveRight, projectGeneration, session.ProcessorSampleRateHz));
    }

    /// <summary>False, writing nothing, when the side is no longer the one the session opened on.</summary>
    internal bool TryApplyFirFromConstructor(
        FirConstructorReturnToken token,
        FirFilter kernel,
        FirCrossoverDesign design)
    {
        if (!FirConstructorHandoff.TryApplyReturn(
                session.Channels,
                token,
                kernel,
                design,
                projectGeneration,
                session.ProcessorSampleRateHz,
                session.Project.ResolveDspFirFilters()))
        {
            return false;
        }

        UpdateFirReadout(token.Channel);
        SaveAndRedraw();
        return true;
    }

    private void UpdatePeqReadouts(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        bool noPeq = settings.PeqBands.Count == 0 && settings.PeqPreampDb == 0;
        string text = noPeq
            ? "No PEQ"
            : $"{settings.PeqSourceName ?? "PEQ"}: {settings.PeqBands.Count} bands, " +
              $"preamp {settings.PeqPreampDb:0.0} dB";
        // The block keeps its gain readout in step with the preamp itself.
        VirtualCrossoverChannelControl control = ControlFor(channel);
        control.PeqPreampDb = settings.PeqPreampDb;
        Label peqInfoLabel = control.PeqInfoLabel;
        peqInfoLabel.Text = text;
        toolTip.SetToolTip(peqInfoLabel, noPeq ? string.Empty : text);
    }

    private AcousticView CurrentAcousticView() =>
        radioViewImpulse.Checked ? AcousticView.Impulse
        : radioViewStep.Checked ? AcousticView.Step
        : radioViewPhase.Checked ? AcousticView.Phase
        : radioViewGroupDelay.Checked ? AcousticView.GroupDelay
        : AcousticView.Magnitude;

    private DspPlotMode CurrentDspPlotMode() =>
        radioDspPhase.Checked ? DspPlotMode.Phase
        : radioDspGroupDelay.Checked ? DspPlotMode.GroupDelay
        : radioDspCorrelation.Checked ? DspPlotMode.Correlation
        : radioDspCoherence.Checked ? DspPlotMode.Coherence
        : DspPlotMode.Magnitude;

    // Both junction modes share the pair selector, rebuild loop and inputs.
    private bool JunctionPlotModeSelected() =>
        radioDspCorrelation.Checked || radioDspCoherence.Checked;

    private static bool IsJunctionMode(DspPlotMode mode) =>
        mode is DspPlotMode.Correlation or DspPlotMode.Coherence;

    // Retract the junction radios in the other container first (see the wiring).
    private void OnChainDspModeChecked()
    {
        radioDspCorrelation.Checked = false;
        radioDspCoherence.Checked = false;
        OnDspPlotModeChanged();
    }

    private void OnDspPlotModeChanged()
    {
        comboBoxCorrelationPair.Enabled =
            JunctionPlotModeSelected() && comboBoxCorrelationPair.Items.Count > 0;
        if (suppressProjectEvents)
        {
            return;
        }

        session.Project.SetDspPlotMode(CurrentDspPlotMode());
        ScheduleSave();
        RedrawDspPlot();
    }

    private void OnCorrelationPairChanged()
    {
        if (suppressProjectEvents || suppressCorrelationPairEvents)
        {
            return;
        }

        session.Project.CorrelationPairIndex =
            Math.Max(0, comboBoxCorrelationPair.SelectedIndex);
        ScheduleSave();
        RedrawDspPlot();
    }

    /// <summary>Read from the control, not the project, so a mid-edit redraw draws the new pick.</summary>
    private VirtualCrossoverGroupView SelectedGroupView =>
        comboBoxGroupView.SelectedItem is VirtualCrossoverGroupView view
            ? view
            : VirtualCrossoverGroupView.FrontAndSub;

    private SumLossWindow SelectedSumLossWindow =>
        comboBoxSumLoss.SelectedItem is SumLossWindow window
            ? window
            : SumLossWindow.Direct;

    private void InitializeSumLossComboBox()
    {
        foreach (SumLossWindow window in SumLossWindows.All)
        {
            comboBoxSumLoss.Items.Add(window);
        }

        comboBoxSumLoss.Format += (_, args) =>
        {
            if (args.ListItem is SumLossWindow window)
            {
                args.Value = SumLossWindows.DisplayName(window);
            }
        };
        comboBoxSumLoss.SelectedItem = SumLossWindow.Direct;
    }

    private void InitializeGroupViewComboBox()
    {
        comboBoxGroupView.Items.AddRange(
            [.. VirtualCrossoverGroupViews.All.Cast<object>()]);
        comboBoxGroupView.Format += (_, args) =>
        {
            if (args.ListItem is VirtualCrossoverGroupView view)
            {
                args.Value = VirtualCrossoverGroupViews.DisplayName(view);
            }
        };
        comboBoxGroupView.SelectedItem = VirtualCrossoverGroupView.FrontAndSub;
    }

    private void InitializeSmoothingComboBox()
    {
        foreach (int value in OverlaySmoothing.SupportedInverseOctaves)
        {
            comboBoxSmoothing.Items.Add(value);
        }

        comboBoxSmoothing.Format += (_, args) =>
        {
            if (args.ListItem is int value)
            {
                args.Value = OverlaySmoothing.GetLabel(value);
            }
        };
        comboBoxSmoothing.SelectedItem = 12;
    }

    private void InitializeToolTips()
    {
        toolTip.SetToolTip(
            checkBoxShowSum,
            "The complex (vector) sum of the processed channels —\r\n" +
            "the physically correct prediction of all drivers\r\n" +
            "playing together.");
        toolTip.SetToolTip(
            labelSumLoss,
            "How many dB the complex sum falls short of the magnitude sum\r\n" +
            "(<= 0): 0 dB is perfectly in phase. The selector beside it\r\n" +
            "picks the window it is read through.");
        toolTip.SetToolTip(
            buttonAddChannel,
            "Add a channel block to the bottom of the list.");
        toolTip.SetToolTip(
            buttonRemoveChannel,
            "Drop the last channel block, with whatever is loaded in it.");
        toolTip.SetToolTip(
            buttonResetChannels,
            $"Start over: {DefaultChannelCount} empty default blocks, and the\r\n" +
            "panel's own settings; calibration and the EQ target stay.\r\n" +
            "Asks first, and copies the session aside so Load session…\r\n" +
            "brings it back.");
        toolTip.SetToolTip(
            radioViewMagnitude,
            "Show the magnitude of the channels, the sum,\r\n" +
            "and the sum loss.");
        toolTip.SetToolTip(
            radioViewPhase,
            "Show the phase of the processed channels and the sum.\r\n" +
            "Well-aligned channels track each other through\r\n" +
            "the crossover region.");
        toolTip.SetToolTip(
            radioViewImpulse,
            "Show each channel's processed impulse response around\r\n" +
            "the phase gate, every trace normalized to its own peak.\r\n" +
            "Well-aligned drivers start together.");
        toolTip.SetToolTip(
            radioViewGroupDelay,
            "Show each processed channel's group delay and the Sum's\r\n" +
            "through the phase gate: the arrival time of the energy\r\n" +
            "inside the window, in ms from the record's start.\r\n" +
            "Well-aligned drivers meet through the crossover.");
        toolTip.SetToolTip(
            radioViewStep,
            "Show each processed channel's step response and the Sum's\r\n" +
            "around the phase gate, all on one common scale.\r\n" +
            "A driver in the wrong polarity steps the other way first.");
        toolTip.SetToolTip(
            comboBoxGroupView,
            "Which part of the installation the plot shows; the curves, the\r\n" +
            "Sum, the loss and the read-out follow it. Groups sums one line\r\n" +
            "per zone. A centre is drawn but never summed.");
        toolTip.SetToolTip(
            comboBoxSmoothing,
            "Fractional-octave smoothing of the magnitude curves and the\r\n" +
            "Sum loss read. Psychoacoustic: 1/3–1/6 octave with peak\r\n" +
            "weighting. The junction metrics stay unsmoothed.");
        toolTip.SetToolTip(
            comboBoxSumLoss,
            "The window the Sum loss is read through. FDW-8: the direct\r\n" +
            "sound, as the Junction phase block reads it. Full: the\r\n" +
            "steady-state sum the cabin hears. The two are not comparable.");
        toolTip.SetToolTip(
            radioDspGroupDelay,
            "What the lower plot shows for each channel's DSP chain:\r\n" +
            "Magnitude, Phase, or filter Group delay (the crossover/PEQ\r\n" +
            "group delay in ms, excluding the channel's bulk delay).");
        toolTip.SetToolTip(
            radioDspCorrelation,
            "Junction correlation of the selected pair, and its acoustic\r\n" +
            "score, against an extra delay on the upper channel in both\r\n" +
            "polarities. 0 ms is the alignment as it stands.");
        toolTip.SetToolTip(
            comboBoxCorrelationPair,
            "Which adjacent channel pair the correlation view analyzes\r\n" +
            "(active side, ordered along the spectrum).");
        toolTip.SetToolTip(
            checkBoxShowTarget,
            "Draw the EQ target over the predicted sum: the SAME target the\r\n" +
            "EQ Wizard is set to, so a shape tuned in either place is the\r\n" +
            "one shape this app aims at. It is a magnitude reference, so it\r\n" +
            "is offered on the Magnitude view only.");
        toolTip.SetToolTip(
            numericTargetLevel,
            "The level the target hangs at. These are transfer-function\r\n" +
            "dB with no absolute reference, so the target has no level of\r\n" +
            "its own here: set it where you read the sum. Stored with the\r\n" +
            "session, not with the target, so retuning the shape leaves it.");
        toolTip.SetToolTip(
            buttonTargetSettings,
            "Shape the target — a parametric shape or an imported house\r\n" +
            "curve — previewed on the Magnitude view. It is the SAME target\r\n" +
            "the EQ Wizard equalizes towards.");
        toolTip.SetToolTip(
            comboBoxCalibration,
            "Microphone calibration for the magnitude curves and the Sum\r\n" +
            "loss read. Optional — the measurement is loopback-referenced.\r\n" +
            "Saved into the session as the curve itself.");
        toolTip.SetToolTip(
            buttonAutoDelay,
            "Align the channels: first arrivals, then a phase search for\r\n" +
            "delays and polarity, reviewed as before/after. Set the\r\n" +
            "crossovers first — the search reads their overlap.");
        toolTip.SetToolTip(
            radioSideLeft,
            "Show and edit the LEFT side of every channel pair.\r\n" +
            "● — at least one source is loaded on this side.");
        toolTip.SetToolTip(
            radioSideRight,
            "Show and edit the RIGHT side of every channel pair.\r\n" +
            "● — at least one source is loaded on this side.");
        toolTip.SetToolTip(
            buttonCopyLeftToRight,
            "Copy the LEFT side onto the RIGHT side: a dialog picks the\r\n" +
            "channels and the parts of the chain — crossover and PEQ by\r\n" +
            "default, gain, delay, polarity and the all-pass on request.\r\n" +
            "Sources stay with their side; mono channels are not\r\n" +
            "offered.");
        toolTip.SetToolTip(
            buttonCopyRightToLeft,
            "Copy the RIGHT side onto the LEFT side: a dialog picks the\r\n" +
            "channels and the parts of the chain — crossover and PEQ by\r\n" +
            "default, gain, delay, polarity and the all-pass on request.\r\n" +
            "Sources stay with their side; mono channels are not\r\n" +
            "offered.");
        toolTip.SetToolTip(
            checkBoxSideLock,
            "Keep both sides' crossover and polarity in step: a change\r\n" +
            "on the side shown is written onto the other side as it is\r\n" +
            "made. Gain, delay, phase and PEQ are not locked.");
        toolTip.SetToolTip(
            buttonDspProcessor,
            "The processor this project is designed for: its processing\r\n" +
            "rate, which every simulated filter is built at, and its PEQ Q\r\n" +
            "convention, which only restates the tuning sheet.");
        toolTip.SetToolTip(
            buttonAi,
            "Work with a chat assistant through the clipboard: Copy for AI\r\n" +
            "puts the tune on it, Import AI proposal reads the reply back\r\n" +
            "and shows every change before it is applied.\r\n" +
            "Nothing is sent anywhere by Resonalyze.");
        toolTip.SetToolTip(
            buttonAutoSetup,
            "Crossover wizard: detect each channel's driver type from\r\n" +
            "its response, confirm the types, and get a starting point —\r\n" +
            "LR24 splits where the responses intersect and cut-only\r\n" +
            "gains that level the channels.\r\n" +
            "Run Auto delay afterward to phase-align the result.");
        toolTip.SetToolTip(
            buttonPhaseGate,
            "Gate for the phase and impulse views: offset, fades and an IR\r\n" +
            "preview. The magnitude view takes only the offset. Offset and\r\n" +
            "detrend belong to the side shown; lengths and mode are shared.");
        toolTip.SetToolTip(
            buttonSessionExport,
            "Save the whole session (sources, DSP chains, gate, view)\r\n" +
            "to a file to share or archive it.");
        toolTip.SetToolTip(
            buttonSessionImport,
            "Load a saved session file, replacing the current state.\r\n" +
            "Sources are re-resolved from history or their file paths.");
        toolTip.SetToolTip(
            buttonAudition,
            "Render a music file through the tune into a stereo WAV: the\r\n" +
            "left sum on channel 1, the right on channel 2.\r\n" +
            "Listen through HEADPHONES only.");
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
            session.GateFor(!session.Project.ActiveSideRight).StoredOffsetMs,
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
        buttonAutoDelay.Enabled = !busy;
        // Gathered at one revision; an import would be overwritten by a load in progress.
        buttonAi.Enabled = !busy && !agentBusy;
        // Starting mid-redraw would race the invalidation and render nothing.
        buttonAudition.Enabled = !busy;
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

    private async Task RedrawMainPlotAsync()
    {
        // Old curves stay on screen until new data is ready (no flicker).
        ProcessedRender? render = await ProcessChannelsAsync();
        if (render == null || mainPlotView.IsDisposed)
        {
            return;
        }
        long revision = render.Revision;
        List<ProcessedChannel> processed = render.Channels;
        if (!processingCoordinator.IsCurrent(revision))
        {
            return;
        }

        // The whole set is kept: the junction views and opposite-side read-outs need channels this view does not draw.
        lastProcessedRender = render;

        // Read once: a control changed during the awaits below requests the next frame. Filtered by group view once, so
        // curves, sum, loss and read-out describe the same channels.
        VirtualCrossoverViewState view = CaptureViewState();
        VirtualCrossoverGroupView groupView = view.GroupView;
        var frame = VirtualCrossoverFrame.Of(processed, groupView);
        if (frame.Shown.Count == 0)
        {
            // With nothing resolved at all the zone hint would mislead, so show the no-sources hint.
            acousticPlot.Draw(new AcousticRender(
                processed.Count == 0
                    ? AcousticViewBuilder.NoSourcesHint
                    : AcousticViewBuilder.EmptyViewHint(groupView),
                [],
                null));
            MetricChanged?.Invoke(string.Empty, string.Empty);
            // A warning about channels no longer visible would read as a fault in this view.
            HideWarning();
            return;
        }

        // Direct loss (FDW-8) sums the junction read-out's own gated spectra, so it is built with it.
        VirtualCrossoverPhaseGate gate = session.Gate;
        (List<VirtualCrossoverMetric.PhaseEntry> phaseEntries, List<SignalPoint>? directLoss) =
            await frame.ReadJunctionsAsync(
                metrics,
                gate,
                gate.PinnedOffsetMs,
                session.MagnitudeGate.SmoothingInverseOctaves,
                withDirectLoss: view.LossWindow == SumLossWindow.Direct);

        // Narrowed by the Show filter, never a shortened list: a block's list position is its cache identity.
        List<VirtualCrossoverMetric.StereoDelta> stereoDeltas =
            await metrics.ComputeStereoDeltasAsync(
                session.Channels,
                revision,
                includePair: pair =>
                    VirtualCrossoverGroupViews.IsShown(groupView, pair.Zone),
                hybridLevelDeltaDb: hybridReader.StereoLevelReader(HybridRequested));
        // Quoted by cross-group views instead of a loss; adds only arrival FFTs.
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> groupDeltas =
            await metrics.ComputeGroupDeltasAsync(
                frame.Shown, groupView, revision,
                hybridGroupLevelDeltaDb: hybridReader.GroupLevelReader(HybridRequested));
        // The curve windows through the OPPOSITE side's gate placement; both sides must be drawn by the same method.
        VirtualCrossoverSideSum? oppositeSide = null;
        if (view.ShowSum && view.View is AcousticView.Magnitude or AcousticView.Step)
        {
            oppositeSide = await metrics.ComputeSideSumAsync(
                session.Channels, !session.Project.ActiveSideRight, revision, minimumChannels: 2,
                includePair: pair =>
                    VirtualCrossoverGroupViews.ParticipatesInTotalSum(
                        groupView, pair.Zone));
        }

        // Envelopes (Hilbert over the whole record, 2^17+ samples) are warmed off the UI thread: a drag hands
        // each frame a new array. Memoized per array.
        if (view.View == AcousticView.Impulse)
        {
            Complex[][] drawnResponses =
                [.. frame.Shown
                    .Where(item => item.Channel.Pair.ShowProcessedCurve)
                    .Select(item => item.ImpulseResponse)];
            await Task.Run(() =>
            {
                using var _ = AppProfiler.Zone("VirtualDSP.WarmImpulseEnvelopes");
                drawnResponses.AsParallel().ForAll(
                    response => ImpulseWindowPreview.EnvelopeOf(response));
            });
        }
        if (mainPlotView.IsDisposed || !processingCoordinator.IsCurrent(revision))
        {
            return;
        }

        // The synchronous UI-thread part of the frame; each step carries its own zone.
        using var _ = AppProfiler.Zone("VirtualDSP.RedrawMainPlot");
        List<AnalysisCurve>? magnitudes;
        AnalysisCurve? sumCurve;
        List<SignalPoint>? lossCurve;
        using (AppProfiler.Zone("VirtualDSP.BuildCurves"))
        {
            (magnitudes, sumCurve, lossCurve) = metrics.BuildCurves(
                frame.Shown, session.MagnitudeGate.SmoothingInverseOctaves, frame.Summed);
        }

        // Decided before the awaits, where the junction phase block uses it.
        if (!frame.QuotesJunctions)
        {
            lossCurve = null;
        }

        // Disable draws no curve but the column keeps the full read.
        bool lossDirect = view.LossWindow == SumLossWindow.Direct;
        List<SignalPoint>? shownLoss = lossDirect ? directLoss : lossCurve;
        List<SignalPoint>? drawnLoss = view.LossWindow == SumLossWindow.Off ? null : shownLoss;

        // Before warnings and render: both read it.
        HybridMagnitudes? hybrid = null;
        if (view.HybridRequested && magnitudes != null && view.View == AcousticView.Magnitude)
        {
            using (AppProfiler.Zone("VirtualDSP.BuildHybrid"))
            {
                hybrid = hybridReader.Build(
                    frame.Shown,
                    magnitudes,
                    session.Project.ActiveSideRight,
                    session.MagnitudeGate.SmoothingInverseOctaves);
            }

            if (hybrid != null)
            {
                lastHybrid = (revision, hybrid.OffsetDb);
            }
        }

        // No hybrid capture on the other side -> drop the curve: a mixed-method sum reads as a false L/R difference.
        AnalysisCurve? oppositeSum = null;
        if (oppositeSide != null)
        {
            using (AppProfiler.Zone("VirtualDSP.BuildOppositeSum"))
            {
                oppositeSum = hybrid == null
                    ? BuildOppositeMagnitudeCurve(oppositeSide)
                    : hybridReader.OppositeSum(oppositeSide, hybrid.OffsetDb);
            }
        }

        using (AppProfiler.Zone("VirtualDSP.UpdateMetric"))
        {
            UpdateMetric(
                frame.Summed, shownLoss, phaseEntries, stereoDeltas, hybrid,
                groupDeltas, lossDirect);
        }

        using (AppProfiler.Zone("VirtualDSP.UpdateWarnings"))
        {
            UpdateWarnings(processed, hybrid);
        }

        // Split from the draw so the profiler separates curve building from OxyPlot.
        AcousticRender acousticRender;
        using (AppProfiler.Zone("VirtualDSP.BuildAcousticRender"))
        {
            acousticRender = viewBuilder.Build(
                view,
                frame,
                new AcousticFrameCurves(
                    magnitudes, sumCurve, drawnLoss, lossDirect, oppositeSum, oppositeSide, hybrid),
                loadingProject);
        }

        using (AppProfiler.Zone("VirtualDSP.AcousticPlotDraw"))
        {
            acousticPlot.Draw(acousticRender);
        }
    }

    // Handed to the host (the EQ Wizard owns the one target). A session without a stored target starts carrying the current one.
    private void ApplyProjectTarget()
    {
        if (session.Project.Target is { } stored)
        {
            TargetCurveChanged?.Invoke(stored.ToCurve());
            return;
        }

        if (targetCurve is { } current)
        {
            session.Project.Target = VirtualCrossoverTargetSettings.FromCurve(current);
        }
    }

    // Same menu as the EQ Wizard's Target button; rebuilt per click.
    private void ShowTargetMenu()
    {
        if (targetCurve is not { } current)
        {
            return;
        }

        if (targetMenu is { Visible: true })
        {
            targetMenu.Close();
            return;
        }

        targetMenu?.Dispose();
        targetMenu = TargetCurveMenu.Build(
            current.Spec.Imported,
            OpenTargetSettings,
            ImportTargetCurve);
        DropDownMenu.ShowUnder(buttonTargetSettings, targetMenu);
    }

    private void ImportTargetCurve()
    {
        if (targetCurve is not { } before ||
            TargetCurveImport.Prompt(FindForm()) is not { } imported)
        {
            return;
        }

        radioViewMagnitude.Checked = true;
        checkBoxShowTarget.Checked = true;
        var edited = before with
        {
            Spec = before.Spec with { Imported = imported }
        };
        ApplyTargetLocally(edited);
        StoreTargetInProject(edited);
        TargetCurveChanged?.Invoke(edited);
    }

    // The EQ Wizard's isolated target dialog previewing on this plot; Save hands the curve to the host.
    private void OpenTargetSettings()
    {
        if (targetCurve is not { } before)
        {
            return;
        }

        // Put the target on screen: magnitude is the only view where a dB shape means anything.
        radioViewMagnitude.Checked = true;
        checkBoxShowTarget.Checked = true;
        // Opened as the EQ Wizard's dialog so the smoothing vocabulary matches from either button.
        using var dialog = new OverlayTargetSettingsDialog(
            Mode.EqWizard,
            "EQ target",
            0,
            before.Preset,
            before.Spec,
            before.ToleranceDb,
            before.DeviationMode,
            before.Color,
            before.StrokeThickness,
            before.LineStyle,
            100,
            before.SmoothingInverseOctaves,
            [],
            ApplyTargetPreview,
            isolatedTarget: true);

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            ApplyTargetLocally(before);
            return;
        }

        var edited = new EqTargetCurve(
            dialog.Preset,
            dialog.Spec,
            dialog.ToleranceDb,
            dialog.DeviationMode,
            dialog.SelectedColor,
            dialog.StrokeThickness,
            dialog.LineStyle,
            dialog.SmoothingInverseOctaves);
        ApplyTargetLocally(edited);
        StoreTargetInProject(edited);
        TargetCurveChanged?.Invoke(edited);
    }

    // The preview carries no preset, so the current one rides through untouched.
    private void ApplyTargetPreview(OverlayTargetPreview preview)
    {
        if (targetCurve is not { } current)
        {
            return;
        }

        ApplyTargetLocally(current with
        {
            Spec = preview.Spec,
            ToleranceDb = preview.ToleranceDb,
            DeviationMode = preview.DeviationMode,
            Color = preview.Color,
            StrokeThickness = preview.StrokeThickness,
            LineStyle = preview.LineStyle,
            SmoothingInverseOctaves = preview.SmoothingInverseOctaves
        });
    }

    // Memory and plot only: the autosave ticks inside the modal loop and would write an uncommitted preview.
    private void ApplyTargetLocally(EqTargetCurve value)
    {
        targetCurve = value;
        targetToggleColor = value.Color;
        UpdateTargetToggleLook();
        RedrawAll();
    }

    // Nothing before the project loads: the host pushes a target at startup, which would save a default project over the real one.
    private void StoreTargetInProject(EqTargetCurve value)
    {
        if (!initialized)
        {
            return;
        }

        session.Project.Target = VirtualCrossoverTargetSettings.FromCurve(value);
        ScheduleSave();
    }

    // Junction read-outs use the SUMMED channels: a drawn-only centre would invent a crossover.
    private void UpdateMetric(
        List<ProcessedChannel> summed,
        List<SignalPoint>? lossCurve,
        IReadOnlyList<VirtualCrossoverMetric.PhaseEntry> phaseEntries,
        IReadOnlyList<VirtualCrossoverMetric.StereoDelta> stereoDeltas,
        HybridMagnitudes? hybrid,
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> groupDeltas,
        bool lossDirect)
    {
        // Zoned apart from formatting: the per-junction banded analysis is the real work.
        List<VirtualCrossoverMetric.Entry> entries;
        using (AppProfiler.Zone("VirtualDSP.BuildEntries"))
        {
            entries = metrics.BuildEntries(summed, lossCurve);
        }

        (string compact, string detail) = VirtualCrossoverMetric.FormatReadOut(
            entries, lossDirect, phaseEntries, groupDeltas, stereoDeltas, hybrid?.OffsetDb);
        MetricChanged?.Invoke(compact, detail);
    }

    // Read once per frame; the Target curve travels only when it is shown.
    private VirtualCrossoverViewState CaptureViewState() => new(
        CurrentAcousticView(),
        SelectedGroupView,
        checkBoxShowSum.Checked,
        SelectedSumLossWindow,
        HybridRequested,
        checkBoxShowTarget.Checked ? targetCurve : null,
        (double)numericTargetLevel.Value);

    private void UpdateWarnings(
        List<ProcessedChannel> processed, HybridMagnitudes? hybrid)
    {
        gatePlacement = GatePlacementVerdict.Judge(
            processed, session.MagnitudeGate, session.ActiveSideRight);
        if (warnings.Judge(processed, hybrid, gatePlacement) is not { } warning)
        {
            HideWarning();
            return;
        }

        WarningChanged?.Invoke(
            warning.Text,
            warning.Detail,
            warning.Level switch
            {
                // Amber: the view cannot be read yet, not a tuning error.
                VirtualCrossoverWarningLevel.Caution => UiPalette.Warning,
                VirtualCrossoverWarningLevel.Information => UiPalette.TextAccent,
                _ => UiPalette.Error
            });
    }

    private void HideWarning() =>
        WarningChanged?.Invoke(string.Empty, string.Empty, UiPalette.Error);

    private async void AutoAlignDelay()
    {
        (AutoDelayPlan? plan, AutoDelayRefusal? refusal) = VirtualCrossoverAutoDelay.Prepare(
            session, gatePlacement, ConsentToBroadWindowSearch);
        if (plan == null)
        {
            ShowRefusal(refusal!);
            return;
        }

        using var dialog = new VirtualCrossoverAutoDelayDialog();
        // The dialog edits layout-neutral magnitudes; the project stores them layout-signed (see VirtualCrossoverAutoDelay.Commit).
        dialog.Init(
            plan.Stereo,
            session.Project.StereoSceneOffsetMagnitudeMs,
            session.Project.StereoRightHandDrive,
            Math.Abs(session.Project.StereoLevelDifferenceDb),
            plan.Run,
            plan.PolarityWarning,
            plan.HasRearFill,
            session.Project.RearFillOffsetMs);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } result ||
            IsDisposed)
        {
            return;
        }

        await ApplyConfirmedAutoDelayAsync(result);
    }

    private bool ConsentToBroadWindowSearch() =>
        MessageBox.Show(
            FindForm(),
            "No channel has a crossover configured, so the delay search " +
            "will use a broad 100 Hz – 10 kHz window instead of the " +
            "crossover region." +
            Environment.NewLine + Environment.NewLine +
            "For an accurate alignment set the crossover filters first, " +
            "then run Auto delay again." +
            Environment.NewLine + Environment.NewLine +
            "Run the broad-window search anyway?",
            "Virtual DSP",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    private void ShowRefusal(AutoDelayRefusal refusal)
    {
        if (refusal.Message is { } message)
        {
            ShowError(message, refusal.Detail ?? string.Empty);
        }
        else if (refusal.Beep)
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    // Commit first, then the outcome metric best-effort: a metric failure must not read as a failed Apply.
    private async Task ApplyConfirmedAutoDelayAsync(AutoDelayRunResult result)
    {
        try
        {
            CommitAutoDelayResult(result);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Auto delay apply failed: {exception}");
            if (!IsDisposed && IsHandleCreated)
            {
                ShowError("Auto delay apply failed.", exception.Message);
            }

            return;
        }

        try
        {
            await AppendOutcomeMetricAsync(result);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Auto delay outcome metric failed: {exception}");
            if (!IsDisposed && IsHandleCreated)
            {
                ShowError(
                    "Auto delay was applied, but the outcome metric could not " +
                    "be computed.",
                    "The settings are in place; only the diagnostic log is " +
                    "missing its final metric.\r\n\r\n" + exception.Message);
            }
        }
    }

    private void CommitAutoDelayResult(AutoDelayRunResult result)
    {
        VirtualCrossoverAutoDelay.Commit(result, session.Project);
        foreach (VirtualCrossoverChannel runtime in
            result.Outcomes.Select(outcome => outcome.Runtime).Distinct())
        {
            ApplySettingsToControl(runtime);
        }

        // "Keep the hidden side's polarity" is invisible to the lock as a difference, so re-remember the result.
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        SaveAndRedraw();
        VirtualCrossoverAutoDelay.WriteLog(result.Log.ToString());
    }

    private async Task AppendOutcomeMetricAsync(AutoDelayRunResult result)
    {
        // RedrawAll pushes the read-out asynchronously, so recompute here; capture the side before the await.
        bool metricSideRight = session.Project.ActiveSideRight;
        ProcessedRender? render = await ProcessChannelsAsync();
        List<ProcessedChannel> outcomeChannels = render?.Channels ?? [];
        (_, _, List<SignalPoint>? outcomeLoss) =
            metrics.BuildCurves(outcomeChannels, session.MagnitudeGate.SmoothingInverseOctaves);
        result.Log.AppendLine(
            $"Metric ({(metricSideRight ? "R" : "L")} side):");
        result.Log.AppendLine(VirtualCrossoverMetric.FormatDetail(
            metrics.BuildEntries(outcomeChannels, outcomeLoss)));
        VirtualCrossoverAutoDelay.WriteLog(result.Log.ToString());
    }

    // Its own pin or anchor, never the active side's.
    private AnalysisCurve BuildOppositeMagnitudeCurve(VirtualCrossoverSideSum side)
    {
        MagnitudeGateSnapshot snapshot = session.MagnitudeGate;
        return snapshot.MeasuredSum(
            side.Channels,
            side.AnchorIndex,
            snapshot.ResolveGateOffsetMs(
                oppositeSide: true, side.AnchorIndex, side.SampleRate),
            session.Calibration.For).Display;
    }

    // Both automatic commands are verified on the gated view, so a misplaced gate refuses them.
    private bool GateIsMisplaced => gatePlacement is { CutsChannels: true };

    private bool RefuseOnMisplacedGate(string command)
    {
        if (gatePlacement is not { CutsChannels: true } verdict)
        {
            return false;
        }

        ShowError(verdict.FormatRefusal(command), verdict.FormatDetail());
        return true;
    }

    private async Task OpenPhaseGateDialogAsync()
    {
        ProcessedRender? render = await ProcessChannelsAsync();
        if (render == null)
        {
            return;
        }
        List<ProcessedChannel> processed = render.Channels;
        if (processed.Count == 0)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        int sampleRate = processed[0].SampleRate;
        int reference = ProcessedChannels.SharedStartAnchorIndex(processed);
        double fitOffsetMs = PhaseGatePlacement.EarliestStartMs(
            PlacementChannel.From(processed), sampleRate);

        var traces = processed
            .Select(item => new IrPreviewTrace(
                item.ImpulseResponse,
                item.Channel.Name,
                item.Color))
            .ToList();

        VirtualCrossoverPhaseGate stored = VirtualCrossoverPhaseGate.For(
            session.Project, session.Project.ActiveSideRight, preview: null);
        using var dialog = new VirtualCrossoverGateDialog();
        dialog.Init(
            traces,
            sampleRate,
            stored.SharedOffsetMs(processed, sampleRate),
            stored.LeftMs,
            stored.PlateauMs,
            stored.RightMs,
            // One τ for every curve keeps relative phase through the detrend.
            stored.StoredDetrendMs ?? reference * 1_000.0 / sampleRate,
            stored.WindowMode,
            stored.FdwCycles,
            stored.DetrendMode,
            fitOffsetMs,
            autoOffset: stored.StoredOffsetMs == null);
        // Wired after Init so seeding the controls does not redraw.
        dialog.PreviewChanged = (offsetMs, autoOffset, leftMs, plateauMs, rightMs,
            windowMode, fdwCycles, detrendMode, detrendMs) =>
        {
            session.GatePreview = new VirtualCrossoverGatePreview(
                offsetMs, autoOffset, leftMs, plateauMs, rightMs,
                windowMode, fdwCycles, detrendMode, detrendMs);
            RequestRedraw();
        };

        try
        {
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            {
                // Only the placement is per side; lengths and modes are project-wide.
                VirtualCrossoverPhaseGateSettings gate =
                    session.Project.PhaseGateFor(session.Project.ActiveSideRight);
                // Auto = null: keeps following the earliest channel IR start.
                gate.OffsetMs = dialog.AutoOffset ? null : dialog.GateOffsetMs;
                gate.DetrendMs = dialog.DetrendMs;
                session.Project.PhaseGateLeftMs = dialog.LeftMs;
                session.Project.PhaseGatePlateauMs = dialog.PlateauMs;
                session.Project.PhaseGateRightMs = dialog.RightMs;
                session.Project.PhaseWindowMode = dialog.WindowMode;
                session.Project.PhaseFdwCycles = dialog.FdwCycles;
                session.Project.PhaseDetrendMode = dialog.DetrendMode;
                ScheduleSave();
            }
        }
        finally
        {
            session.GatePreview = null;
            RequestRedraw();
        }
    }

    private ProcessedRender? lastProcessedRender;

    // The offset belongs to the capture SET; one handed-over channel could not re-derive it.
    private (long Revision, double OffsetDb)? lastHybrid;

    /// <summary>The spatial average handed to the EQ Wizard, or null when the hybrid is not drawn.</summary>
    /// <remarks>Cheap and redraw-independent so the handoff and the return guard cannot disagree mid-redraw.</remarks>
    private LiveCaptureDocument? HybridHandoffCapture(
        VirtualCrossoverChannel channel, bool rightSide) =>
        HybridRequested
            ? channel.SideState(rightSide).SpatialAverageFor(session.SpatialAverageMode)
            : null;

    /// <summary>The capture plus its offset onto the IR axis; resolved here when no current magnitude render carries it.</summary>
    private (LiveCaptureDocument? Capture, double OffsetDb) HandoffSpatialAverage(
        VirtualCrossoverChannel channel, bool rightSide)
    {
        if (HybridHandoffCapture(channel, rightSide) is not { } capture)
        {
            return (null, 0.0);
        }

        if (lastHybrid is { } cached && processingCoordinator.IsCurrent(cached.Revision))
        {
            return (capture, cached.OffsetDb);
        }

        if (lastProcessedRender is not { } render ||
            !processingCoordinator.IsCurrent(render.Revision))
        {
            return (null, 0.0);
        }

        (List<AnalysisCurve>? magnitudes, _, _) =
            metrics.BuildCurves(render.Channels, session.MagnitudeGate.SmoothingInverseOctaves);
        if (magnitudes == null ||
            hybridReader.Build(
                render.Channels,
                magnitudes,
                rightSide,
                session.MagnitudeGate.SmoothingInverseOctaves) is not { } hybrid)
        {
            return (null, 0.0);
        }

        return (capture, hybrid.OffsetDb);
    }

    // Single-flight like the main redraw: stacked tasks would each burn a full sweep of inverse FFTs.
    private Task? correlationRebuildTask;
    private bool correlationRebuildPending;

    private bool suppressCorrelationPairEvents;

    private void RedrawDspPlot()
    {
        if (IsJunctionMode(CurrentDspPlotMode()))
        {
            UpdateCorrelationPairChoices();
            RequestCorrelationRedraw();
            return;
        }

        using var _ = AppProfiler.Zone("VirtualDSP.RedrawDspPlot");
        var curves = new List<DspChainCurve>();
        for (int i = 0; i < session.Channels.Count; i++)
        {
            VirtualCrossoverChannel channel = session.Channels[i];
            if (!channel.Pair.Enabled || channel.TransferImpulseResponse == null)
            {
                continue;
            }

            // Drawn without the delay term: a bulk delay wraps phase into a sawtooth and swamps the filter GD.
            DspChannelChain chain = channel.Pair.Bypass
                ? DspChannelChain.Identity
                : channel.Settings.ToChain(channel.Pair.Zone) with { DelayMs = 0 };
            curves.Add(new DspChainCurve(
                $"{channel.Name} filter", chain, session.ProcessorSampleRateHz, VirtualCrossoverColors.Channel(i)));
        }

        dspChainPlot.Draw(CurrentDspPlotMode(), curves);
    }

    // From the last processed snapshot, narrowed to the view's summing chain (ProcessedChannels.JunctionsInView).
    private List<AdjacentPair> CurrentCorrelationPairs() =>
        lastProcessedRender is { } render
            ? ProcessedChannels.JunctionsInView(render.Channels, SelectedGroupView)
            : [];

    private void UpdateCorrelationPairChoices()
    {
        List<AdjacentPair> pairs = CurrentCorrelationPairs();
        List<string> labels = pairs
            .Select(pair => $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}")
            .ToList();
        bool changed = comboBoxCorrelationPair.Items.Count != labels.Count;
        for (int i = 0; !changed && i < labels.Count; i++)
        {
            changed = !Equals(comboBoxCorrelationPair.Items[i], labels[i]);
        }

        int wanted = Math.Clamp(
            session.Project.CorrelationPairIndex, 0, Math.Max(0, labels.Count - 1));
        if (!changed && comboBoxCorrelationPair.SelectedIndex == wanted)
        {
            return;
        }

        suppressCorrelationPairEvents = true;
        try
        {
            if (changed)
            {
                comboBoxCorrelationPair.Items.Clear();
                foreach (string label in labels)
                {
                    comboBoxCorrelationPair.Items.Add(label);
                }
            }

            if (labels.Count > 0)
            {
                comboBoxCorrelationPair.SelectedIndex = wanted;
            }
        }
        finally
        {
            suppressCorrelationPairEvents = false;
        }

        comboBoxCorrelationPair.Enabled =
            JunctionPlotModeSelected() && labels.Count > 0;
    }

    private void RequestCorrelationRedraw()
    {
        if (correlationRebuildTask is { IsCompleted: false })
        {
            correlationRebuildPending = true;
            return;
        }

        correlationRebuildTask = RunCorrelationRebuildLoopAsync();
    }

    private async Task RunCorrelationRebuildLoopAsync()
    {
        do
        {
            correlationRebuildPending = false;
            await RedrawCorrelationPlotAsync();
        }
        while (correlationRebuildPending && !dspPlotView.IsDisposed &&
            IsJunctionMode(CurrentDspPlotMode()));

        correlationRebuildTask = null;
    }

    // Shared by both junction modes; a result is dropped if the user switched mode mid-compute.
    private async Task RedrawCorrelationPlotAsync()
    {
        DspPlotMode mode = CurrentDspPlotMode();
        List<AdjacentPair> pairs = CurrentCorrelationPairs();
        if (pairs.Count == 0)
        {
            if (mode == DspPlotMode.Coherence)
            {
                dspChainPlot.DrawCoherence(null);
            }
            else
            {
                dspChainPlot.DrawCorrelation(null);
            }

            return;
        }

        AdjacentPair pair = pairs[Math.Clamp(
            session.Project.CorrelationPairIndex, 0, pairs.Count - 1)];
        JunctionCorrelationView? correlation = null;
        JunctionCoherenceView? coherence = null;
        try
        {
            List<ProcessedChannel> scope = lastProcessedRender is { } render
                ? render.Channels.ToList()
                : [pair.Lower, pair.Upper];
            if (mode == DspPlotMode.Coherence)
            {
                coherence = await Task.Run(() => JunctionViews.BuildCoherenceView(pair, scope));
            }
            else
            {
                correlation = await Task.Run(() => JunctionViews.BuildCorrelationView(pair, scope));
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Junction view rebuild failed: {exception}");
        }

        if (dspPlotView.IsDisposed || CurrentDspPlotMode() != mode)
        {
            return;
        }

        if (correlationRebuildPending)
        {
            return;
        }

        if (coherence != null)
        {
            dspChainPlot.DrawCoherence(coherence);
        }
        else if (correlation != null)
        {
            dspChainPlot.DrawCorrelation(correlation);
        }
    }

    private async Task CaptureSumToOverlayAsync()
    {
        ProcessedRender? render = await ProcessChannelsAsync();
        if (render == null)
        {
            return;
        }
        List<ProcessedChannel> processed = render.Channels;
        if (processed.Count < 2 || OverlayCaptureRequested == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        int overlayAnchor = processed.Min(item => item.PeakIndex);
        MagnitudeGateSnapshot overlayGate = session.MagnitudeGate;
        AnalysisCurve sumCurve = overlayGate.MeasuredSum(
            processed,
            overlayAnchor,
            overlayGate.ResolveGateOffsetMs(
                oppositeSide: false, overlayAnchor, processed[0].SampleRate),
            session.Calibration.For).Display;

        string title = "vDSP Sum " + string.Join(
            "+",
            processed.Select(item => item.Channel.Name));
        OverlayPoint[] points = sumCurve.Points
            .Select(point => new OverlayPoint(point.X, point.Y))
            .ToArray();

        int? slot = OverlayCaptureRequested(title, points);
        if (slot.HasValue)
        {
            MessageBox.Show(
                FindForm(),
                $"The virtual sum was saved as overlay slot {slot.Value} in " +
                "Frequency Response.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            ShowError(
                "No free overlay slot.",
                "All twelve Frequency Response overlay slots are occupied; " +
                "clear one and try again.");
        }
    }

    private async Task ExportTuningSheetAsync()
    {
        PeqQConvention? qConvention = AskSheetQConvention();
        if (qConvention == null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "pdf",
            FileName = "virtual-dsp",
            Filter = "Tuning sheet (PDF) (*.pdf)|*.pdf|Tuning sheet (text) (*.txt)|*.txt",
            Title = "Export Virtual DSP tuning sheet"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        ProcessedRender? render = await ProcessChannelsAsync();
        if (render == null)
        {
            return;
        }
        List<ProcessedChannel> metricChannels = render.Channels;
        (_, _, List<SignalPoint>? metricLoss) =
            metrics.BuildCurves(metricChannels, session.MagnitudeGate.SmoothingInverseOctaves);
        string metricLine = VirtualCrossoverMetric.FormatLabel(
            metrics.BuildEntries(metricChannels, metricLoss));
        // The chain graph shows the filters, so it uses the processor's rate, not the measurement rate.
        int sampleRate = session.ProcessorSampleRateHz;
        try
        {
            if (dialog.FilterIndex == 1)
            {
                VirtualCrossoverSheetPdf.Export(
                    dialog.FileName, session.Project, metricLine, sampleRate, qConvention.Value);
            }
            else
            {
                AtomicFile.WriteAllText(
                    dialog.FileName,
                    VirtualCrossoverSheet.FormatText(
                        session.Project, metricLine, qConvention.Value));
            }

            // Remember the answer only once a sheet reached the disk.
            sheetQConvention = qConvention;
        }
        catch (Exception exception)
        {
            ShowError("The tuning sheet could not be exported.", exception.Message);
        }
    }

    // A known model states its Q convention; a Custom profile asks, pre-selected with the last exported answer.
    // Null when cancelled.
    private PeqQConvention? AskSheetQConvention()
    {
        DspProcessorProfile profile = session.ProcessorProfile;
        if (!profile.IsCustom)
        {
            return profile.QConvention;
        }

        using var dialog = new TuningSheetQConventionDialog(
            sheetQConvention ?? profile.QConvention);
        return dialog.ShowDialog(FindForm()) == DialogResult.OK
            ? dialog.SelectedConvention
            : null;
    }

    /// <summary>Opens the crossover wizard and writes what it proposes (<see cref="VirtualCrossoverAutoSetup"/>).</summary>
    /// <returns>Null when written; otherwise a refusal phrase an import's summary can quote.</returns>
    private string? OpenAutoSetupWizard()
    {
        List<VirtualCrossoverChannel> participating = VirtualCrossoverAutoSetup.Participants(session);
        if (participating.Count < 2)
        {
            System.Media.SystemSounds.Beep.Play();
            return "fewer than two enabled channels have a measurement";
        }

        // The band read is gate-independent, but the result is checked on gated views, so refuse a misplaced gate.
        if (RefuseOnMisplacedGate("Auto crossover"))
        {
            return "the phase gate is misplaced";
        }

        List<AutoSetupWizardChannel> dialogChannels;
        // Building an FDW curve per channel is the one stretch before the dialog appears; without this the button
        // looks like it did nothing for the best part of a second.
        UseWaitCursor = true;
        try
        {
            dialogChannels = VirtualCrossoverAutoSetup.ReadChannels(session, participating);
        }
        catch (ArgumentException exception)
        {
            ShowError("A channel's response has no usable band.", exception.Message);
            return "a channel's response has no usable band";
        }
        finally
        {
            UseWaitCursor = false;
        }

        using var dialog = new VirtualCrossoverAutoSetupDialog();
        dialog.Init(
            participating[0].SampleRate,
            session.ProcessorSampleRateHz,
            dialogChannels);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } proposals)
        {
            return "cancelled in the wizard";
        }

        int clearedRotations = VirtualCrossoverAutoSetup.Write(participating, proposals);
        foreach (VirtualCrossoverChannel channel in participating)
        {
            ApplySettingsToControl(channel);
        }

        if (dialog.ChainOrder is { } chainOrder)
        {
            ApplyChannelOrder(VirtualCrossoverAutoSetup.Reorder(session.Channels, participating, chainOrder));
        }

        // The wizard wrote both sides; the lock must not carry the shown side's other edge over.
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        SaveAndRedraw();
        if (clearedRotations > 0)
        {
            MessageBox.Show(
                this,
                $"{clearedRotations} channel side" +
                (clearedRotations == 1 ? " had" : "s had") +
                " a phase rotation dialled in.\r\n\r\nThat control states its angle " +
                "at the channel's crossover, so the filter it built is not the one " +
                "the same number would build at the new corners — the rotations " +
                "were cleared rather than left meaning something else. Dial them " +
                "in again against the crossovers this run chose.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return null;
    }

    private void ExportSession()
    {
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "json",
            FileName = "virtual-dsp-session",
            Filter = "Virtual DSP session (*.json)|*.json|All files (*.*)|*.*",
            Title = "Save Virtual DSP session"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            session.Project.SaveTo(dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowError("The session could not be saved.", exception.Message);
        }
    }

    // The import immediately becomes the new internal autosave.
    private async Task ImportSessionAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Virtual DSP session (*.json)|*.json|All files (*.*)|*.*",
            Title = "Load Virtual DSP session"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        await ImportSessionFileAsync(dialog.FileName);
    }

    /// <summary>Imports the session at <paramref name="path"/> like Load; reports its own failures.</summary>
    /// <remarks>Waits for the stored project load a tab switch just started, which would otherwise restore the replaced session.</remarks>
    internal async Task ImportSessionFileAsync(string path)
    {
        await storedProjectLoad;

        VirtualCrossoverProjectFile imported;
        try
        {
            imported = VirtualCrossoverProjectFile.LoadFrom(path);
        }
        catch (Exception exception)
        {
            ShowError("The session could not be loaded.", exception.Message);
            return;
        }

        await ApplyProjectAsync(imported, imported: true);
        NotifyIfMigrationCostAFilter(imported.MigrationNoticeText);
        ScheduleSave();
        await RelinkMissingSourcesAsync();
        ShowCalibrationNotice();
    }

    // Stored paths come from the measuring machine; one folder answers for all missing sources and stays a search root.
    private async Task RelinkMissingSourcesAsync()
    {
        List<(VirtualCrossoverChannel Channel, bool RightSide)> missing =
            session.MissingSourceSides().ToList();
        if (missing.Count == 0 || IsDisposed)
        {
            return;
        }

        if (MessageBox.Show(
                FindForm(),
                $"{DescribeMissingSources(missing)}\r\n\r\nThey were saved with this " +
                "session's own paths, which do not exist on this computer. Point at " +
                "the folder holding the measurements?",
                "Virtual DSP",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder holding this session's measurements",
            UseDescriptionForTitle = true,
            SelectedPath = session.Project.ProjectDirectory ?? string.Empty
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        session.RelinkDirectory = dialog.SelectedPath;
        SetProjectLoading(true);
        try
        {
            foreach ((VirtualCrossoverChannel channel, bool rightSide) in missing)
            {
                await ResolveSourceAsync(channel, rightSide, showErrors: false);
            }

            foreach (VirtualCrossoverChannel channel in
                missing.Select(item => item.Channel).Distinct())
            {
                UpdateSourceButton(channel);
            }

            UpdateSideRadioTexts();
        }
        finally
        {
            SetProjectLoading(false);
            RedrawAll();
        }

        ScheduleSave();

        List<(VirtualCrossoverChannel Channel, bool RightSide)> remaining =
            session.MissingSourceSides().ToList();
        if (remaining.Count > 0 && !IsDisposed)
        {
            MessageBox.Show(
                FindForm(),
                $"{DescribeMissingSources(remaining)}\r\n\r\nThe folder holds no file " +
                "under the name each channel was saved with. Pick those measurements " +
                "with the channel's Source button, or import the session again to " +
                "choose a different folder.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static string DescribeMissingSources(
        IReadOnlyList<(VirtualCrossoverChannel Channel, bool RightSide)> missing)
    {
        string sides = string.Join(
            ", ",
            missing.Select(item => item.Channel.SideLabel(item.RightSide)));
        return missing.Count == 1
            ? $"The measurement of channel {sides} was not found."
            : $"{missing.Count} measurements were not found: {sides}.";
    }

    // A session carrying its curve starts on it and offers to keep it; an id-only match is reported as such,
    // and a miss keeps the panel's selection.
    private void ShowCalibrationNotice()
    {
        VirtualCrossoverCalibrationNotice notice = pendingCalibrationNotice;
        pendingCalibrationNotice = VirtualCrossoverCalibrationNotice.None;
        if (IsDisposed)
        {
            return;
        }

        switch (notice)
        {
            case VirtualCrossoverCalibrationNotice.CarriedBySession
                when sessionCalibration is { } carried:
                OfferSessionCalibration(carried);
                break;

            case VirtualCrossoverCalibrationNotice.MatchedBySlotName:
                MessageBox.Show(
                    FindForm(),
                    "This session names its microphone calibration by a slot only " +
                    $"('{session.Calibration.SelectedName}'), without the curve itself — it " +
                    "was written by an older version. This computer's entry of the " +
                    "same name is selected, but nothing says the two files agree: " +
                    "check that it is the calibration of the microphone these " +
                    "measurements were taken with.",
                    "Virtual DSP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                break;

            case VirtualCrossoverCalibrationNotice.KeptPrevious:
                string kept = session.Calibration.SelectedName is { } name
                    ? $"The '{name}' calibration this panel already had is kept"
                    : "The curves are drawn without any calibration, as before";
                MessageBox.Show(
                    FindForm(),
                    "This session was tuned with a microphone calibration that is not " +
                    "configured on this computer, and it was written by an older " +
                    $"version that did not store the curve itself. {kept}; the " +
                    "curves may not match the ones its author saw.",
                    "Virtual DSP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                break;
        }
    }

    // Keeping it is right for the author's measurements, wrong for ones taken here with another microphone.
    private void OfferSessionCalibration(VirtualCrossoverSessionCalibration carried)
    {
        if (calibrationAdder == null)
        {
            MessageBox.Show(
                FindForm(),
                $"This session carries the microphone calibration {carried.Description} " +
                "it was tuned with, and it is selected, so the curves match the ones " +
                "its author saw.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        DialogResult answer = MessageBox.Show(
            FindForm(),
            $"This session carries the microphone calibration {carried.Description} " +
            "it was tuned with, and it is selected, so the curves match the ones its " +
            "author saw.\r\n\r\nAdd it to your calibrations (Record Settings → More " +
            "calibrations) so the other views can use it too? Say yes if these " +
            "measurements were taken with that microphone; a measurement you take " +
            "with your own microphone needs its own calibration.",
            "Virtual DSP",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        string? addedId = calibrationAdder(carried);
        if (addedId == null)
        {
            return;
        }

        // Only for a host that did not refresh consumers on adding.
        if (!string.Equals(
                MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration),
                addedId,
                StringComparison.OrdinalIgnoreCase))
        {
            sessionCalibration = null;
            ApplyCalibrationSelection(addedId);
            PersistCalibrationSelection();
            ScheduleSave();
        }
    }

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
