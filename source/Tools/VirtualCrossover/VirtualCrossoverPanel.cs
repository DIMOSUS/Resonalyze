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

    private const string NoSourcesHint =
        "Pick a measurement for at least one channel (Source...).\n" +
        "Every source needs a loopback transfer IR recorded at the same\n" +
        "microphone position and sample rate.";

    private static readonly OxyColor SumColor = OxyColors.White;
    private static readonly OxyColor LossColor = VirtualCrossoverAcousticPlot.LossAxisColor;
    private static readonly OxyColor[] ChannelColors =
    [
        OxyColor.FromRgb(86, 156, 255),
        OxyColor.FromRgb(255, 150, 64),
        OxyColor.FromRgb(96, 210, 120),
        OxyColor.FromRgb(200, 130, 255),
        OxyColor.FromRgb(80, 210, 220),
        OxyColor.FromRgb(240, 100, 140),
        OxyColor.FromRgb(210, 200, 90),
        OxyColor.FromRgb(140, 200, 90),
        // I–L must stay distinct from A–H on the dark ground; a saturated red would read as a warning.
        OxyColor.FromRgb(230, 120, 90),
        OxyColor.FromRgb(150, 175, 215),
        OxyColor.FromRgb(215, 180, 140),
        OxyColor.FromRgb(90, 180, 175)
    ];

    private readonly System.Windows.Forms.Timer saveTimer = new()
    {
        Interval = SaveDebounceMilliseconds
    };

    // Magnitude reads through the same gate as the phase/impulse views. Swapped atomically on the UI thread
    // by RequestRedraw, read by PLINQ workers. See docs/tech/virtual-dsp-panel.md#gate-snapshot.
    internal sealed record MagnitudeGateSnapshot(
        PhaseAnalysisSettings Template,
        double? PinnedOffsetMs,
        double? OppositePinnedOffsetMs,
        int SmoothingInverseOctaves)
    {
        // Each side has its own pin: the active side's pin must never window the opposite side's sum.
        internal double ResolveGateOffsetMs(
            bool oppositeSide,
            int anchorPeakIndex,
            int sampleRate) =>
            (oppositeSide ? OppositePinnedOffsetMs : PinnedOffsetMs)
                ?? anchorPeakIndex * 1_000.0 / sampleRate;
    }

    private MagnitudeGateSnapshot magnitudeGate = new(
        new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed,
            PhaseAnalysisSettings.DefaultFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: 0.0,
            LeftMs: FrequencyResponseOptions.SteadyStateLeftMs,
            PlateauMs: FrequencyResponseOptions.SteadyStatePlateauMs,
            RightMs: FrequencyResponseOptions.SteadyStateRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0),
        PinnedOffsetMs: null,
        OppositePinnedOffsetMs: null,
        SmoothingInverseOctaves: 12);

    private readonly List<VirtualCrossoverChannel> channels = new();
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

    private VirtualCrossoverProjectFile project = new();

    // Extra search root from relinking an imported session's missing measurements; cleared on bind.
    private string? relinkDirectory;

    // Gate dialog candidates while it is open (live preview); null once closed. AutoOffset gates per curve
    // as Save will, while OffsetMs is where the dialog's window is drawn.
    private (double OffsetMs, bool AutoOffset, double LeftMs, double PlateauMs,
        double RightMs, PhaseWindowMode WindowMode, int FdwCycles,
        PhaseDetrendMode DetrendMode, double DetrendMs)? gatePreview;
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
        // While controls stand where the designer put them: the layout pass stretches plots by deltas on this.
        CaptureLayoutBaseline();
        Ui.DarkScrollBars.Apply(channelListPanel);
        Ui.DarkScrollBars.Apply(this);
        SetChannelCount(DefaultChannelCount);

        checkBoxShowSum.ForeColor = Color.FromArgb(SumColor.R, SumColor.G, SumColor.B);
        labelSumLoss.ForeColor = Color.FromArgb(LossColor.R, LossColor.G, LossColor.B);
        targetToggleColor = checkBoxShowTarget.ForeColor;
        hybridToggleColor = checkBoxHybrid.ForeColor;

        metrics = new VirtualCrossoverMetrics(
            processingCoordinator,
            BuildMagnitudeCurve,
            CalibrationFor,
            BuildMeasuredSumCurve);
        acousticPlot = new VirtualCrossoverAcousticPlot(
            mainPlotView, NoSourcesHint, CurrentAcousticView());
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

    private CalibrationFile? Calibration { get; set; }

    /// <summary>"Own (as measured)": each curve uses its measurement's calibration; <see cref="Calibration"/> is null.</summary>
    private bool ownCalibrationSelected;

    /// <summary>Under Own, null for a measurement naming no calibration: never substitute the panel's.</summary>
    private CalibrationFile? CalibrationFor(ProcessedChannel channel) =>
        ownCalibrationSelected ? channel.MicrophoneCalibration : Calibration;

    private CalibrationFile? CalibrationFor(VirtualCrossoverChannelState state) =>
        ownCalibrationSelected ? state.MicrophoneCalibrationCurve : Calibration;

    /// <summary>Under Own, the capture's own correction (a moving-mic pass or array has its own), not this side's file.</summary>
    private SpatialAverageCalibration SpatialAverageCalibrationFor(
        VirtualCrossoverChannelState state) =>
        ownCalibrationSelected
            ? SpatialAverageCalibration.Own
            : SpatialAverageCalibration.Specific(Calibration);

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

    private const string LoadingHint = "Loading the previous session…";

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
            acousticPlot.ShowHint(LoadingHint);
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
        project = newProject;
        relinkDirectory = null;
        // The previous import's undo would restore into settings nobody displays.
        agentUndo = null;
        // Channel objects are reused across binds; this tells an EQ Wizard handoff which project it came from.
        projectGeneration++;
        SetChannelCount(project.Pairs.Count);

        suppressProjectEvents = true;
        try
        {
            comboBoxSumLoss.SelectedItem = project.SumLossWindowMode;
            // Intent only: captures attach as sources resolve, and HybridRequested also needs coverage.
            checkBoxHybrid.Checked = project.ShowHybridCurves;
            checkBoxShowTarget.Checked = project.ShowTargetCurve;
            numericTargetLevel.Value =
                numericTargetLevel.ClampValue(project.TargetLevelDb);
            // Each newer view flag is written beside the older one it falls back to.
            radioViewStep.Checked = project.ShowStepView;
            radioViewImpulse.Checked =
                !project.ShowStepView && project.ShowImpulseView;
            radioViewGroupDelay.Checked =
                !project.ShowStepView && !project.ShowImpulseView &&
                project.ShowGroupDelayView;
            radioViewPhase.Checked =
                !project.ShowStepView && !project.ShowImpulseView &&
                !project.ShowGroupDelayView && project.ShowPhaseView;
            radioViewMagnitude.Checked =
                !project.ShowStepView && !project.ShowImpulseView &&
                !project.ShowGroupDelayView && !project.ShowPhaseView;
            // After the radios: the Sum toggle is remembered per view.
            ApplySumToggleForView();
            ApplyProjectTarget();
            radioSideRight.Checked = project.ActiveSideRight;
            radioSideLeft.Checked = !project.ActiveSideRight;
            acousticPlot.ConfigureForView(CurrentAcousticView());
            comboBoxSmoothing.SelectedItem =
                OverlaySmoothing.IsValid(project.SmoothingCode)
                    ? project.SmoothingCode
                    : 12;
            comboBoxGroupView.SelectedItem = project.GroupView;
            if (VirtualCrossoverGroupViews.DrawsGroupSums(project.GroupView))
            {
                radioViewMagnitude.Checked = true;
            }
            radioDspMagnitude.Checked =
                project.EffectiveDspPlotMode == DspPlotMode.Magnitude;
            radioDspPhase.Checked =
                project.EffectiveDspPlotMode == DspPlotMode.Phase;
            radioDspGroupDelay.Checked =
                project.EffectiveDspPlotMode == DspPlotMode.GroupDelay;
            radioDspCorrelation.Checked =
                project.EffectiveDspPlotMode == DspPlotMode.Correlation;
            radioDspCoherence.Checked =
                project.EffectiveDspPlotMode == DspPlotMode.Coherence;
            comboBoxCorrelationPair.Enabled = JunctionPlotModeSelected() &&
                comboBoxCorrelationPair.Items.Count > 0;

            // Before filling blocks: it re-pins their height once instead of per block.
            RefreshProcessorRowAvailability();
            for (int i = 0; i < channels.Count; i++)
            {
                channels[i].Pair = project.Pairs[i];
                channels[i].ActiveRight = project.ActiveSideRight;
                ApplySettingsToControl(channels[i]);
            }
        }
        finally
        {
            suppressProjectEvents = false;
        }

        // Restart the lock from the loaded pairs, or the first edit is recorded as a starting state.
        sideLock.Remember(channels.Select(channel => channel.Pair));

        // Selector events were silenced above; refresh the view-muted controls from the landed state.
        UpdateViewDependentControls();

        BindCalibrationSelection(imported, previousCalibrationId, previousSession);

        await RestoreProjectSourcesAsync(
            channels,
            channel => channel.Pair.Mono,
            channel =>
            {
                channel.PhysicalSideState(false).Clear();
                channel.PhysicalSideState(true).Clear();
            },
            (channel, rightSide) =>
                ResolveSourceAsync(channel, rightSide, showErrors: false),
            UpdateSourceButton);

        // After sources (an array brings one): settle the averaging method once and redraw the buttons drawn earlier.
        if (SettleSpatialAverageMode())
        {
            foreach (VirtualCrossoverChannel channel in channels)
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

    // Wipe BOTH slots of EVERY channel before any source resolves: TryAssignSource's rate guard votes over
    // the resolved sides. See docs/tech/virtual-dsp-panel.md#project-restore-order.
    internal static async Task RestoreProjectSourcesAsync<TChannel>(
        IReadOnlyList<TChannel> channels,
        Func<TChannel, bool> isMono,
        Action<TChannel> clearBothSlots,
        Func<TChannel, bool, Task> resolveSide,
        Action<TChannel> channelRestored)
    {
        foreach (TChannel channel in channels)
        {
            clearBothSlots(channel);
        }

        foreach (TChannel channel in channels)
        {
            foreach (bool rightSide in new[] { false, true })
            {
                if (isMono(channel) && rightSide)
                {
                    continue;
                }

                await resolveSide(channel, rightSide);
            }

            channelRestored(channel);
        }
    }

    private void ScheduleSave()
    {
        // Every change passes through here, so the side lock reads here, ahead of the redraw and the save.
        sideLock.Follow(channels.Select(channel => channel.Pair), project.ActiveSideRight);
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
            project.Save();
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
            : project.CalibrationId;
        if (sessionCalibration is { } session && calibrationResolver is { } resolve)
        {
            MicrophoneCalibrationEntry? same = calibrationEntries.FirstOrDefault(entry =>
                entry.Available &&
                CalibrationFile.SameCurve(resolve(entry.Id), session.Curve));
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
                project.CalibrationId,
                project.Calibration,
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

    private void ResolveCalibration()
    {
        string? selected =
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration);
        ownCalibrationSelected = VirtualCrossoverCalibrationSelection.IsOwn(selected);
        // Null under Own: one field cannot hold a per-channel answer.
        Calibration = ownCalibrationSelected ? null : ResolveSelectedCalibration(selected);
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
                project.CalibrationId,
                project.Calibration);
        bool changed =
            !string.Equals(id, project.CalibrationId, StringComparison.OrdinalIgnoreCase) ||
            !SameStoredCurve(calibration, project.Calibration);
        project.CalibrationId = id;
        project.Calibration = calibration;
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
        ScheduleSave();
        RedrawAll();
    }

    // The curve itself, not an id: the session's own curve has no id the wizard could resolve.
    private string? SelectedCalibrationName() =>
        MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration) is { } id
            ? CalibrationEntriesWithSession()
                .FirstOrDefault(entry =>
                    string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                ?.Name
            : null;

    /// <summary>Under Own the name follows the file's own curve: the wizard's disabled selector shows it.</summary>
    private string? CalibrationNameFor(VirtualCrossoverChannelState state) =>
        ownCalibrationSelected
            ? state.MicrophoneCalibration?.Name
            : SelectedCalibrationName();

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
        project.ActiveSideRight = rightSide;
        suppressProjectEvents = true;
        try
        {
            foreach (VirtualCrossoverChannel channel in channels)
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
        ScheduleSave();
        RedrawAll();
    }

    // The source is never copied (each side has its own measurement); mono pairs are not offered.
    private void CopySideSettings(bool fromRight)
    {
        List<VirtualCrossoverChannel> candidates = channels
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
        bool targetSideShown = project.ActiveSideRight == !fromRight;
        foreach (int index in dialog.SelectedIndices)
        {
            VirtualCrossoverChannel channel = candidates[index];
            CopyChainSettings(
                channel.SideSettings(fromRight),
                channel.SideSettings(!fromRight),
                scope);
            if (targetSideShown)
            {
                ApplySettingsToControl(channel);
            }
        }

        ScheduleSave();
        RedrawAll();
    }

    // Engaging copies nothing; see docs/tech/virtual-dsp-panel.md#side-lock. On by default, not stored.
    private void OnSideLockChanged()
    {
        if (checkBoxSideLock.Checked)
        {
            sideLock.Engage(channels.Select(channel => channel.Pair));
        }
        else
        {
            sideLock.Release();
        }
    }

    // Defaults tick crossover and PEQ (driver shape); gain, delay, polarity and all-pass are opt-in because
    // they align against one side's geometry. See docs/tech/virtual-dsp-panel.md#copying-between-sides.
    private static void CopyChainSettings(
        VirtualCrossoverChannelSettings from,
        VirtualCrossoverChannelSettings to,
        VirtualCrossoverCopyScope scope)
    {
        if (scope.Gain)
        {
            to.GainDb = from.GainDb;
        }

        if (scope.Delay)
        {
            to.DelayMs = from.DelayMs;
        }

        if (scope.InvertPolarity)
        {
            to.InvertPolarity = from.InvertPolarity;
        }

        if (scope.Crossover)
        {
            to.CrossoverKind = from.CrossoverKind;
            to.HighPassEdge = from.HighPassEdge;
            to.LowPassEdge = from.LowPassEdge;
        }

        // A timing decision like the delay, so its own tick; copied as the number, the reference follows the target's crossover.
        if (scope.Phase)
        {
            to.PhaseRotationDegrees = from.PhaseRotationDegrees;
        }

        // Immutable kernel, shared by reference.
        if (scope.Fir)
        {
            to.Fir = from.Fir;
            to.FirSourceName = from.FirSourceName;
            to.FirDesign = from.FirDesign;
        }

        // All-pass filters live in the PEQ bank; Peq and AllPass split that one list by band type.
        if (scope.Peq || scope.AllPass)
        {
            List<PeqBand> tonal = (scope.Peq ? from : to)
                .PeqBands.Where(band => !band.Type.IsAllPass()).ToList();
            List<PeqBand> allPass = (scope.AllPass ? from : to)
                .PeqBands.Where(band => band.Type.IsAllPass()).ToList();
            // Over the slot budget the COPIED kind gives way (an unticked scope promised the target's bands stay);
            // with both copied the all-pass stays.
            int overflow =
                tonal.Count + allPass.Count - EqualizationCurve.MaxBandCount;
            if (overflow > 0)
            {
                if (scope.Peq)
                {
                    tonal = tonal.Take(Math.Max(0, tonal.Count - overflow)).ToList();
                }
                else
                {
                    allPass = allPass.Take(Math.Max(0, allPass.Count - overflow)).ToList();
                }
            }

            to.PeqBands = tonal.Concat(allPass).ToList();
        }

        if (scope.Peq)
        {
            to.PeqPreampDb = from.PeqPreampDb;
            to.PeqSourceName = from.PeqSourceName;
        }
    }

    // ● has at least one source, ○ none.
    private void UpdateSideRadioTexts()
    {
        bool leftAny = channels.Any(channel =>
            channel.SideState(false).TransferImpulseResponse != null);
        bool rightAny = channels.Any(channel =>
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
            BackColor = Color.FromArgb(46, 51, 62),
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 0, 6),
            ChannelName = ChannelNameFor(index),
            // Before joining the list: the rows change its height and a re-pin makes the list jump.
            PhaseControlShown = project.ResolveDspPhaseControl(),
            FirControlShown = project.ResolveDspFirFilters(),
            ProcessorSampleRateHz = ProcessorSampleRateHz
        };

        OxyColor color = ChannelColors[index];
        control.SetAccentColor(Color.FromArgb(color.R, color.G, color.B));

        // Per block, not in the constructor: blocks added later need tooltips too.
        control.ApplyTooltips(toolTip);

        var channel = new VirtualCrossoverChannel(ChannelNameFor(index))
        {
            // Read on demand: the processor can change at any time.
            ProcessorSampleRateProvider = () => ProcessorSampleRateHz
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
        int at = channels.IndexOf(channel);
        int to = at + delta;
        if (at < 0 || to < 0 || to >= channels.Count)
        {
            return;
        }

        var order = Enumerable.Range(0, channels.Count).ToList();
        (order[at], order[to]) = (order[to], order[at]);
        ApplyChannelOrder(order);
        ScheduleSave();
        RedrawAll();
    }

    /// <summary><c>order[newIndex]</c> is the block's current position.</summary>
    /// <remarks>The project's pairs are permuted by the same indices, not rebuilt from the channels: they are bound
    /// only once a project is applied. The pair list is the whole persisted order.</remarks>
    private void ApplyChannelOrder(IReadOnlyList<int> order)
    {
        List<VirtualCrossoverChannel> reordered =
            order.Select(index => channels[index]).ToList();
        channels.Clear();
        channels.AddRange(reordered);
        if (project.Pairs.Count == order.Count)
        {
            List<VirtualCrossoverChannelPairSettings> pairs =
                order.Select(index => project.Pairs[index]).ToList();
            project.Pairs.Clear();
            project.Pairs.AddRange(pairs);
        }

        channelListPanel.SuspendLayout();
        for (int i = 0; i < channels.Count; i++)
        {
            VirtualCrossoverChannel channel = channels[i];
            VirtualCrossoverChannelControl control = ControlFor(channel);
            channel.Name = ChannelNameFor(i);
            control.ChannelName = channel.Name;
            OxyColor color = ChannelColors[i];
            control.SetAccentColor(Color.FromArgb(color.R, color.G, color.B));
            channelListPanel.Controls.SetChildIndex(control, i);
        }

        channelListPanel.ResumeLayout(performLayout: true);
        UpdateChannelButtons();
    }

    private VirtualCrossoverChannelControl ControlFor(VirtualCrossoverChannel channel) =>
        channelControls[channel];

    // One rate per project (disagreeing measurements are rejected), so the first resolved side answers.
    // Both physical sides are read: the shown side may be empty.
    private double ProjectSampleRateHz => MeasuredSampleRateHz ?? DefaultSampleRateHz;

    /// <summary>Null while the project has no measurement, so a read-out can say "nothing yet".</summary>
    private int? MeasuredSampleRateHz
    {
        get
        {
            foreach (VirtualCrossoverChannel channel in channels)
            {
                int leftRate = channel.PhysicalSideState(rightSide: false).SampleRate;
                if (leftRate > 0)
                {
                    return leftRate;
                }

                int rightRate = channel.PhysicalSideState(rightSide: true).SampleRate;
                if (rightRate > 0)
                {
                    return rightRate;
                }
            }

            return null;
        }
    }

    private const double DefaultSampleRateHz = 48_000;

    /// <summary>A named model answers from the catalog; Custom without a stored rate follows the measurements.</summary>
    private DspProcessorProfile ProcessorProfile =>
        project.ResolveDspProcessor((int)Math.Round(ProjectSampleRateHz));

    /// <summary>The rate simulated filters are designed at, NOT the measurement rate (see <see cref="PreparedDspResponse"/>).</summary>
    internal int ProcessorSampleRateHz => ProcessorProfile.SampleRateHz;

    /// <summary>Ceiling for automatic delay proposals; manual delay fields keep a wider range on purpose.</summary>
    internal double ProcessorMaxDelayMs => ProcessorProfile.MaxDelayMs;

    private bool ProcessorRateFollowsMeasurements =>
        project.DspProcessorRateFollowsMeasurements;

    // The processing rate keys the coordinator cache, so a change re-runs every channel.
    private void OpenDspProcessorDialog()
    {
        // The dialog gets the real measured rate, zero included, never a default.
        using var dialog = new DspProcessorDialog(
            ProcessorProfile,
            ProcessorRateFollowsMeasurements,
            MeasuredSampleRateHz ?? 0,
            project.DspProcessorPhaseControl,
            project.DspProcessorFirFilters)
        {
            Notes = project.AiNotes
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        // Notes alone are a save, never a re-run.
        string? notes = dialog.Notes;
        bool notesChanged = !string.Equals(notes, project.AiNotes, StringComparison.Ordinal);
        if (notesChanged)
        {
            project.AiNotes = notes;
            ScheduleSave();
        }

        DspProcessorProfile profile = dialog.Profile;
        // Compare intent, not numbers: "follow measurements" equals 48 kHz only until they are replaced.
        bool follows = dialog.FollowsMeasurements;
        // Confirming stores the shown phase answer, so a later model change cannot remove a control in use.
        bool phaseControl = dialog.PhaseControl;
        bool phaseControlChanged = project.DspProcessorPhaseControl != phaseControl;
        bool firFilters = dialog.FirFilters;
        bool firFiltersChanged = project.DspProcessorFirFilters != firFilters;
        if (profile == ProcessorProfile && follows == ProcessorRateFollowsMeasurements &&
            !phaseControlChanged && !firFiltersChanged)
        {
            return;
        }

        project.DspProcessorPhaseControl = phaseControl;
        project.DspProcessorFirFilters = firFilters;
        project.SetDspProcessor(profile, follows);
        // A device without phase control (or FIR) drops them: left in place they would bend curves with no field on screen.
        int clearedRotations = project.ClearUnavailablePhaseRotations();
        int clearedFirFilters = project.ClearUnavailableFirFilters();
        if (clearedRotations > 0 || clearedFirFilters > 0)
        {
            foreach (VirtualCrossoverChannel channel in channels)
            {
                ApplySettingsToControl(channel);
            }
        }

        RefreshProcessorRowAvailability();

        ScheduleSave();
        RedrawAll();
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

        while (channels.Count > count)
        {
            VirtualCrossoverChannel removed = channels[^1];
            // Invalidate BEFORE detaching: a pending source load then refuses to write back into the disposed control.
            removed.Invalidate();
            channels.RemoveAt(channels.Count - 1);
            VirtualCrossoverChannelControl control = ControlFor(removed);
            channelControls.Remove(removed);
            channelListPanel.Controls.Remove(control);
            control.Dispose();
        }

        while (channels.Count < count)
        {
            VirtualCrossoverChannel added = CreateChannel(channels.Count);
            channels.Add(added);
            channelListPanel.Controls.Add(ControlFor(added));
        }

        UpdateChannelButtons();
    }

    private void UpdateChannelButtons()
    {
        buttonAddChannel.Enabled = channels.Count < MaxChannelCount;
        buttonRemoveChannel.Enabled = channels.Count > MinChannelCount;
        for (int i = 0; i < channels.Count; i++)
        {
            ControlFor(channels[i]).SetMoveAvailability(i > 0, i < channels.Count - 1);
        }
    }

    private void AddChannel()
    {
        if (channels.Count >= MaxChannelCount)
        {
            return;
        }

        var pair = new VirtualCrossoverChannelPairSettings();
        project.Pairs.Add(pair);
        SetChannelCount(channels.Count + 1);
        VirtualCrossoverChannel added = channels[^1];
        added.Pair = pair;
        added.ActiveRight = project.ActiveSideRight;
        ApplySettingsToControl(added);

        ScheduleSave();
        RedrawAll();
    }

    private void RemoveChannel()
    {
        if (channels.Count <= MinChannelCount)
        {
            return;
        }

        SetChannelCount(channels.Count - 1);
        if (project.Pairs.Count > channels.Count)
        {
            project.Pairs.RemoveRange(
                channels.Count, project.Pairs.Count - channels.Count);
        }

        ScheduleSave();
        RedrawAll();
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
        (_, string? backupError) = project.SaveResetBackup();
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

        ScheduleSave();
        RedrawAll();
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
                ? project.ShowSumCurveOnPhase
                : radioViewGroupDelay.Checked
                    ? project.ShowSumCurveOnGroupDelay
                    : radioViewStep.Checked
                        ? project.ShowSumCurveOnStep
                        : project.ShowSumCurve;
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
            project.ShowSumCurveOnPhase = checkBoxShowSum.Checked;
        }
        else if (radioViewGroupDelay.Checked)
        {
            project.ShowSumCurveOnGroupDelay = checkBoxShowSum.Checked;
        }
        else if (radioViewStep.Checked)
        {
            project.ShowSumCurveOnStep = checkBoxShowSum.Checked;
        }
        else if (radioViewMagnitude.Checked)
        {
            project.ShowSumCurve = checkBoxShowSum.Checked;
        }

        project.SumLossWindowMode = SelectedSumLossWindow;
        project.ShowHybridCurves = checkBoxHybrid.Checked;
        project.ShowTargetCurve = checkBoxShowTarget.Checked;
        project.TargetLevelDb = (double)numericTargetLevel.Value;
        // Newer view flags are written beside older ones so an older build opens the nearest view.
        project.ShowPhaseView = radioViewPhase.Checked || radioViewGroupDelay.Checked;
        project.ShowImpulseView = radioViewImpulse.Checked || radioViewStep.Checked;
        project.ShowGroupDelayView = radioViewGroupDelay.Checked;
        project.ShowStepView = radioViewStep.Checked;
        project.SetSmoothingCode(comboBoxSmoothing.SelectedItem is int value
            ? value
            : 12);
        project.GroupView = SelectedGroupView;
        ScheduleSave();
        RedrawAll();
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
        bool phaseShown = project.ResolveDspPhaseControl();
        bool firShown = project.ResolveDspFirFilters();
        int rate = ProcessorSampleRateHz;
        // Before the early return, so newly added or loaded pairs get the FIR rate too (EffectiveCrossover reads it).
        foreach (VirtualCrossoverChannel channel in channels)
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
        return (entryId, LocateSource(settings));
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
            Title = $"Choose channel {SideLabel(channel, rightSide)} impulse response"
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
            ResolvedSidesExcept(targetState).ToList();
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
        SettleSpatialAverageMode();
        UpdateSourceButton(channel);
        UpdateSideRadioTexts();
        ScheduleSave();
        RedrawAll();
    }

    // Mono pairs expose only their left slot.
    private IEnumerable<(VirtualCrossoverChannel Channel, bool RightSide, VirtualCrossoverChannelState State)>
        ResolvedSidesExcept(VirtualCrossoverChannelState? except)
    {
        foreach (VirtualCrossoverChannel channel in channels)
        {
            foreach (bool rightSide in new[] { false, true })
            {
                if (channel.Pair.Mono && rightSide)
                {
                    continue;
                }

                VirtualCrossoverChannelState state = channel.SideState(rightSide);
                if (state != except && state.TransferImpulseResponse != null)
                {
                    yield return (channel, rightSide, state);
                }
            }
        }
    }

    private static string SideLabel(VirtualCrossoverChannel channel, bool rightSide) =>
        channel.Pair.Mono
            ? $"{channel.Name} (mono)"
            : $"{channel.Name} {(rightSide ? "R" : "L")}";

    private void ClearSource(VirtualCrossoverChannel channel)
    {
        ClearSourceCore(channel, channel.ActiveRight);
        ScheduleSave();
        RedrawAll();
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

        if (LocateSource(settings) is { } path)
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

    private string? LocateSource(VirtualCrossoverChannelSettings settings) =>
        VirtualCrossoverSourceLocator.Locate(
            settings.SourceFilePath,
            settings.SourceRelativePath,
            project.ProjectDirectory)
        ?? VirtualCrossoverSourceLocator.Locate(
            settings.SourceFilePath,
            settings.SourceRelativePath,
            relinkDirectory);

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
        MagnitudeGateSnapshot snapshot = magnitudeGate;
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
                ProcessorProfile,
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                renderAnchor,
                CapturePhaseContext(channel),
                targetLevelDb ?? (double)numericTargetLevel.Value,
                (double)numericTargetLevel.Minimum,
                (double)numericTargetLevel.Maximum,
                snapshot.SmoothingInverseOctaves,
                // The wizard pins what the panel rendered with, including per-channel Own calibration.
                CalibrationFor(channel.SideState(channel.ActiveRight)),
                CalibrationNameFor(channel.SideState(channel.ActiveRight)),
                SpatialAverageCalibrationFor(channel.SideState(channel.ActiveRight)),
                projectGeneration,
                spatialAverage.Capture,
                spatialAverage.OffsetDb,
                HybridRequested &&
                    SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray &&
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
        double referenceOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(drawn, sampleRate);
        double detrendMs = ResolveCommonDetrendMs(drawn, referenceOffsetMs, sampleRate);
        List<double> offsets = ResolvePhaseGateOffsets(drawn, referenceOffsetMs, sampleRate);

        return new EqWizardPhaseContext(
            // Curves render as Manual against one τ for the whole set, but the user's detrend mode must arrive intact.
            CreateVirtualPhaseSettings(
                referenceOffsetMs,
                gatePreview?.DetrendMode ?? project.PhaseDetrendMode,
                detrendMs),
            offsets[index],
            detrendMs,
            PinnedGateOffsetMs is not null,
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
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        if (!VirtualDspEqHandoff.TryApplyReturn(
                channels,
                token,
                curve,
                projectGeneration,
                // Per side: under Own the panel holds no single calibration, and null would refuse every return.
                CalibrationFor(token.Channel.SideState(token.RightSide)),
                SpatialAverageCalibrationFor(token.Channel.SideState(token.RightSide)),
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                (double)numericTargetLevel.Value,
                // Same decision the handoff recorded, so an in-flight redraw cannot turn a valid return into a refusal.
                HybridHandoffCapture(token.Channel, token.RightSide),
                ProcessorSampleRateHz))
        {
            return false;
        }

        // The guard above proved the level is still the wizard's starting point, so writing it overwrites nothing.
        if (!((double)numericTargetLevel.Value).Equals(targetLevelDb))
        {
            numericTargetLevel.Value = numericTargetLevel.ClampValue(targetLevelDb);
        }

        UpdatePeqReadouts(token.Channel);
        ScheduleSave();
        RedrawAll();
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
                ProcessorSampleRateHz,
                $"Channel {channel.Name} ({side})",
                minHz,
                maxHz,
                // Bands were not necessarily fitted here; no invented statistics.
                Stats: null,
                ProcessorProfile.QConvention));
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
        ScheduleSave();
        RedrawAll();
    }

    private void ClearPeq(VirtualCrossoverChannel channel)
    {
        channel.Settings.PeqBands = new List<PeqBand>();
        channel.Settings.PeqPreampDb = 0;
        channel.Settings.PeqSourceName = null;
        UpdatePeqReadouts(channel);
        ScheduleSave();
        RedrawAll();
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
        ScheduleSave();
        RedrawAll();
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
                ProcessorSampleRateHz,
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
        ScheduleSave();
        RedrawAll();
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
            channel, channel.ActiveRight, projectGeneration, ProcessorSampleRateHz));
    }

    /// <summary>False, writing nothing, when the side is no longer the one the session opened on.</summary>
    internal bool TryApplyFirFromConstructor(
        FirConstructorReturnToken token,
        FirFilter kernel,
        FirCrossoverDesign design)
    {
        if (!FirConstructorHandoff.TryApplyReturn(
                channels,
                token,
                kernel,
                design,
                projectGeneration,
                ProcessorSampleRateHz,
                project.ResolveDspFirFilters()))
        {
            return false;
        }

        UpdateFirReadout(token.Channel);
        ScheduleSave();
        RedrawAll();
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

        project.SetDspPlotMode(CurrentDspPlotMode());
        ScheduleSave();
        RedrawDspPlot();
    }

    private void OnCorrelationPairChanged()
    {
        if (suppressProjectEvents || suppressCorrelationPairEvents)
        {
            return;
        }

        project.CorrelationPairIndex =
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
            $"Start over: {DefaultChannelCount} empty default blocks, and the panel's own\r\n" +
            "settings with them; calibration and the EQ target stay.\r\n" +
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
            "The level the target hangs at. These curves are transfer-function\r\n" +
            "dB with no absolute reference, so the target has no level of its\r\n" +
            "own here: set it where you read the sum. Stored with the session,\r\n" +
            "not with the target, so retuning the shape leaves it where it is.");
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
        magnitudeGate = new MagnitudeGateSnapshot(
            CreateVirtualPhaseSettings(
                gateOffsetMs: 0.0,
                PhaseDetrendMode.Off,
                manualDetrendMilliseconds: 0.0) with
            {
                // The magnitude uses the FIXED steady-state window; only the offset comes from the gate.
                // See docs/tech/virtual-dsp-panel.md#magnitude-window.
                WindowMode = PhaseWindowMode.Fixed,
                LeftMs = FrequencyResponseOptions.SteadyStateLeftMs,
                PlateauMs = FrequencyResponseOptions.SteadyStatePlateauMs,
                RightMs = FrequencyResponseOptions.SteadyStateRightMs
            },
            PinnedGateOffsetMs,
            project.PhaseGateFor(!project.ActiveSideRight).OffsetMs,
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
            for (int i = 0; i < channels.Count; i++)
            {
                VirtualCrossoverChannel channel = channels[i];
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
                    ProcessorSampleRateHz,
                    chain));
                bindings.Add(
                    i,
                    (channel,
                        ChannelColors[i],
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

        // Filter by group view once, so curves, sum, loss and read-out describe the same channels.
        VirtualCrossoverGroupView groupView = SelectedGroupView;
        List<ProcessedChannel> shown = ChannelsShownBy(processed, groupView);
        List<ProcessedChannel> summedChannels = ChannelsSummedBy(shown, groupView);
        if (shown.Count == 0)
        {
            // With nothing resolved at all the zone hint would mislead, so show the no-sources hint.
            acousticPlot.Draw(new AcousticRender(
                processed.Count == 0 ? NoSourcesHint : EmptyViewHint(groupView),
                [],
                null));
            MetricChanged?.Invoke(string.Empty, string.Empty);
            // A warning about channels no longer visible would read as a fault in this view.
            HideWarning();
            return;
        }

        // No loss (curve or figure) across groups or where the chain has no junction.
        // See docs/tech/virtual-dsp-panel.md#sum-loss-and-group-views.
        bool quotesJunctions =
            VirtualCrossoverGroupViews.LossChainZone(groupView) != null &&
            ProcessedChannels.HasJunction(summedChannels);

        // Off the UI thread: the FDW gate is 50–100 ms per new response set. Reads the SUMMING channels.
        // See docs/tech/virtual-dsp-panel.md#junction-phase-read-out.
        List<VirtualCrossoverMetric.PhaseEntry> phaseEntries = [];
        // Direct loss (FDW-8) sums the same block's spectra, so built in the same task from the same windows.
        List<SignalPoint>? directLoss = null;
        SumLossWindow lossWindow = SelectedSumLossWindow;
        int lossSmoothing = magnitudeGate.SmoothingInverseOctaves;
        if (quotesJunctions)
        {
            int phaseRate = summedChannels[0].SampleRate;
            double? pinnedOffsetMs = PinnedGateOffsetMs;
            double gateLeftMs = gatePreview?.LeftMs ?? project.PhaseGateLeftMs;
            double gatePlateauMs = gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs;
            double gateRightMs = gatePreview?.RightMs ?? project.PhaseGateRightMs;
            (phaseEntries, directLoss) = await Task.Run(() =>
            {
                // Zone inside the task: Tracy zones are per-thread LIFO.
                using var _ = AppProfiler.Zone("VirtualDSP.BuildPhaseEntries");
                IReadOnlyList<ProcessedChannel>? orderedSet = null;
                IReadOnlyList<Complex[]>? spectra = null;
                List<VirtualCrossoverMetric.PhaseEntry> entries = metrics.BuildPhaseEntries(
                    summedChannels,
                    ordered =>
                    {
                        orderedSet = ordered;
                        spectra = JunctionPhaseSpectra.Build(
                            ordered, phaseRate, pinnedOffsetMs,
                            gateLeftMs, gatePlateauMs, gateRightMs);
                        return spectra;
                    });
                List<SignalPoint>? direct =
                    lossWindow == SumLossWindow.Direct && spectra != null
                        ? metrics.BuildDirectLossCurve(orderedSet!, spectra, lossSmoothing)
                        : null;
                return (entries, direct);
            });
        }

        // Narrowed by the Show filter, never a shortened list: a block's list position is its cache identity.
        List<VirtualCrossoverMetric.StereoDelta> stereoDeltas =
            await metrics.ComputeStereoDeltasAsync(
                channels,
                revision,
                includePair: pair =>
                    VirtualCrossoverGroupViews.IsShown(groupView, pair.Zone),
                hybridLevelDeltaDb: HybridStereoLevelReader());
        // Quoted by cross-group views instead of a loss; adds only arrival FFTs.
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> groupDeltas =
            await metrics.ComputeGroupDeltasAsync(
                shown, groupView, revision,
                hybridGroupLevelDeltaDb: HybridGroupLevelReader());
        // The curve windows through the OPPOSITE side's gate placement; both sides must be drawn by the same method.
        VirtualCrossoverSideSum? oppositeSide = null;
        if (checkBoxShowSum.Checked &&
            (radioViewMagnitude.Checked || radioViewStep.Checked))
        {
            oppositeSide = await metrics.ComputeSideSumAsync(
                channels, !project.ActiveSideRight, revision, minimumChannels: 2,
                includePair: pair =>
                    VirtualCrossoverGroupViews.ParticipatesInTotalSum(
                        groupView, pair.Zone));
        }

        // Envelopes (Hilbert over the whole record, 2^17+ samples) are warmed off the UI thread: a drag hands
        // each frame a new array. Memoized per array.
        if (radioViewImpulse.Checked)
        {
            Complex[][] drawnResponses =
                [.. shown
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
                shown, magnitudeGate.SmoothingInverseOctaves, summedChannels);
        }

        // Decided before the awaits, where the junction phase block uses it.
        if (!quotesJunctions)
        {
            lossCurve = null;
        }

        // Disable draws no curve but the column keeps the full read.
        bool lossDirect = lossWindow == SumLossWindow.Direct;
        List<SignalPoint>? shownLoss = lossDirect ? directLoss : lossCurve;
        List<SignalPoint>? drawnLoss = lossWindow == SumLossWindow.Off ? null : shownLoss;

        // Before warnings and render: both read it.
        HybridMagnitudes? hybrid = null;
        if (HybridRequested && magnitudes != null && radioViewMagnitude.Checked)
        {
            using (AppProfiler.Zone("VirtualDSP.BuildHybrid"))
            {
                hybrid = BuildHybridMagnitudes(
                    shown,
                    magnitudes,
                    project.ActiveSideRight,
                    magnitudeGate.SmoothingInverseOctaves);
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
                    : BuildOppositeHybridSumCurve(oppositeSide, hybrid.OffsetDb);
            }
        }

        using (AppProfiler.Zone("VirtualDSP.UpdateMetric"))
        {
            // Junction read-outs use the SUMMED channels: a drawn-only centre would invent a crossover.
            UpdateMetric(
                summedChannels, shownLoss, phaseEntries, stereoDeltas, hybrid,
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
            acousticRender = BuildAcousticRender(
                shown, summedChannels, groupView, magnitudes, sumCurve, drawnLoss,
                oppositeSum, oppositeSide, hybrid, lossDirect);
        }

        using (AppProfiler.Zone("VirtualDSP.AcousticPlotDraw"))
        {
            acousticPlot.Draw(acousticRender);
        }
    }

    private static List<ProcessedChannel> ChannelsShownBy(
        IReadOnlyList<ProcessedChannel> processed,
        VirtualCrossoverGroupView view) =>
        [.. processed.Where(item =>
            VirtualCrossoverGroupViews.IsShown(view, item.Channel.Pair.Zone))];

    // Drawn and summed differ where a centre is shown: compared, not added.
    private static List<ProcessedChannel> ChannelsSummedBy(
        IReadOnlyList<ProcessedChannel> shown,
        VirtualCrossoverGroupView view) =>
        [.. shown.Where(item =>
            VirtualCrossoverGroupViews.ParticipatesInTotalSum(view, item.Channel.Pair.Zone))];

    private static string EmptyViewHint(VirtualCrossoverGroupView view) =>
        $"No channels in {VirtualCrossoverGroupViews.DisplayName(view)}." +
        Environment.NewLine +
        "Set a block's Zone to bring it into this view.";

    // While a session loads, processed is empty; keep the loading note instead of the no-sources hint.
    private AcousticRender BuildAcousticRender(
        List<ProcessedChannel> processed,
        IReadOnlyList<ProcessedChannel> summed,
        VirtualCrossoverGroupView view,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve,
        List<SignalPoint>? lossCurve,
        AnalysisCurve? oppositeSum,
        VirtualCrossoverSideSum? oppositeSide,
        HybridMagnitudes? hybrid,
        bool lossDirect = false)
    {
        string hint = loadingProject
            ? LoadingHint
            : processed.Count == 0 ? NoSourcesHint : string.Empty;
        if (processed.Count == 0)
        {
            return new AcousticRender(hint, [], null);
        }
        if (radioViewPhase.Checked)
        {
            // Drawn set for traces, summed subset for the Sum, matching the magnitude view.
            return new AcousticRender(hint, BuildPhaseCurves(processed, summed), null);
        }
        if (radioViewGroupDelay.Checked)
        {
            return new AcousticRender(hint, BuildGroupDelayCurves(processed, summed), null);
        }
        if (radioViewImpulse.Checked)
        {
            return new AcousticRender(hint, [], BuildImpulseRender(processed));
        }
        if (radioViewStep.Checked)
        {
            return new AcousticRender(
                hint, [], BuildStepRender(processed, summed, oppositeSide));
        }

        if (VirtualCrossoverGroupViews.DrawsGroupSums(view))
        {
            return new AcousticRender(
                hint, BuildGroupSumCurves(processed, magnitudes, hybrid), null);
        }

        return new AcousticRender(
            hint,
            BuildMagnitudeCurves(
                processed, magnitudes, sumCurve, lossCurve, oppositeSum, hybrid,
                lossDirect),
            null);
    }

    // One summed line per zone, all gated on ONE anchor across the shown channels.
    // See docs/tech/virtual-dsp-panel.md#groups-view.
    private List<AcousticCurve> BuildGroupSumCurves(
        List<ProcessedChannel> shown,
        IReadOnlyList<AnalysisCurve>? magnitudes,
        HybridMagnitudes? hybrid)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildGroupSumCurves");
        int anchor = ProcessedChannels.SharedStartAnchorIndex(shown);
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        double gateOffsetMs = snapshot.ResolveGateOffsetMs(
            oppositeSide: false, anchor, shown[0].SampleRate);
        // Check every list the slice indexes: the slice runs before BuildHybridSumCurve's own guard.
        bool drawHybrid = hybrid != null && magnitudes != null &&
            magnitudes.Count >= shown.Count &&
            hybrid.Channels.Count >= shown.Count &&
            hybrid.UnsmoothedChannels.Count >= shown.Count &&
            hybrid.ChannelOffsetsDb.Count >= shown.Count;
        var curves = new List<AcousticCurve>();
        foreach (VirtualCrossoverZone zone in VirtualCrossoverZones.All)
        {
            // By position: hybrid curves and magnitudes are indexed against the shown set.
            List<int> positions =
            [
                .. Enumerable.Range(0, shown.Count)
                    .Where(index => shown[index].Channel.Pair.Zone == zone)
            ];
            if (positions.Count == 0)
            {
                continue;
            }

            List<ProcessedChannel> members = [.. positions.Select(index => shown[index])];
            List<SignalPoint>? points = drawHybrid
                ? BuildHybridSumCurve(
                    HybridSubset(hybrid!, positions),
                    members,
                    anchor,
                    snapshot,
                    gateOffsetMs,
                    [
                        .. positions.Select(index =>
                            (IReadOnlyList<SignalPoint>)magnitudes![index].Points)
                    ])
                : null;
            curves.Add(new AcousticCurve(
                VirtualCrossoverZones.DisplayName(zone),
                points ?? BuildMeasuredSumCurve(members, anchor).Display.Points,
                GroupColor(zone),
                2.0,
                LineStyle.Solid));
        }

        if (BuildTargetCurve() is { } target)
        {
            curves.Insert(0, target);
        }

        return curves;
    }

    /// <summary>One group's slice of the set's hybrid.</summary>
    /// <remarks>Per-channel lists are narrowed positionally; the set offset and datums are not, keeping all lines on one axis.</remarks>
    internal static HybridMagnitudes HybridSubset(
        HybridMagnitudes hybrid,
        IReadOnlyList<int> positions) =>
        hybrid with
        {
            Channels = [.. positions.Select(index => hybrid.Channels[index])],
            UnsmoothedChannels =
                [.. positions.Select(index => hybrid.UnsmoothedChannels[index])],
            ChannelOffsetsDb =
                [.. positions.Select(index => hybrid.ChannelOffsetsDb[index])],
            PointMeasuredChannels = hybrid.PointMeasuredChannels.Count == 0
                ? []
                : [.. positions.Select(index => hybrid.PointMeasuredChannels[index])]
        };

    // Semantic: in this view a line IS a zone.
    private static OxyColor GroupColor(VirtualCrossoverZone zone) => zone switch
    {
        VirtualCrossoverZone.Rear => OxyColor.FromRgb(255, 150, 64),
        VirtualCrossoverZone.Center => OxyColor.FromRgb(96, 210, 120),
        VirtualCrossoverZone.Sub => OxyColor.FromRgb(200, 130, 255),
        _ => OxyColor.FromRgb(86, 156, 255)
    };

    // Handed to the host (the EQ Wizard owns the one target). A session without a stored target starts carrying the current one.
    private void ApplyProjectTarget()
    {
        if (project.Target is { } stored)
        {
            TargetCurveChanged?.Invoke(stored.ToCurve());
            return;
        }

        if (targetCurve is { } current)
        {
            project.Target = VirtualCrossoverTargetSettings.FromCurve(current);
        }
    }

    // A target is parametric, so it spans the audio band on its own grid.
    private const double TargetGridLowHz = 20;
    private const double TargetGridHighHz = 20_000;
    private const int TargetGridPoints = 512;

    // Hung at the user's level, not fitted: transfer-function dB has no absolute reference to fit.
    private AcousticCurve? BuildTargetCurve()
    {
        if (!checkBoxShowTarget.Checked || targetCurve is not { } target)
        {
            return null;
        }

        double level = (double)numericTargetLevel.Value;
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(
            TargetGridLowHz, TargetGridHighHz, TargetGridPoints);
        var points = new SignalPoint[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            points[i] = new SignalPoint(
                grid[i], level + target.Spec.Evaluate(grid[i]));
        }

        return new AcousticCurve(
            "Target",
            points,
            OxyColor.FromArgb(
                target.Color.A, target.Color.R, target.Color.G, target.Color.B),
            target.StrokeThickness,
            OverlayLineStyles.ToOxy(target.LineStyle));
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

        project.Target = VirtualCrossoverTargetSettings.FromCurve(value);
        ScheduleSave();
    }

    private List<AcousticCurve> BuildMagnitudeCurves(
        List<ProcessedChannel> processed,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve,
        List<SignalPoint>? lossCurve,
        AnalysisCurve? oppositeSumCurve,
        HybridMagnitudes? hybrid,
        bool lossDirect = false)
    {
        // A shown RAW curve is built here per channel; processed ones arrive prebuilt.
        using var _ = AppProfiler.Zone("VirtualDSP.BuildMagnitudeCurves");
        var curves = new List<AcousticCurve>();
        // First, so the curves draw on top of it.
        if (BuildTargetCurve() is { } target)
        {
            curves.Add(target);
        }

        for (int i = 0; i < processed.Count; i++)
        {
            ProcessedChannel item = processed[i];
            if (item.Channel.Pair.ShowRawCurve)
            {
                AnalysisCurve raw = BuildRawMagnitudeCurve(
                    item.Channel.TransferImpulseResponse!,
                    item.Channel.TransferPeakIndex,
                    item.Channel.SampleRate,
                    item.MeasuredBand,
                    CalibrationFor(item));
                curves.Add(new AcousticCurve(
                    $"{item.Channel.Name} raw",
                    raw.Points,
                    OxyColor.FromAColor(90, item.Color),
                    1.2,
                    LineStyle.Solid));
            }

            if (item.Channel.Pair.ShowProcessedCurve)
            {
                // Non-null here: magnitudes are withheld only for an empty set.
                AnalysisCurve curve = magnitudes![i];
                IReadOnlyList<SignalPoint> points = hybrid != null
                    ? ShiftedBy(hybrid.Channels[i], hybrid.OffsetDb)
                    : curve.Points;
                curves.Add(new AcousticCurve(
                    item.Channel.Name, points, item.Color, 1.8, LineStyle.Solid));
            }
        }

        if (magnitudes == null || sumCurve == null)
        {
            return curves;
        }

        if (checkBoxShowSum.Checked)
        {
            // Hybrid: averages hold no phase, so cancellation comes from the IR loss curve (BuildHybridSumCurve).
            IReadOnlyList<SignalPoint> sumPoints =
                (hybrid != null ? BuildActiveHybridSumCurve(processed, magnitudes, hybrid) : null)
                ?? sumCurve.Points;
            curves.Add(new AcousticCurve(
                "Sum", sumPoints, SumColor, 2.4, LineStyle.Solid));
            if (oppositeSumCurve != null)
            {
                curves.Add(new AcousticCurve(
                    $"Sum {(project.ActiveSideRight ? "L" : "R")}",
                    oppositeSumCurve.Points,
                    OxyColor.FromAColor(110, SumColor),
                    1.8,
                    LineStyle.Dash));
            }
        }

        if (lossCurve != null)
        {
            // Complex sum vs phase-blind magnitude sum (<= 0), from UNSMOOTHED magnitudes, smoothed as a ratio; the same list
            // the read-out averages. On the loss axis. Null under Disable; under FDW-8 the direct-sound loss.
            curves.Add(new AcousticCurve(
                lossDirect ? "Sum loss (direct)" : "Sum loss",
                lossCurve, LossColor, 1.8, LineStyle.Dash, OnLossAxis: true));
        }

        return curves;
    }

    private void UpdateMetric(
        List<ProcessedChannel> processed,
        List<SignalPoint>? lossCurve,
        IReadOnlyList<VirtualCrossoverMetric.PhaseEntry> phaseEntries,
        IReadOnlyList<VirtualCrossoverMetric.StereoDelta>? stereoDeltas = null,
        HybridMagnitudes? hybrid = null,
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta>? crossGroup = null,
        bool lossDirect = false)
    {
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> groupDeltas = crossGroup ?? [];
        // Zoned apart from formatting: the per-junction banded analysis is the real work.
        List<VirtualCrossoverMetric.Entry> entries;
        using (AppProfiler.Zone("VirtualDSP.BuildEntries"))
        {
            entries = metrics.BuildEntries(processed, lossCurve);
        }

        // Built off the UI thread by the caller, which also decides whether junctions are quoted.
        string compact = VirtualCrossoverMetric.FormatCompact(entries, lossDirect);
        string detail = entries.Count > 0
            ? VirtualCrossoverMetric.FormatDetail(entries, lossDirect)
            : string.Empty;
        if (phaseEntries.Count > 0)
        {
            compact += "\r\n\r\n" +
                VirtualCrossoverMetric.FormatPhaseCompact(phaseEntries);
            detail += (detail.Length > 0 ? "\r\n\r\n" : string.Empty) +
                VirtualCrossoverMetric.FormatPhaseDetail(phaseEntries);
        }
        // Under the loss column: in a cross-group view it stands in for the withheld loss.
        if (groupDeltas.Count > 0)
        {
            compact += (compact.Length > 0 ? "\r\n\r\n" : string.Empty) +
                VirtualCrossoverMetric.FormatGroupDeltasCompact(groupDeltas);
            detail += (detail.Length > 0 ? "\r\n\r\n" : string.Empty) +
                VirtualCrossoverMetric.FormatGroupDeltasDetail(groupDeltas);
        }
        if (stereoDeltas is { Count: > 0 })
        {
            compact += "\r\n\r\n" +
                VirtualCrossoverMetric.FormatStereoDeltasCompact(stereoDeltas);
            detail += (detail.Length > 0 ? "\r\n\r\n" : string.Empty) +
                VirtualCrossoverMetric.FormatStereoDeltasDetail(stereoDeltas);
        }
        if (hybrid != null)
        {
            // A health reading: an array shares the IRs' loopback, so a large offset means a different input, calibration or driver.
            compact += "\r\n\r\n" +
                $"Spatial average {hybrid.OffsetDb:+0.0;-0.0} dB";
            detail += (detail.Length > 0 ? "\r\n\r\n" : string.Empty) +
                $"The spatial averages sit {hybrid.OffsetDb:+0.0;-0.0} dB from the " +
                "impulse responses, and the whole set is drawn shifted by that one " +
                "figure.";
        }

        MetricChanged?.Invoke(compact, detail);
    }

    // Only one warning line; the gate placement comes first (a late window turns every driver into its tail).
    private void UpdateWarnings(
        List<ProcessedChannel> processed, HybridMagnitudes? hybrid)
    {
        gatePlacement = JudgeGatePlacement(processed);
        if (gatePlacement is { CutsChannels: true } verdict)
        {
            ShowWarning(
                FormatGateCutWarning(verdict),
                FormatGateCutDetail(verdict),
                GateWarningColor);
            return;
        }

        // Only while the hybrid is drawn.
        if (hybrid != null && hybrid.SpreadDb > HybridSpreadWarningDb)
        {
            ShowWarning(
                $"⚠ The spatial averages disagree by {hybrid.SpreadDb:0.0} dB — " +
                    "check the captures.",
                FormatHybridSpreadDetail(hybrid, processed),
                GateWarningColor);
            return;
        }

        // Warn, not refuse: the loopback holds levels, but "the average" means something different per channel.
        if (hybrid != null && DescribeArrayCompositionMismatch() is { } mismatch)
        {
            ShowWarning(
                "⚠ The channels were not averaged over the same array.",
                mismatch,
                GateWarningColor);
            return;
        }

        if (DescribeUnappliedCalibration(processed) is { } unapplied)
        {
            ShowWarning(
                "⚠ The selected calibration does not reach every curve.",
                unapplied,
                GateWarningColor);
            return;
        }

        if (DescribeOwnCalibrationMismatch(processed) is { } corrections)
        {
            ShowWarning(
                "⚠ The channels were not measured through one calibration.",
                corrections,
                GateWarningColor);
            return;
        }

        // Info: the hybrid exists to keep point-measurement dips away from an EQ.
        if (hybrid is { PointMeasuredCount: > 0 } fallbacks)
        {
            ShowWarning(
                fallbacks.PointMeasuredCount == 1
                    ? "1 channel is drawn from its point measurement."
                    : $"{fallbacks.PointMeasuredCount} channels are drawn from their " +
                        "point measurements.",
                FormatPointMeasuredDetail(fallbacks, processed),
                InfoWarningColor);
            return;
        }

        UpdateCrossoverWarning(processed);
    }

    /// <summary>Allowed disagreement (dB) of a spatial-average set's per-channel offsets before flagging; per mode.</summary>
    /// <remarks>See docs/tech/virtual-dsp-panel.md#hybrid-spread-thresholds.</remarks>
    private double HybridSpreadWarningDb =>
        SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray
            ? ArraySpreadWarningDb
            : MovingMicSpreadWarningDb;

    private const double MovingMicSpreadWarningDb = 3.0;

    /// <summary>Arrays share the IRs' loopback: a real set read 0.33 dB apart, so the margin is tighter.</summary>
    private const double ArraySpreadWarningDb = 1.5;

    private static string FormatPointMeasuredDetail(
        HybridMagnitudes hybrid,
        IReadOnlyList<ProcessedChannel> processed)
    {
        var names = new List<string>();
        for (int i = 0; i < processed.Count && i < hybrid.PointMeasuredChannels.Count; i++)
        {
            if (hybrid.PointMeasuredChannels[i])
            {
                names.Add(processed[i].Channel.Name);
            }
        }

        return $"Drawn from one microphone: {string.Join(", ", names)}." +
            "\r\n\r\nThe rest of the set is drawn from its microphone arrays. A " +
            "channel measured at one point carries dips that belong to that spot " +
            "rather than to the listening volume, and an equalizer fitted to them " +
            "is fitted to a place nobody's head occupies. Below the cabin's first " +
            "mode the two measurements agree, so a subwoofer loses little by it.";
    }

    /// <summary>Null when a named calibration reaches every capture on the plot, or none was named.</summary>
    /// <remarks>A capture with mixed per-position calibrations keeps its own aggregate correction; the user chose a microphone that part of the plot is not reading through.</remarks>
    private string? DescribeUnappliedCalibration(IReadOnlyList<ProcessedChannel> processed)
    {
        if (ownCalibrationSelected || Calibration == null)
        {
            return null;
        }

        var aggregates = new List<string>();
        foreach (ProcessedChannel item in processed)
        {
            LiveCaptureDocument? capture = item.Channel
                .SideState(project.ActiveSideRight)
                .SpatialAverageFor(SpatialAverageMode);
            if (capture is { CalibrationIsAggregate: true })
            {
                aggregates.Add($"{item.Channel.Name} {item.Channel.Settings.DisplayName}");
            }
        }

        if (aggregates.Count == 0)
        {
            return null;
        }

        return
            $"{string.Join(", ", aggregates)} " +
            (aggregates.Count == 1 ? "was" : "were") +
            " averaged over positions carrying DIFFERENT calibration files, so the " +
            "correction stored with the average belongs to no single microphone and " +
            "there is nothing " +
            $"\"{SelectedCalibrationName() ?? "the selected calibration"}\" could be " +
            "swapped for. Those curves keep their own corrections — each position " +
            "through the file it was measured with, which is the closest thing to the " +
            "truth there is. Everything else on the plot is read through your " +
            "selection.\r\n\r\nSelect \"Own (as measured)\" to read the whole plot " +
            "the way each measurement was taken, and the note goes away.";
    }

    /// <summary>Under Own, null when all channels share a microphone. Each channel carries its correction into the sum
    /// (<see cref="BuildMeasuredSumCurve"/>), so this is information, not a defect.</summary>
    private string? DescribeOwnCalibrationMismatch(IReadOnlyList<ProcessedChannel> processed)
    {
        if (!ownCalibrationSelected || processed.Count < 2)
        {
            return null;
        }

        CalibrationFile? first = processed[0].MicrophoneCalibration;
        var differing = new List<string>();
        foreach (ProcessedChannel channel in processed)
        {
            if (!CalibrationFile.SameCurve(channel.MicrophoneCalibration, first))
            {
                differing.Add(channel.Channel.Name);
            }
        }

        if (differing.Count == 0)
        {
            return null;
        }

        return
            $"{processed[0].Channel.Name} was measured through " +
            $"{Describe(processed[0].MicrophoneCalibration)}, and " +
            $"{string.Join(", ", differing)} through something else. Each channel is " +
            "drawn through its own correction, which is what \"Own (as measured)\" " +
            "means, and the sum carries each channel's correction with it — so the " +
            "sum and the summation loss are honest. What they are not is one " +
            "instrument: the curves are being compared across microphones, and a " +
            "difference between two channels holds the difference between their " +
            "capsules as well. Pick one calibration above to read the whole plot " +
            "through a single microphone, at the cost of reading every channel " +
            "through one that did not measure it.";

        static string Describe(CalibrationFile? calibration) =>
            calibration is { HasData: true } ? "a calibration" : "no calibration";
    }

    private string? DescribeArrayCompositionMismatch()
    {
        if (SpatialAverageMode != VirtualCrossoverSpatialAverageMode.MicArray)
        {
            return null;
        }

        // Every array in the project, both sides and muted included: composition is a property of the measurements.
        // Cross-side (7 vs 5 positions) is the case nothing else catches.
        var arrays = new List<(string Name, LiveCaptureDocument Document, bool Drawn)>();
        foreach (VirtualCrossoverChannel channel in channels)
        {
            AddSide(rightSide: false);
            if (!channel.Pair.Mono)
            {
                // A mono pair would be compared with itself.
                AddSide(rightSide: true);
            }

            void AddSide(bool rightSide)
            {
                if (channel.SideState(rightSide).ArrayCapture is not { } document)
                {
                    return;
                }

                arrays.Add((
                    channel.Pair.Mono
                        ? channel.Name
                        : $"{channel.Name} {(rightSide ? "R" : "L")}",
                    document,
                    channel.Pair.Enabled));
            }
        }

        if (arrays.Count < 2)
        {
            return null;
        }

        bool countsDiffer = arrays.Any(entry =>
            entry.Document.Recipe.MicrophoneCount !=
            arrays[0].Document.Recipe.MicrophoneCount);
        bool calibrationsDiffer = arrays.Any(entry =>
            !SameArrayCorrection(entry.Document, arrays[0].Document));
        if (!countsDiffer && !calibrationsDiffer)
        {
            return null;
        }

        var lines = new StringBuilder();
        lines.Append(
            "A spatial average describes the volume its microphones stood in, so " +
            "captures averaged over different arrays are answering slightly " +
            "different questions:\r\n\r\n");
        foreach ((string name, LiveCaptureDocument document, bool drawn) in arrays)
        {
            string calibration = document.Calibration?.Name
                ?? (document.CalibrationIsAggregate
                    ? "several calibrations, one per position"
                    : "no calibration");
            lines.Append(
                $"    {name}    {document.Recipe.MicrophoneCount} microphone(s), " +
                $"{calibration}, measured {document.SavedAtUtc.ToLocalTime():g}" +
                $"{(drawn ? string.Empty : "  (muted)")}\r\n");
        }

        lines.Append(
            "\r\nThe hybrid still draws: each average is honest about its own " +
            "driver, and their levels are held by the loopback rather than by the " +
            "arrays matching. Re-measure only if the odd capture's array sampled a " +
            "different volume from the rest — and read an L/R comparison carefully " +
            "when the two SIDES are what differ, because then the sides are not being " +
            "asked the same question.\r\n\r\nThe dates are there because " +
            "nothing records WHERE the microphones stood, and nothing can derive " +
            "it: a rig lifted and set down somewhere else between two channels " +
            "leaves every stored property identical. Captures from one sitting " +
            "are one volume; captures from different days may not be.");
        return lines.ToString();
    }

    /// <summary>Aggregates (mixed per-position calibrations) name no curve, so they are compared band by band.</summary>
    private static bool SameArrayCorrection(
        LiveCaptureDocument first,
        LiveCaptureDocument second)
    {
        if (first.CalibrationIsAggregate != second.CalibrationIsAggregate)
        {
            return false;
        }
        if (!first.CalibrationIsAggregate)
        {
            return SameCalibration(first.Calibration, second.Calibration);
        }

        double[]? a = first.CalibrationCorrectionDb;
        double[]? b = second.CalibrationCorrectionDb;
        if (a == null || b == null)
        {
            return a == b;
        }
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int band = 0; band < a.Length; band++)
        {
            // 0.01 dB: closer is one correction written twice.
            if (Math.Abs(a[band] - b[band]) > 0.01)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameCalibration(
        VirtualCrossoverCalibrationSettings? first,
        VirtualCrossoverCalibrationSettings? second)
    {
        if (first == null || second == null)
        {
            return first == null && second == null;
        }

        return CalibrationFile.SameCurve(
            first.ToCalibrationFile(),
            second.ToCalibrationFile());
    }

    private string FormatHybridSpreadDetail(
        HybridMagnitudes hybrid, List<ProcessedChannel> processed)
    {
        var lines = new StringBuilder();
        lines.Append(
            SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray
                ? "Every array is referenced to the same loopback its impulse " +
                    "response is, so each channel should sit the same distance from " +
                    "it. These do not:\r\n\r\n"
                : "Every capture in one set is taken with one analyzer recipe at one " +
                    "input gain, so each channel should sit the same distance from " +
                    "its impulse response. These do not:\r\n\r\n");
        // The whole set, muted included: the spread was measured over it, and a mute must not hide the outlier.
        if (hybrid.SetDatumsDb.Count > 0)
        {
            var drawn = processed.Select(item => item.Channel).ToHashSet();
            foreach (SetDatum entry in hybrid.SetDatumsDb)
            {
                string figure = entry.DatumDb is { } datum
                    ? $"{datum:+0.0;-0.0} dB"
                    : "no overlap to compare";
                string muted = drawn.Contains(entry.Channel) ? string.Empty : "  (muted)";
                lines.Append(
                    $"    {entry.Channel.Name} {entry.Channel.Settings.DisplayName}" +
                    $"    {figure}{muted}\r\n");
            }
        }
        else
        {
            // Positional, nulls included: packing once shifted figures onto the wrong driver's name.
            for (int i = 0; i < hybrid.ChannelOffsetsDb.Count && i < processed.Count; i++)
            {
                VirtualCrossoverChannel channel = processed[i].Channel;
                string figure = hybrid.ChannelOffsetsDb[i] is { } offset
                    ? $"{offset:+0.0;-0.0} dB"
                    : "no overlap to compare";
                lines.Append(
                    $"    {channel.Name} {channel.Settings.DisplayName}    {figure}\r\n");
            }
        }

        lines.Append(
            SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray
                ? "\r\nAn array set should agree closely, so a channel standing " +
                    "apart usually means its array read a different input, a " +
                    "different calibration, or a driver that was not the one being " +
                    "measured. "
                : "\r\nUsually one capture was taken with a different input gain, a " +
                    "different frame length or window (which moves the noise-slope " +
                    "compensation), or belongs to another session. ");
        lines.Append(
            "The hybrid still draws: one offset serves the whole set, so a channel " +
            "that disagrees is drawn at the level it claims.");
        return lines.ToString();
    }

    private void ShowWarning(string text, string detail, Color color) =>
        WarningChanged?.Invoke(text, detail, color);

    private void HideWarning() =>
        WarningChanged?.Invoke(string.Empty, string.Empty, CrossoverWarningColor);

    // Amber: the view cannot be read yet, not a tuning error.
    private static readonly Color GateWarningColor = Color.FromArgb(230, 184, 0);

    private static readonly Color InfoWarningColor = Color.FromArgb(150, 170, 200);
    private static readonly Color CrossoverWarningColor = Color.FromArgb(235, 110, 95);

    // A steep/narrow LF band-pass arrives so late that Auto delay pushes every driver out by this much.
    private const double CrossoverGroupDelayWarningMs = 15.0;

    // Reads the applied delays, not a GD proxy (a narrow LF band-pass peaks late in its own band). Bypassed excluded.
    private void UpdateCrossoverWarning(List<ProcessedChannel> processed)
    {
        if (CrossoverSpreadWarning([.. processed.Select(item => item.Channel)])
            is not (string name, double spread, IReadOnlyList<VirtualCrossoverZone> placed))
        {
            HideWarning();
            return;
        }

        ShowWarning(
            $"⚠ {name} lags the others by ~{spread:0} ms — check its crossover.",
            $"{name} arrives ~{spread:0} ms after the other drivers, so Auto delay pushes " +
            "them out by that much to match it.\r\n\r\n" +
            "This is usually excessive crossover group delay — a narrow or steep low-frequency " +
            "band-pass. Reduce its slope or widen its band to bring the alignment delays down." +
            // Names the groups the spread left out, only those the project has.
            ExcludedGroupsNote(placed),
            CrossoverWarningColor);
    }

    internal static string ExcludedGroupsNote(IReadOnlyList<VirtualCrossoverZone> placed)
    {
        bool rear = placed.Contains(VirtualCrossoverZone.Rear);
        bool centre = placed.Contains(VirtualCrossoverZone.Center);
        return (rear, centre) switch
        {
            (true, true) =>
                "\r\n\r\nThe rear fill and the centre are not counted. They are placed " +
                    "against the front stage rather than tuned with it, and the rear sits " +
                    "its fill offset behind by design.",
            (true, false) =>
                "\r\n\r\nThe rear fill is not counted. It is placed against the front stage " +
                    "rather than tuned with it, and sits its fill offset behind by design.",
            (false, true) =>
                "\r\n\r\nThe centre is not counted. It is placed against the front stage " +
                    "rather than tuned with it.",
            _ => string.Empty
        };
    }

    internal static (string Name, double SpreadMs, IReadOnlyList<VirtualCrossoverZone> Placed)?
        CrossoverSpreadWarning(IReadOnlyList<VirtualCrossoverChannel> channels)
    {
        // Front chain only: later stages are PLACED and drag nothing (a 15 ms rear fill would trip the warning itself).
        // See docs/tech/virtual-dsp-panel.md#staged-auto-delay.
        (List<VirtualCrossoverChannel> active, List<VirtualCrossoverChannel> placed) =
            SplitAlignmentStages([.. channels.Where(channel => !channel.Pair.Bypass)]);
        if (active.Count < 2)
        {
            return null;
        }

        // The latest driver holds the smallest delay.
        VirtualCrossoverChannel latest = active.MinBy(channel => channel.Settings.DelayMs)!;
        double earliestDelay = active.Max(channel => channel.Settings.DelayMs);
        double spread = earliestDelay - latest.Settings.DelayMs;
        return spread > CrossoverGroupDelayWarningMs
            ? (latest.Name, spread, [.. placed.Select(channel => channel.Pair.Zone).Distinct()])
            : null;
    }

    // Stages and tie-breaks live in AutoAlignmentEngine / AlignmentSelection. Previous delays and polarities are
    // ignored: each run is an absolute proposal.
    private async void AutoAlignDelay()
    {
        (AutoDelayLaunch? launch, _) = PrepareAutoDelay(interactive: true);
        if (launch == null)
        {
            return;
        }

        using var dialog = new VirtualCrossoverAutoDelayDialog();
        // The dialog edits layout-neutral magnitudes; the project stores them layout-signed (see CommitAutoDelayResult).
        dialog.Init(
            launch.Stereo,
            project.StereoSceneOffsetMagnitudeMs,
            project.StereoRightHandDrive,
            Math.Abs(project.StereoLevelDifferenceDb),
            launch.Runner,
            launch.PolarityWarning,
            launch.HasRearFill,
            project.RearFillOffsetMs);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } result ||
            IsDisposed)
        {
            return;
        }

        await ApplyConfirmedAutoDelayAsync(result);
    }

    /// <summary>A prepared Auto delay run; the button hands it to the dialog, an AI import runs it directly.</summary>
    private sealed record AutoDelayLaunch(
        bool Stereo,
        Func<AutoDelayRunRequest, Task<AutoDelayRunResult>> Runner,
        string? PolarityWarning,
        bool HasRearFill);

    // Headless (AI import) shows nothing and treats the broad-window question as a refusal.
    private (AutoDelayLaunch? Launch, string? Refusal) PrepareAutoDelay(bool interactive)
    {
        // Stereo when some non-mono pair has both sides resolved (the highest becomes the L/R bridge).
        (List<VirtualCrossoverSideAlignmentChannel> leftSide, List<VirtualCrossoverSideAlignmentChannel> rightSide) =
            CollectStereoSides(channels);
        VirtualCrossoverSideAlignmentChannel? bridgeRight =
            PickStereoBridge(leftSide, rightSide);
        if (bridgeRight != null && leftSide.Count(InFrontChain) >= 2)
        {
            return PrepareStereoAutoDelay(leftSide, rightSide, bridgeRight, interactive);
        }

        return PrepareSingleSideAutoDelay(interactive);
    }

    // The dialog's modality keeps channel settings stable during the background compute; an import disables the panel.
    private (AutoDelayLaunch? Launch, string? Refusal) PrepareSingleSideAutoDelay(bool interactive)
    {
        // No DSP here: crop and ApplyChain run later in ComputeAutoAlignment's AlignmentReprocessor.
        List<VirtualCrossoverChannel> participants = channels
            .Where(channel =>
                channel.Pair.Enabled && channel.TransferImpulseResponse != null)
            .ToList();
        if (participants.Count < 2)
        {
            if (interactive)
            {
                System.Media.SystemSounds.Beep.Play();
            }

            return (null, "fewer than two enabled channels have a measurement");
        }

        // Refuse bypassed channels: overrides would not move them, yet they would join the walk and get a delay applied later.
        List<VirtualCrossoverChannel> bypassed = participants
            .Where(channel => channel.Pair.Bypass)
            .ToList();
        if (bypassed.Count > 0)
        {
            if (interactive)
            {
                ShowError(
                    "Auto delay cannot run with bypassed channels.",
                    "Bypass feeds the raw measured signal, so the computed delays " +
                    "and polarities would not apply to: " +
                    string.Join(", ", bypassed.Select(channel => channel.Name)) +
                    ".\r\n\r\nDisable Bypass on every participating channel " +
                    "(or mute the channel to exclude it) and run Auto delay again.");
            }

            return (null, "a participating channel is bypassed: " +
                string.Join(", ", bypassed.Select(channel => channel.Name)));
        }

        if (interactive ? RefuseOnMisplacedGate("Auto delay") : GateIsMisplaced)
        {
            return (null, "the phase gate is misplaced");
        }

        // Without crossovers the search uses a broad midband window and the result shifts once filters are set.
        bool anyCrossover = participants.Any(
            channel => channel.Settings.EffectiveCrossover.Kind != CrossoverKind.Off);
        if (!anyCrossover && !interactive)
        {
            return (null, "no channel has a crossover configured; set the crossovers first");
        }
        if (!anyCrossover)
        {
            DialogResult answer = MessageBox.Show(
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
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return (null, "cancelled");
            }
        }

        (double minHz, double maxHz) = VirtualCrossoverJunctions.GetCrossoverWindow(
            participants.Select(channel => channel.Settings));

        return (
            new AutoDelayLaunch(
                Stereo: false,
                request => RunSingleSideProposalAsync(participants, minHz, maxHz, request),
                PolarityWarning: null,
                HasRearFill: participants.Any(channel =>
                    channel.Pair.Zone == VirtualCrossoverZone.Rear)),
            null);
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

    // A mono block appears once (as its left instance), so a centre is found exactly once.
    /// <summary>Top front-chain pair resolved on both sides, or null where the run cannot be a stereo one.
    /// The bridge must be a front-chain pair, or the scene anchors to the rear fill.</summary>
    internal static VirtualCrossoverSideAlignmentChannel? PickStereoBridge(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide) =>
        rightSide
            .Where(item => item.RightSide &&
                InFrontChain(item) &&
                leftSide.Any(left =>
                    left.Runtime == item.Runtime && !left.RightSide))
            .OrderBy(item => VirtualCrossoverJunctions.BandCenterHz(item.Settings))
            .LastOrDefault();

    /// <summary>Bridge band = INTERSECTION of both sides' playing bands; null where they barely overlap.</summary>
    internal static (double LowHz, double HighHz)? StereoBridgeBand(
        VirtualCrossoverSideAlignmentChannel bridgeLeft,
        VirtualCrossoverSideAlignmentChannel bridgeRight)
    {
        (double leftLowHz, double leftHighHz) =
            VirtualCrossoverJunctions.GetChannelBand(bridgeLeft.Settings);
        (double rightLowHz, double rightHighHz) =
            VirtualCrossoverJunctions.GetChannelBand(bridgeRight.Settings);
        double lowHz = Math.Max(leftLowHz, rightLowHz);
        double highHz = Math.Min(leftHighHz, rightHighHz);
        return highHz < lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio
            ? null
            : (lowHz, highHz);
    }

    internal static bool InFrontChain(VirtualCrossoverSideAlignmentChannel side) =>
        VirtualCrossoverAlignmentStages.StageOf(side.Runtime.Pair.Zone) ==
            VirtualCrossoverAlignmentStage.FrontChain;

    // Rear fill placed PER SIDE against its own side's front stage; the centre between both.
    // Returns the channels carrying the rear-fill offset, for the normalization pass.
    private IReadOnlyCollection<IAlignmentChannel> PlaceLaterStagesStereo(
        IReadOnlyList<VirtualCrossoverSideAlignmentChannel> chainReference,
        IReadOnlyList<VirtualCrossoverSideAlignmentChannel> chainFar,
        IReadOnlyList<VirtualCrossoverSideAlignmentChannel> later,
        AlignmentReprocessor reprocessor,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        double sceneOffsetMs,
        double rearFillOffsetMs,
        bool rightHandDrive,
        System.Text.StringBuilder log)
    {
        var fillCarriers = new List<IAlignmentChannel>();
        IReadOnlyList<AlignmentSnapshot> settled = reprocessor.Reprocess(alignment);
        Dictionary<IAlignmentChannel, AlignmentSnapshot> byChannel =
            settled.ToDictionary(snapshot => snapshot.Channel);
        Complex[] SumOf(IEnumerable<VirtualCrossoverSideAlignmentChannel> group) =>
            VirtualCrossoverAnalysis.SumImpulseResponses(
                [.. group.Select(side => byChannel[side].ImpulseResponse)]);
        (double LowHz, double HighHz) BandOf(
            IEnumerable<VirtualCrossoverSideAlignmentChannel> group)
        {
            double low = double.MaxValue;
            double high = double.MinValue;
            foreach (VirtualCrossoverSideAlignmentChannel side in group)
            {
                (double sideLow, double sideHigh) =
                    VirtualCrossoverJunctions.GetChannelBand(side.Settings);
                low = Math.Min(low, sideLow);
                high = Math.Max(high, sideHigh);
            }

            return (low, high);
        }

        // Rebound by the inner walks so later groups read settled responses.
        Complex[] referenceSum = SumOf(chainReference);
        Complex[] farSum = SumOf(chainFar);
        // Each side's own band: the two sides may carry different crossover corners.
        (double referenceLow, double referenceHigh) = BandOf(chainReference);
        (double farLow, double farHigh) = BandOf(chainFar);
        int sampleRate = chainReference[0].SampleRate;

        // Grouped by cabin side so a two-way rear settles its own junction first.
        foreach (IGrouping<bool, VirtualCrossoverSideAlignmentChannel> sideGroup in
            later
                .Where(item =>
                    VirtualCrossoverAlignmentStages.StageOf(item.Runtime.Pair.Zone) ==
                        VirtualCrossoverAlignmentStage.Rear)
                .GroupBy(item => item.RightSide))
        {
            List<VirtualCrossoverSideAlignmentChannel> members = [.. sideGroup];
            // Sums arrive in engine ROLES: on right-hand drive the reference is the right side.
            bool far = IsFarSide(sideGroup.Key, rightHandDrive);
            Dictionary<IAlignmentChannel, AlignmentOverride> inner = SettleWithinGroup(
                [.. members.Cast<IAlignmentChannel>()],
                member => ((VirtualCrossoverSideAlignmentChannel)member).Settings,
                byChannel,
                reprocessor,
                log);
            if (inner.Count > 0)
            {
                ApplyInnerSettlement(members, inner, alignment);
                settled = reprocessor.Reprocess(alignment);
                byChannel = settled.ToDictionary(snapshot => snapshot.Channel);
            }

            string name = string.Join("+", members.Select(item => item.Name));
            (double sideLow, double sideHigh) = BandOf(members);
            // One front driver, not the summed stage: the sum's arrival belongs to the earliest player (a tweeter).
            (VirtualCrossoverSideAlignmentChannel Channel, double LowHz, double HighHz)? pick =
                VirtualCrossoverGroupPlacement.ChooseReference(
                    far ? chainFar : chainReference,
                    item => VirtualCrossoverJunctions.GetChannelBand(item.Settings),
                    sideLow,
                    sideHigh);
            Complex[] against = pick is { } chosen
                ? byChannel[chosen.Channel].ImpulseResponse
                : far ? farSum : referenceSum;
            string againstName = pick is { } named
                ? named.Channel.Name
                : "this side's front stage";
            double lowHz = pick?.LowHz ?? Math.Max(far ? farLow : referenceLow, sideLow);
            double highHz = pick?.HighHz ?? Math.Min(far ? farHigh : referenceHigh, sideHigh);
            GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
                against,
                SumOf(members),
                sampleRate,
                lowHz,
                highHz);
            if (placement == null)
            {
                log.AppendLine(
                    $"  rear {name}: not placed - no reliable arrival in " +
                    $"{lowHz:0}-{highHz:0} Hz against {againstName}. " +
                    "Its current delay stands.");
                foreach (VirtualCrossoverSideAlignmentChannel member in members)
                {
                    alignment[member] = new AlignmentOverride(
                        member.Settings.DelayMs, member.Settings.InvertPolarity);
                    decisions[member] = new AlignmentDecision(
                        AlignmentDecisionKind.Locked,
                        null,
                        $"not placed: no reliable arrival in {lowHz:0}-{highHz:0} Hz " +
                        $"against {againstName}, so the current delay stands");
                }

                continue;
            }

            fillCarriers.AddRange(members);
            double delayMs = placement.CoArrivalDelayMs + rearFillOffsetMs;
            bool invert = rearFillOffsetMs < HaasPolarityIrrelevantMs &&
                placement.Inverted;
            log.AppendLine(
                $"  rear {name}: {delayMs:+0.00;-0.00;0.00} ms (co-arrival " +
                $"{placement.CoArrivalDelayMs:+0.00;-0.00;0.00}" +
                (rearFillOffsetMs != 0
                    ? $" plus {rearFillOffsetMs:0.##} ms fill"
                    : string.Empty) +
                $"), against {againstName} in {lowHz:0}-{highHz:0} Hz, " +
                $"r {placement.Coefficient:0.00}" +
                (invert ? ", inverted" : string.Empty) +
                (placement.EdgePinned
                    ? ", pinned to the refinement edge (the arrival stands, " +
                        "no polarity claimed)"
                    : string.Empty));
            foreach (VirtualCrossoverSideAlignmentChannel member in members)
            {
                double innerMs = inner.TryGetValue(member, out AlignmentOverride own)
                    ? own.DelayMs
                    : 0.0;
                bool innerInvert =
                    inner.TryGetValue(member, out AlignmentOverride flip) &&
                    flip.InvertPolarity;
                alignment[member] = new AlignmentOverride(
                    innerMs + delayMs, innerInvert ^ invert);
                decisions[member] = PlacementDecision(
                    placement.Coefficient,
                    corroborated: !placement.EdgePinned,
                    $"placed as a rear group against {againstName} in " +
                    $"{lowHz:0}-{highHz:0} Hz, r {placement.Coefficient:0.00}" +
                    (rearFillOffsetMs != 0
                        ? $", held back {rearFillOffsetMs:0.##} ms"
                        : string.Empty));
            }
        }

        // The centre is read against one reference per side (ChooseCentreReferences: peer drivers, else each side's own
        // content) and placed at the midpoint; the readings should differ by the scene offset, so a disagreement is reported, not averaged.
        List<VirtualCrossoverSideAlignmentChannel> centreMembers = [.. later.Where(item =>
            VirtualCrossoverAlignmentStages.StageOf(item.Runtime.Pair.Zone) ==
                VirtualCrossoverAlignmentStage.Center)];
        if (centreMembers.Count > 0)
        {
            // A two-way centre settles its own junction first, then is placed as one.
            Dictionary<IAlignmentChannel, AlignmentOverride> inner = SettleWithinGroup(
                [.. centreMembers.Cast<IAlignmentChannel>()],
                member => ((VirtualCrossoverSideAlignmentChannel)member).Settings,
                byChannel,
                reprocessor,
                log);
            if (inner.Count > 0)
            {
                ApplyInnerSettlement(centreMembers, inner, alignment);
                settled = reprocessor.Reprocess(alignment);
                byChannel = settled.ToDictionary(snapshot => snapshot.Channel);
            }

            string centreName =
                string.Join("+", centreMembers.Select(item => item.Name));
            // Both readings over the SAME band: intersection of both references, narrowed to the centre.
            (double centreLow, double centreHigh) = BandOf(centreMembers);
            // See VirtualCrossoverGroupPlacement.ChooseCentreReferences (testable; mirrors matching the centre to the voice-band driver).
            CentreReferenceChoice<VirtualCrossoverSideAlignmentChannel> choice =
                VirtualCrossoverGroupPlacement.ChooseCentreReferences(
                    chainReference,
                    chainFar,
                    item => VirtualCrossoverJunctions.GetChannelBand(item.Settings),
                    (near, far) => near.Runtime == far.Runtime && near != far,
                    centreLow,
                    centreHigh);
            if (choice.Plan is not { } plan)
            {
                // No plan is a refusal: a midpoint between readings that share no content is not a placement.
                log.AppendLine(
                    $"  centre {centreName}: not placed - {choice.Refusal}. " +
                    "Its current delay stands.");
                foreach (VirtualCrossoverSideAlignmentChannel member in centreMembers)
                {
                    alignment[member] = new AlignmentOverride(
                        member.Settings.DelayMs, member.Settings.InvertPolarity);
                    decisions[member] = new AlignmentDecision(
                        AlignmentDecisionKind.Locked,
                        null,
                        $"not placed: {choice.Refusal}, so there is no midpoint " +
                        "to place the centre between and the current delay stands");
                }

                return fillCarriers;
            }

            double lowHz = plan.LowHz;
            double highHz = plan.HighHz;
            string Describe(IReadOnlyList<VirtualCrossoverSideAlignmentChannel> side) =>
                (plan.Peers ? string.Empty : "the own content ") +
                string.Join("+", side.Select(item => item.Name));
            string nearName = Describe(plan.Near);
            string farName = Describe(plan.Far);
            Complex[] nearIr = SumOf(plan.Near);
            Complex[] farIr = SumOf(plan.Far);

            Complex[] centreIr = SumOf(centreMembers);
            GroupPlacement? againstReference = VirtualCrossoverGroupPlacement.Place(
                nearIr, centreIr, sampleRate, lowHz, highHz);
            GroupPlacement? againstFar = VirtualCrossoverGroupPlacement.Place(
                farIr, centreIr, sampleRate, lowHz, highHz);
            if (againstReference == null || againstFar == null)
            {
                log.AppendLine(
                    $"  centre {centreName}: not placed - no reliable arrival " +
                    $"in {lowHz:0}-{highHz:0} Hz against " +
                    (againstReference == null ? nearName : farName) +
                    ". Its current delay stands.");
                foreach (VirtualCrossoverSideAlignmentChannel member in centreMembers)
                {
                    alignment[member] = new AlignmentOverride(
                        member.Settings.DelayMs, member.Settings.InvertPolarity);
                    decisions[member] = new AlignmentDecision(
                        AlignmentDecisionKind.Locked,
                        null,
                        $"not placed: no reliable arrival in {lowHz:0}-{highHz:0} Hz " +
                        $"against {nearName} and {farName}, so the current delay stands");
                }

                return fillCarriers;
            }

            (double delayMs, bool inverted, CentreCorroboration corroboration) =
                VirtualCrossoverGroupPlacement.Midpoint(
                    againstReference,
                    againstFar,
                    sceneOffsetMs,
                    CentreWitnessToleranceMs);
            log.AppendLine(
                $"  centre {centreName}: {delayMs:+0.00;-0.00;0.00} ms - midway " +
                $"between {againstReference.CoArrivalDelayMs:+0.00;-0.00;0.00} " +
                $"(vs {nearName}, r {againstReference.Coefficient:0.00}) and " +
                $"{againstFar.CoArrivalDelayMs:+0.00;-0.00;0.00} " +
                $"(vs {farName}, r {againstFar.Coefficient:0.00}) in " +
                $"{lowHz:0}-{highHz:0} Hz" +
                (inverted ? ", inverted" : string.Empty) +
                $" - {corroboration.Describe()}" +
                (corroboration.Confident ? string.Empty : " - LOW CONFIDENCE"));
            foreach (VirtualCrossoverSideAlignmentChannel member in centreMembers)
            {
                double innerMs = inner.TryGetValue(member, out AlignmentOverride own)
                    ? own.DelayMs
                    : 0.0;
                bool innerInvert =
                    inner.TryGetValue(member, out AlignmentOverride flip) &&
                    flip.InvertPolarity;
                alignment[member] = new AlignmentOverride(
                    innerMs + delayMs, innerInvert ^ inverted);
                decisions[member] = PlacementDecision(
                    Math.Min(againstReference.Coefficient, againstFar.Coefficient),
                    corroboration.Confident,
                    $"placed midway between {nearName} and {farName} in " +
                    $"{lowHz:0}-{highHz:0} Hz - {corroboration.Describe()}");
            }
        }

        return fillCarriers;
    }

    /// <summary>The driver's side is the reference, so the far side is the other.</summary>
    internal static bool IsFarSide(bool rightSide, bool rightHandDrive) =>
        rightSide != rightHandDrive;

    // Allowed disagreement beyond the scene offset: envelope vs phase-extremum on two paths, narrower than a lobe.
    private const double CentreWitnessToleranceMs = 0.35;

    // Beyond this offset the groups no longer sum audibly, so polarity describes the measurement, not the listener.
    private const double HaasPolarityIrrelevantMs = 5.0;

    // A project without rear fill or centre gets everything in the chain and takes the unstaged path.
    internal static (List<VirtualCrossoverChannel> Chain, List<VirtualCrossoverChannel> Later)
        SplitAlignmentStages(IReadOnlyList<VirtualCrossoverChannel> participants)
    {
        List<VirtualCrossoverChannel> chain = [.. participants.Where(channel =>
            VirtualCrossoverAlignmentStages.StageOf(channel.Pair.Zone) ==
                VirtualCrossoverAlignmentStage.FrontChain)];
        List<VirtualCrossoverChannel> later = [.. participants.Except(chain)];
        // A rear-only project is its own chain.
        return chain.Count == 0 || later.Count == 0
            ? ([.. participants], [])
            : (chain, later);
    }

    private static (double LowHz, double HighHz) GroupBandOf(
        IEnumerable<VirtualCrossoverChannel> members)
    {
        double low = double.MaxValue;
        double high = double.MinValue;
        foreach (VirtualCrossoverChannel member in members)
        {
            (double memberLow, double memberHigh) =
                VirtualCrossoverJunctions.GetChannelBand(member.Settings);
            low = Math.Min(low, memberLow);
            high = Math.Max(high, memberHigh);
        }

        return (low, high);
    }

    /// <summary>Walks a later group's own junctions first, so it is placed as one settled body.</summary>
    /// <returns>The engine's SPARSE map (its reference has no entry); compose via <see cref="ApplyInnerSettlement"/>.</returns>
    internal static Dictionary<IAlignmentChannel, AlignmentOverride> SettleWithinGroup(
        IReadOnlyList<IAlignmentChannel> members,
        Func<IAlignmentChannel, VirtualCrossoverChannelSettings> settingsOf,
        IReadOnlyDictionary<IAlignmentChannel, AlignmentSnapshot> snapshots,
        AlignmentReprocessor reprocessor,
        System.Text.StringBuilder log)
    {
        var inner = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        if (members.Count < 2)
        {
            return inner;
        }

        List<IAlignmentChannel> byBand = [.. members.OrderBy(member =>
            VirtualCrossoverJunctions.BandCenterHz(settingsOf(member)))];
        var junctions = new List<AlignmentJunction>();
        for (int i = 0; i < byBand.Count - 1; i++)
        {
            double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                settingsOf(byBand[i]), settingsOf(byBand[i + 1]));
            (double lowHz, double highHz) = VirtualCrossoverJunctions.OverlapBand(pairHz);
            junctions.Add(new AlignmentJunction(
                snapshots[byBand[i]], snapshots[byBand[i + 1]], pairHz, lowHz, highHz));
        }

        if (junctions.Count == 0)
        {
            return inner;
        }

        log.AppendLine(
            $"  settling {byBand.Count} drivers within the group " +
            $"({junctions.Count} junction(s)) before placing it:");
        AutoAlignmentEngine.Compute(
            [.. byBand.Select(member => snapshots[member])],
            junctions,
            reprocessor.Reprocess,
            inner,
            log);
        return inner;
    }

    /// <summary>Never index <c>inner[member]</c>: the engine's map is sparse and omits its reference channel.</summary>
    internal static void ApplyInnerSettlement(
        IEnumerable<IAlignmentChannel> members,
        IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> inner,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment)
    {
        foreach (IAlignmentChannel member in members)
        {
            alignment[member] = inner.GetValueOrDefault(member);
        }
    }

    // A placement the run does not trust must arrive in the report marked.
    private static AlignmentDecision PlacementDecision(
        double coefficient,
        bool corroborated,
        string detail)
    {
        AlignmentConfidence confidence =
            !corroborated || coefficient < VirtualCrossoverGroupPlacement.MinimumTrustedCoefficient
                ? AlignmentConfidence.Low
                : coefficient >= StrongPlacementCoefficient
                    ? AlignmentConfidence.High
                    : AlignmentConfidence.Medium;
        return new AlignmentDecision(AlignmentDecisionKind.Search, confidence, detail);
    }

    // Groups playing one band from different places never correlate like a crossover, so the bar is below a junction's.
    private const double StrongPlacementCoefficient = 0.6;

    // Each later group gets ONE delay for all members (rigid body).
    // Returns the channels carrying the rear-fill offset, for the normalization pass.
    private IReadOnlyCollection<IAlignmentChannel> PlaceLaterStages(
        IReadOnlyList<VirtualCrossoverChannel> chain,
        IReadOnlyList<VirtualCrossoverChannel> later,
        AlignmentReprocessor reprocessor,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        double rearFillOffsetMs,
        System.Text.StringBuilder log)
    {
        var fillCarriers = new List<IAlignmentChannel>();
        IReadOnlyList<AlignmentSnapshot> settled = reprocessor.Reprocess(alignment);
        Dictionary<IAlignmentChannel, AlignmentSnapshot> byChannel =
            settled.ToDictionary(snapshot => snapshot.Channel);
        Complex[] SumOf(IEnumerable<VirtualCrossoverChannel> group) =>
            VirtualCrossoverAnalysis.SumImpulseResponses(
                [.. group.Select(channel => byChannel[channel].ImpulseResponse)]);
        // Rebound (not shadowed) by the inner walks: later groups read settled responses.


        Complex[] reference = SumOf(chain);
        int sampleRate = chain[0].SampleRate;
        (double chainLow, double chainHigh) = GroupBandOf(chain);
        foreach (VirtualCrossoverAlignmentStage stage in
            VirtualCrossoverAlignmentStages.InOrder.Where(
                item => item != VirtualCrossoverAlignmentStage.FrontChain))
        {
            List<VirtualCrossoverChannel> members = [.. later.Where(channel =>
                VirtualCrossoverAlignmentStages.StageOf(channel.Pair.Zone) == stage)];
            if (members.Count == 0)
            {
                continue;
            }

            Dictionary<IAlignmentChannel, AlignmentOverride> inner = SettleWithinGroup(
                [.. members.Cast<IAlignmentChannel>()],
                member => ((VirtualCrossoverChannel)member).Settings,
                byChannel,
                reprocessor,
                log);
            if (inner.Count > 0)
            {
                ApplyInnerSettlement(members, inner, alignment);
                settled = reprocessor.Reprocess(alignment);
                byChannel = settled.ToDictionary(snapshot => snapshot.Channel);
            }

            (double groupLow, double groupHigh) = GroupBandOf(members);
            // One front driver, not the chain summed (see ChooseReference).
            (VirtualCrossoverChannel Channel, double LowHz, double HighHz)? pick =
                VirtualCrossoverGroupPlacement.ChooseReference(
                    chain,
                    item => VirtualCrossoverJunctions.GetChannelBand(item.Settings),
                    groupLow,
                    groupHigh);
            Complex[] against = pick is { } chosen
                ? byChannel[chosen.Channel].ImpulseResponse
                : reference;
            string againstName = pick is { } named ? named.Channel.Name : "the front stage";
            double lowHz = pick?.LowHz ?? Math.Max(chainLow, groupLow);
            double highHz = pick?.HighHz ?? Math.Min(chainHigh, groupHigh);
            GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
                against, SumOf(members), sampleRate, lowHz, highHz);
            if (placement == null)
            {
                // Written explicitly: an absent override means zero to the reprocessor.
                log.AppendLine(
                    $"  {stage}: not placed - the band it shares with {againstName} " +
                    $"({lowHz:0}-{highHz:0} Hz) holds no reliable arrival. " +
                    "Its current delay stands.");
                foreach (VirtualCrossoverChannel member in members)
                {
                    alignment[member] = new AlignmentOverride(
                        member.Settings.DelayMs, member.Settings.InvertPolarity);
                    decisions[member] = new AlignmentDecision(
                        AlignmentDecisionKind.Locked,
                        null,
                        $"not placed: no reliable arrival in {lowHz:0}-{highHz:0} Hz " +
                        $"against {againstName}, so the current delay stands");
                }

                continue;
            }

            // A rear fill is wanted behind the front (precedence effect); a centre takes no offset.
            double offsetMs = stage == VirtualCrossoverAlignmentStage.Rear
                ? rearFillOffsetMs
                : 0.0;
            if (stage == VirtualCrossoverAlignmentStage.Rear)
            {
                fillCarriers.AddRange(members);
            }

            double delayMs = placement.CoArrivalDelayMs + offsetMs;
            bool invert = offsetMs < HaasPolarityIrrelevantMs && placement.Inverted;
            log.AppendLine(
                $"  {stage}: {delayMs:+0.00;-0.00;0.00} ms (co-arrival " +
                $"{placement.CoArrivalDelayMs:+0.00;-0.00;0.00}" +
                (offsetMs != 0 ? $" plus {offsetMs:0.##} ms fill" : string.Empty) +
                $"), against {againstName} in {lowHz:0}-{highHz:0} Hz, " +
                $"r {placement.Coefficient:0.00}" +
                (invert ? ", inverted" : string.Empty) +
                (placement.EdgePinned
                    ? ", pinned to the refinement edge (the arrival stands, " +
                        "no polarity claimed)"
                    : string.Empty) +
                (placement.EdgePinned ||
                    placement.Coefficient <
                        VirtualCrossoverGroupPlacement.MinimumTrustedCoefficient
                    ? " - LOW CONFIDENCE"
                    : string.Empty));
            foreach (VirtualCrossoverChannel member in members)
            {
                double innerMs = inner.TryGetValue(member, out AlignmentOverride own)
                    ? own.DelayMs
                    : 0.0;
                bool innerInvert = inner.TryGetValue(member, out AlignmentOverride flip) &&
                    flip.InvertPolarity;
                alignment[member] = new AlignmentOverride(
                    innerMs + delayMs, innerInvert ^ invert);
                decisions[member] = PlacementDecision(
                    placement.Coefficient,
                    corroborated: !placement.EdgePinned,
                    $"placed as a {stage} group against {againstName} in " +
                    $"{lowHz:0}-{highHz:0} Hz, r {placement.Coefficient:0.00}" +
                    (offsetMs != 0 ? $", held back {offsetMs:0.##} ms" : string.Empty));
            }
        }

        return fillCarriers;
    }

    // Slides every participant (absent = zero; the map omits the engine's reference) until the earliest is at zero.
    // See docs/tech/virtual-dsp-panel.md#delay-normalization.
    internal static void NormalizeStagedDelays(
        IReadOnlyList<IAlignmentChannel> scope,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        System.Text.StringBuilder log,
        double maxDelayMs = AutoAlignmentEngine.DefaultMaxDelayMs,
        double rearFillOffsetMs = 0,
        IReadOnlyCollection<IAlignmentChannel>? rearFillCarriers = null)
    {
        if (scope.Count == 0)
        {
            return;
        }

        // Raw placements before the shift: the fill scan folds min(0, minimum) in itself.
        Dictionary<IAlignmentChannel, double> raw = scope.Distinct().ToDictionary(
            channel => channel,
            channel => alignment.GetValueOrDefault(channel).DelayMs);

        double minimum = raw.Values.Min();
        if (minimum < 0)
        {
            log.AppendLine(
                $"  normalization: every channel shifted +{-minimum:0.00} ms so the " +
                "earliest sits at zero.");
            foreach (IAlignmentChannel channel in raw.Keys)
            {
                AlignmentOverride over = alignment.GetValueOrDefault(channel);
                alignment[channel] = over with { DelayMs = over.DelayMs - minimum };
            }
        }

        IAlignmentChannel widest = raw.Keys.MaxBy(
            channel => alignment.GetValueOrDefault(channel).DelayMs)!;
        double widestDelayMs = alignment.GetValueOrDefault(widest).DelayMs;
        if (widestDelayMs <= maxDelayMs + 0.005)
        {
            return;
        }

        string message =
            "The staged alignment does not fit the DSP delay range: " +
            $"{widest.Name} needs {widestDelayMs:0.00} ms with the earliest " +
            $"channel at 0, but the limit is {maxDelayMs:0.##} ms.";
        message += LargestFittingRearFill(
                raw, rearFillCarriers, rearFillOffsetMs, maxDelayMs)
            is double fittingMs
            ? $" The {rearFillOffsetMs:0.##} ms rear fill is what pushes it past " +
                $"the range: up to {fittingMs:0.##} ms of fill fits — lower " +
                "Rear fill in the dialog and rerun."
            : " The spread between the earliest and latest channels is wider " +
                "than the DSP can realize.";
        throw new InvalidOperationException(message);
    }

    // Walks down on the 0.01 ms grid: the dialable span is not monotone in the fill. Null when no fill is in play or a zero fill does not fit either.
    private static double? LargestFittingRearFill(
        IReadOnlyDictionary<IAlignmentChannel, double> raw,
        IReadOnlyCollection<IAlignmentChannel>? carriers,
        double requestedFillMs,
        double maxDelayMs)
    {
        if (carriers == null || carriers.Count == 0 || requestedFillMs <= 0)
        {
            return null;
        }

        for (double fillMs = Math.Floor(requestedFillMs * 100) / 100;
            fillMs >= 0;
            fillMs = Math.Round(fillMs - 0.01, 2))
        {
            double minMs = double.MaxValue;
            double maxMs = double.MinValue;
            foreach ((IAlignmentChannel channel, double delayMs) in raw)
            {
                double trialMs = carriers.Contains(channel)
                    ? delayMs - requestedFillMs + fillMs
                    : delayMs;
                minMs = Math.Min(minMs, trialMs);
                maxMs = Math.Max(maxMs, trialMs);
            }

            if (maxMs - Math.Min(0, minMs) <= maxDelayMs + 0.005)
            {
                return fillMs;
            }
        }

        return null;
    }

    private async Task<AutoDelayRunResult> RunSingleSideProposalAsync(
        List<VirtualCrossoverChannel> participants,
        double windowMinHz,
        double windowMaxHz,
        AutoDelayRunRequest request)
    {
        bool adjustGains = request.AdjustGains;
        var log = new System.Text.StringBuilder();
        log.AppendLine($"Auto delay {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        log.AppendLine($"Crossover window: {windowMinHz:0} - {windowMaxHz:0} Hz");
        log.AppendLine("Previous delay / polarity settings ignored for this run.");

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var decisions = new Dictionary<IAlignmentChannel, AlignmentDecision>();
        IReadOnlyList<GainBalanceResult>? gains = null;
        AutoDelaySumLossForecast? sumLoss = null;
        await Task.Run(() =>
        {
            // An unstaged project puts every participant in the chain, so this is the plain unstaged engine call.
            (List<VirtualCrossoverChannel> chain, List<VirtualCrossoverChannel> later) =
                SplitAlignmentStages(participants);
            AlignmentReprocessor reprocessor = ComputeAutoAlignment(
                participants,
                alignment,
                decisions,
                log,
                walkSet: later.Count > 0 ? chain : null);
            if (later.Count > 0)
            {
                IReadOnlyCollection<IAlignmentChannel> fillCarriers =
                    PlaceLaterStages(
                        chain, later, reprocessor, alignment, decisions,
                        request.RearFillOffsetMs, log);
                NormalizeStagedDelays(
                    [.. participants.Cast<IAlignmentChannel>()], alignment, log,
                    ProcessorMaxDelayMs, request.RearFillOffsetMs, fillCarriers);
            }

            // "Before" snapshots exist only for the report's before/after forecast.
            IReadOnlyList<AlignmentSnapshot> beforeSnapshots =
                reprocessor.Reprocess(participants.ToDictionary(
                    channel => (IAlignmentChannel)channel,
                    channel => new AlignmentOverride(
                        channel.Settings.DelayMs, channel.Settings.InvertPolarity)));
            if (adjustGains)
            {
                gains = ComputeGainBalance(
                    participants.Select(channel => (
                        (IAlignmentChannel)channel,
                        channel.Settings,
                        channel.Pair.Mono,
                        RightSide: false,
                        (IAlignmentChannel?)null)),
                    reprocessor, alignment, levelDifferenceDb: 0, log);
            }

            IReadOnlyList<AlignmentSnapshot> afterSnapshots =
                reprocessor.Reprocess(alignment);
            sumLoss = ForecastSumLoss(
                participants.Select(channel =>
                    ((IAlignmentChannel)channel, channel.Settings)).ToList(),
                ToIrMap(beforeSnapshots), ToIrMap(afterSnapshots),
                AdjustedGainMap(gains), windowMinHz, windowMaxHz);
        });

        List<AutoDelayChannelOutcome> outcomes = BuildOutcomes(
            participants.Select(channel => (
                (IAlignmentChannel)channel,
                Runtime: channel,
                channel.Settings,
                channel.Name)),
            alignment, decisions, gains);
        string report = VirtualCrossoverAutoDelayReport.Format(
            outcomes, stereo: false, request, sumLoss);
        // Written at the proposal stage so a discarded run can still be shared.
        WriteAlignmentLog(log.ToString());
        return new AutoDelayRunResult(outcomes, Stereo: false, request, report, log);
    }

    // Levels from the FINAL snapshots; the engine subtracts the baked-in gain, so the proposal is absolute.
    private static IReadOnlyList<GainBalanceResult> ComputeGainBalance(
        IEnumerable<(IAlignmentChannel Channel, VirtualCrossoverChannelSettings Settings,
            bool Mono, bool RightSide, IAlignmentChannel? LeftPeer)> channels,
        AlignmentReprocessor reprocessor,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        double levelDifferenceDb,
        System.Text.StringBuilder log)
    {
        IReadOnlyList<AlignmentSnapshot> snapshots = reprocessor.Reprocess(alignment);
        Dictionary<IAlignmentChannel, AlignmentSnapshot> byChannel =
            snapshots.ToDictionary(snapshot => snapshot.Channel);
        List<GainBalanceInput> inputs = channels
            .Select(item =>
            {
                (double lowHz, double highHz) =
                    VirtualCrossoverJunctions.GetChannelBand(item.Settings);
                return new GainBalanceInput(
                    item.Channel,
                    byChannel[item.Channel].ImpulseResponse,
                    item.Channel.SampleRate,
                    item.Settings.GainDb,
                    lowHz,
                    highHz,
                    item.Settings.EffectiveCrossover.Kind != CrossoverKind.Off,
                    item.Mono,
                    item.RightSide,
                    item.LeftPeer);
            })
            .ToList();
        return GainBalanceEngine.Compute(inputs, levelDifferenceDb, log);
    }

    private static Dictionary<IAlignmentChannel, Complex[]> ToIrMap(
        IReadOnlyList<AlignmentSnapshot> snapshots) =>
        snapshots.ToDictionary(
            snapshot => snapshot.Channel,
            snapshot => snapshot.ImpulseResponse);

    private static Dictionary<IAlignmentChannel, GainBalanceResult>? AdjustedGainMap(
        IReadOnlyList<GainBalanceResult>? gains) =>
        gains?.Where(result => result.Adjusted)
            .ToDictionary(result => result.Channel);

    // Proposed gains enter as spectrum scales (the reprocessor's chains carry current gains). Null below two channels.
    private static AutoDelaySumLossForecast? ForecastSumLoss(
        IReadOnlyList<(IAlignmentChannel Channel, VirtualCrossoverChannelSettings Settings)> sideChannels,
        IReadOnlyDictionary<IAlignmentChannel, Complex[]> beforeIrs,
        IReadOnlyDictionary<IAlignmentChannel, Complex[]> afterIrs,
        IReadOnlyDictionary<IAlignmentChannel, GainBalanceResult>? adjustedGains,
        double windowMinHz,
        double windowMaxHz)
    {
        if (sideChannels.Count < 2)
        {
            return null;
        }

        int sampleRate = sideChannels[0].Channel.SampleRate;
        double? before = VirtualCrossoverAnalysis.PredictedAverageSumLossDb(
            sideChannels.Select(item => beforeIrs[item.Channel]).ToList(),
            sampleRate, windowMinHz, windowMaxHz);
        List<double> scales = sideChannels
            .Select(item =>
                adjustedGains != null &&
                adjustedGains.TryGetValue(item.Channel, out GainBalanceResult? gain)
                    ? Math.Pow(10.0, (gain.ProposedGainDb - item.Settings.GainDb) / 20.0)
                    : 1.0)
            .ToList();
        double? after = VirtualCrossoverAnalysis.PredictedAverageSumLossDb(
            sideChannels.Select(item => afterIrs[item.Channel]).ToList(),
            sampleRate, windowMinHz, windowMaxHz, scales);
        return before.HasValue && after.HasValue
            ? new AutoDelaySumLossForecast(before.Value, after.Value)
            : null;
    }

    private static List<AutoDelayChannelOutcome> BuildOutcomes(
        IEnumerable<(IAlignmentChannel Channel, VirtualCrossoverChannel Runtime,
            VirtualCrossoverChannelSettings Settings, string Name)> channels,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        IReadOnlyList<GainBalanceResult>? gains)
    {
        Dictionary<IAlignmentChannel, GainBalanceResult>? gainByChannel =
            gains?.ToDictionary(result => result.Channel);
        var outcomes = new List<AutoDelayChannelOutcome>();
        foreach ((IAlignmentChannel channel, VirtualCrossoverChannel runtime,
            VirtualCrossoverChannelSettings settings, string name) in channels)
        {
            AlignmentOverride over = alignment.GetValueOrDefault(channel);
            AlignmentDecision? decision = decisions.GetValueOrDefault(channel);
            GainBalanceResult? gain = gainByChannel?.GetValueOrDefault(channel);
            bool gainAdjusted = gain?.Adjusted == true;
            outcomes.Add(new AutoDelayChannelOutcome(
                runtime,
                settings,
                name,
                settings.DelayMs,
                settings.InvertPolarity,
                settings.GainDb,
                Math.Round(over.DelayMs, 2),
                over.InvertPolarity,
                gainAdjusted ? gain!.ProposedGainDb : settings.GainDb,
                gainAdjusted,
                decision?.Kind,
                decision?.Confidence,
                decision?.Detail ?? string.Empty,
                gain?.Confidence,
                gain?.Detail ?? string.Empty));
        }

        return outcomes;
    }

    // Synchronous, so Apply fully lands or fails before anything is half-written; the log is rewritten at once.
    private void CommitAutoDelayResult(AutoDelayRunResult result)
    {
        // Stored so the next run on this car starts from the fill it settled on.
        project.RearFillOffsetMs = result.Request.RearFillOffsetMs;
        foreach (AutoDelayChannelOutcome outcome in result.Outcomes)
        {
            outcome.Settings.DelayMs = outcome.AfterDelayMs;
            outcome.Settings.InvertPolarity = outcome.AfterInvert;
            if (outcome.GainAdjusted)
            {
                outcome.Settings.GainDb = outcome.AfterGainDb;
            }

            result.Log.AppendLine(
                $"Result {outcome.Name}: " +
                $"delay {outcome.AfterDelayMs:0.00} ms, " +
                $"invert {(outcome.AfterInvert ? "yes" : "no")}" +
                (outcome.GainAdjusted
                    ? $", gain {outcome.AfterGainDb:0.0} dB"
                    : ""));
        }

        foreach (VirtualCrossoverChannel runtime in
            result.Outcomes.Select(outcome => outcome.Runtime).Distinct())
        {
            ApplySettingsToControl(runtime);
        }

        if (result.Stereo)
        {
            // Persisted only on Apply, layout-signed so older builds read and resave the file.
            project.SetStereoScene(
                result.Request.SceneOffsetMs, result.Request.RightHandDrive);
            project.StereoLevelDifferenceDb = result.Request.LevelDifferenceDb;
        }

        // "Keep the hidden side's polarity" is invisible to the lock as a difference, so re-remember the result.
        sideLock.Remember(channels.Select(channel => channel.Pair));
        ScheduleSave();
        RedrawAll();
        WriteAlignmentLog(result.Log.ToString());
    }

    private async Task AppendOutcomeMetricAsync(AutoDelayRunResult result)
    {
        // RedrawAll pushes the read-out asynchronously, so recompute here; capture the side before the await.
        bool metricSideRight = project.ActiveSideRight;
        ProcessedRender? render = await ProcessChannelsAsync();
        List<ProcessedChannel> outcomeChannels = render?.Channels ?? [];
        (_, _, List<SignalPoint>? outcomeLoss) =
            metrics.BuildCurves(outcomeChannels, magnitudeGate.SmoothingInverseOctaves);
        result.Log.AppendLine(
            $"Metric ({(metricSideRight ? "R" : "L")} side):");
        result.Log.AppendLine(VirtualCrossoverMetric.FormatDetail(
            metrics.BuildEntries(outcomeChannels, outcomeLoss)));
        WriteAlignmentLog(result.Log.ToString());
    }

    // Records may carry a playback-crosstalk click at a fixed early sample (biases GCC-PHAT, wrong branch on gentle
    // slopes): head-gate convicted records and log them.
    private static List<AlignmentReprocessInput> CleanCrosstalkHeads(
        List<AlignmentReprocessInput> inputs,
        System.Text.StringBuilder log) =>
        inputs.Select(input =>
        {
            double[] real = Array.ConvertAll(
                input.MeasuredImpulseResponse, sample => sample.Real);
            CrosstalkHeadGate? gate = TransferIrDiagnostics.DetectCrosstalkHead(
                real, input.SampleRate);
            if (gate is not { } convicted)
            {
                return input;
            }

            log.AppendLine(
                $"{input.Channel.Name}: playback-crosstalk click at " +
                $"{convicted.BurstTimeMs:0.00} ms ({convicted.BurstPeakDbReMax:0.0} dB " +
                "re max) removed from the record's head before the search");
            return input with
            {
                MeasuredImpulseResponse = TransferIrDiagnostics.CleanCrosstalkHead(
                    input.MeasuredImpulseResponse, input.SampleRate, convicted)
            };
        }).ToList();

    // Bridges to AutoAlignmentEngine on a background thread; the reprocessor's run-scoped FFT cache re-FFTs only changed
    // channels and is returned for the gain stage.
    /// <param name="walkSet">Channels forming junctions when narrower than the participants (later stages still render from all); null walks all.</param>
    private AlignmentReprocessor ComputeAutoAlignment(
        List<VirtualCrossoverChannel> participants,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        System.Text.StringBuilder log,
        IReadOnlyList<VirtualCrossoverChannel>? walkSet = null)
    {
        // Adjacent drivers by band centre form the junctions (same as VirtualCrossoverJunctions).
        List<VirtualCrossoverChannel> ordered = participants
            .OrderBy(channel => VirtualCrossoverJunctions.BandCenterHz(channel.Settings))
            .ToList();

        // Shared direct-sound crop: identical delays at a fraction of the FFT cost.
        var reprocessor = new AlignmentReprocessor(
            CleanCrosstalkHeads(
                ordered.Select(channel => new AlignmentReprocessInput(
                    channel,
                    channel.TransferImpulseResponse!,
                    channel.SampleRate,
                    ProcessorSampleRateHz,
                    channel.Settings.ToChain(channel.Pair.Zone))).ToList(),
                log));

        IReadOnlyList<AlignmentSnapshot> initial = reprocessor.Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        var snapshots = ordered
            .Select((channel, i) => (channel, snapshot: initial[i]))
            .ToDictionary(item => item.channel, item => item.snapshot);
        // A narrowed walk keeps the whole set's band order.
        List<VirtualCrossoverChannel> walked = walkSet == null
            ? ordered
            : [.. ordered.Where(walkSet.Contains)];
        var junctions = new List<AlignmentJunction>();
        for (int i = 0; i < walked.Count - 1; i++)
        {
            double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                walked[i].Settings, walked[i + 1].Settings);
            (double bandLowHz, double bandHighHz) =
                VirtualCrossoverJunctions.OverlapBand(pairHz);
            junctions.Add(new AlignmentJunction(
                snapshots[walked[i]], snapshots[walked[i + 1]],
                pairHz, bandLowHz, bandHighHz));
        }

        AutoAlignmentEngine.Compute(
            walked.Select(channel => snapshots[channel]).ToList(),
            junctions,
            reprocessor.Reprocess,
            alignment,
            log,
            decisions,
            maxDelayMs: ProcessorMaxDelayMs);
        return reprocessor;
    }

    // A mono pair contributes ONE instance (left), tuned in the left pass and fixed on the right.
    internal static (List<VirtualCrossoverSideAlignmentChannel> Left, List<VirtualCrossoverSideAlignmentChannel> Right)
        CollectStereoSides(IEnumerable<VirtualCrossoverChannel> channels)
    {
        var left = new List<VirtualCrossoverSideAlignmentChannel>();
        var right = new List<VirtualCrossoverSideAlignmentChannel>();
        foreach (VirtualCrossoverChannel channel in channels)
        {
            if (channel.Pair.Enabled &&
                channel.SideState(false).TransferImpulseResponse != null)
            {
                var side = new VirtualCrossoverSideAlignmentChannel(channel, false);
                left.Add(side);
                if (channel.Pair.Mono)
                {
                    right.Add(side);
                }
            }

            if (!channel.Pair.Mono &&
                channel.Pair.Enabled &&
                channel.SideState(true).TransferImpulseResponse != null)
            {
                right.Add(new VirtualCrossoverSideAlignmentChannel(channel, true));
            }
        }

        return (left, right);
    }

    // Driver's side first, then the L/R bridge at the top pair, then the far side (AutoAlignmentEngine.ComputeStereo).
    private (AutoDelayLaunch? Launch, string? Refusal) PrepareStereoAutoDelay(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        VirtualCrossoverSideAlignmentChannel bridgeRight,
        bool interactive)
    {
        List<VirtualCrossoverSideAlignmentChannel> union = leftSide.Concat(rightSide)
            .Distinct()
            .ToList();

        // Bypass belongs to the block, so both sides are refused.
        List<VirtualCrossoverSideAlignmentChannel> bypassed = union
            .Where(item => item.Runtime.Pair.Bypass)
            .ToList();
        if (bypassed.Count > 0)
        {
            if (interactive)
            {
                ShowError(
                    "Auto delay cannot run with bypassed channels.",
                    "Bypass feeds the raw measured signal, so the computed delays " +
                    "and polarities would not apply to: " +
                    string.Join(", ", bypassed.Select(item => item.Name)) +
                    ".\r\n\r\nDisable Bypass on every participating channel " +
                    "(or mute the channel to exclude it) and run Auto delay again.");
            }

            return (null, "a participating channel is bypassed: " +
                string.Join(", ", bypassed.Select(item => item.Name)));
        }

        // The verdict covers only the side on screen.
        if (interactive ? RefuseOnMisplacedGate("Auto delay") : GateIsMisplaced)
        {
            return (null, "the phase gate is misplaced");
        }

        bool anyCrossover = union.Any(
            item => item.Settings.EffectiveCrossover.Kind != CrossoverKind.Off);
        if (!anyCrossover && !interactive)
        {
            return (null, "no channel has a crossover configured; set the crossovers first");
        }
        if (!anyCrossover)
        {
            DialogResult answer = MessageBox.Show(
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
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return (null, "cancelled");
            }
        }

        VirtualCrossoverSideAlignmentChannel bridgeLeft = leftSide.First(
            item => item.Runtime == bridgeRight.Runtime && !item.RightSide);
        if (StereoBridgeBand(bridgeLeft, bridgeRight) is not
            (double bridgeBandLowHz, double bridgeBandHighHz))
        {
            if (interactive)
            {
                (double leftBandLowHz, double leftBandHighHz) =
                    VirtualCrossoverJunctions.GetChannelBand(bridgeLeft.Settings);
                (double rightBandLowHz, double rightBandHighHz) =
                    VirtualCrossoverJunctions.GetChannelBand(bridgeRight.Settings);
                ShowError(
                    "The stereo bridge has no usable shared band.",
                    $"The top pair's crossover bands barely overlap: " +
                    $"{bridgeLeft.Name} plays {leftBandLowHz:0}-{leftBandHighHz:0} Hz, " +
                    $"{bridgeRight.Name} plays {rightBandLowHz:0}-{rightBandHighHz:0} Hz. " +
                    "Align the pair's crossover settings so the sides share at " +
                    "least a third of an octave and run Auto delay again.");
            }

            return (null, "the stereo bridge has no usable shared band");
        }

        return (
            new AutoDelayLaunch(
                Stereo: true,
                request => RunStereoProposalAsync(
                    leftSide, rightSide, union, bridgeLeft, bridgeRight,
                    bridgeBandLowHz, bridgeBandHighHz, request),
                DescribeLeftRightPolarityMismatch(leftSide, rightSide),
                union.Any(side => side.Runtime.Pair.Zone == VirtualCrossoverZone.Rear)),
            null);
    }

    // From the raw transfer IRs (the "IR:" badge), not the Invert switch: alignment can mask a swapped wire.
    private static string? DescribeLeftRightPolarityMismatch(
        IEnumerable<VirtualCrossoverSideAlignmentChannel> leftSide,
        IReadOnlyCollection<VirtualCrossoverSideAlignmentChannel> rightSide)
    {
        var names = new List<string>();
        foreach (VirtualCrossoverSideAlignmentChannel right in
            rightSide.Where(side => side.RightSide))
        {
            VirtualCrossoverSideAlignmentChannel? left = leftSide.FirstOrDefault(
                side => side.Runtime == right.Runtime && !side.RightSide);
            if (left == null ||
                left.State.TransferImpulseResponse is not { } leftIr ||
                right.State.TransferImpulseResponse is not { } rightIr)
            {
                continue;
            }

            PolarityEstimate leftPolarity = VirtualCrossoverAnalysis.EstimatePolarity(leftIr);
            PolarityEstimate rightPolarity = VirtualCrossoverAnalysis.EstimatePolarity(rightIr);
            if (leftPolarity != PolarityEstimate.Unknown &&
                rightPolarity != PolarityEstimate.Unknown &&
                leftPolarity != rightPolarity)
            {
                names.Add(right.Runtime.Name);
            }
        }

        return FormatPolarityMismatchWarning(names);
    }

    internal static string? FormatPolarityMismatchWarning(
        IReadOnlyList<string> mismatchedDrivers) =>
        mismatchedDrivers.Count == 0
            ? null
            : $"⚠ L/R polarity mismatch on {string.Join(", ", mismatchedDrivers)} — " +
              "one side measured inverted (check wiring).";

    // Right channels' gains are judged against their left peers, tilted by the entered L-R level difference.
    private async Task<AutoDelayRunResult> RunStereoProposalAsync(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        List<VirtualCrossoverSideAlignmentChannel> union,
        VirtualCrossoverSideAlignmentChannel bridgeLeft,
        VirtualCrossoverSideAlignmentChannel bridgeRight,
        double bridgeBandLowHz,
        double bridgeBandHighHz,
        AutoDelayRunRequest request)
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine($"Auto delay (stereo) {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        log.AppendLine(
            $"Layout {(request.RightHandDrive ? "RHD" : "LHD")}: scene offset " +
            $"{request.SceneOffsetMs:0.00} ms, the " +
            $"{(request.RightHandDrive ? "left" : "right")} side leads; " +
            $"bridge {(request.RightHandDrive ? bridgeRight : bridgeLeft).Name} -> " +
            $"{(request.RightHandDrive ? bridgeLeft : bridgeRight).Name} " +
            $"in {bridgeBandLowHz:0}-{bridgeBandHighHz:0} Hz");
        if (request.RightHandDrive)
        {
            log.AppendLine(
                "RHD run: the engine trace below reads in mirrored " +
                "coordinates (ref = the right side, far = the left).");
        }
        log.AppendLine("Previous delay / polarity settings ignored for this run.");

        var engineAlignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var decisions = new Dictionary<IAlignmentChannel, AlignmentDecision>();
        IReadOnlyList<GainBalanceResult>? gains = null;
        AutoDelaySumLossForecast? leftSumLoss = null;
        AutoDelaySumLossForecast? rightSumLoss = null;
        await Task.Run(() =>
        {
            // The reprocessor covers the union (later stages render from it); only the chain is walked.
            List<VirtualCrossoverSideAlignmentChannel> chainLeft =
                [.. leftSide.Where(InFrontChain)];
            List<VirtualCrossoverSideAlignmentChannel> chainRight =
                [.. rightSide.Where(InFrontChain)];
            List<VirtualCrossoverSideAlignmentChannel> later =
                [.. union.Where(side => !InFrontChain(side))];
            AlignmentReprocessor reprocessor = ComputeStereoAlignment(
                chainLeft, chainRight, union, bridgeLeft, bridgeRight,
                bridgeBandLowHz, bridgeBandHighHz, request.SceneOffsetMs,
                request.RightHandDrive, ProcessorSampleRateHz, ProcessorMaxDelayMs,
                engineAlignment, decisions, log);
            if (later.Count > 0)
            {
                // Engine roles, so RHD places groups against the reference the walk settled.
                IReadOnlyCollection<IAlignmentChannel> fillCarriers =
                    PlaceLaterStagesStereo(
                        request.RightHandDrive ? chainRight : chainLeft,
                        request.RightHandDrive ? chainLeft : chainRight,
                        later,
                        reprocessor,
                        engineAlignment,
                        decisions,
                        request.SceneOffsetMs,
                        request.RearFillOffsetMs,
                        request.RightHandDrive,
                        log);
                NormalizeStagedDelays(
                    [.. union.Cast<IAlignmentChannel>()], engineAlignment, log,
                    ProcessorMaxDelayMs, request.RearFillOffsetMs, fillCarriers);
            }

            // "Before" snapshots exist only for the report's before/after forecast.
            IReadOnlyList<AlignmentSnapshot> beforeSnapshots =
                reprocessor.Reprocess(union.ToDictionary(
                    side => (IAlignmentChannel)side,
                    side => new AlignmentOverride(
                        side.Settings.DelayMs, side.Settings.InvertPolarity)));
            if (request.AdjustGains)
            {
                gains = ComputeGainBalance(
                    union.Select(side => (
                        (IAlignmentChannel)side,
                        side.Settings,
                        side.Runtime.Pair.Mono,
                        side.RightSide,
                        (IAlignmentChannel?)(side.RightSide
                            ? leftSide.FirstOrDefault(left =>
                                left.Runtime == side.Runtime && !left.RightSide)
                            : null))),
                    reprocessor, engineAlignment, request.LevelDifferenceDb, log);
            }

            IReadOnlyList<AlignmentSnapshot> afterSnapshots =
                reprocessor.Reprocess(engineAlignment);
            Dictionary<IAlignmentChannel, Complex[]> beforeIrs = ToIrMap(beforeSnapshots);
            Dictionary<IAlignmentChannel, Complex[]> afterIrs = ToIrMap(afterSnapshots);
            Dictionary<IAlignmentChannel, GainBalanceResult>? adjustedGains =
                AdjustedGainMap(gains);
            (double leftMinHz, double leftMaxHz) =
                VirtualCrossoverJunctions.GetCrossoverWindow(
                    leftSide.Select(side => side.Settings));
            leftSumLoss = ForecastSumLoss(
                leftSide.Select(side => ((IAlignmentChannel)side, side.Settings)).ToList(),
                beforeIrs, afterIrs, adjustedGains, leftMinHz, leftMaxHz);
            (double rightMinHz, double rightMaxHz) =
                VirtualCrossoverJunctions.GetCrossoverWindow(
                    rightSide.Select(side => side.Settings));
            rightSumLoss = ForecastSumLoss(
                rightSide.Select(side => ((IAlignmentChannel)side, side.Settings)).ToList(),
                beforeIrs, afterIrs, adjustedGains, rightMinHz, rightMaxHz);
        });

        // Report grouped per block (A L, A R, B L...).
        List<AutoDelayChannelOutcome> outcomes = BuildOutcomes(
            union
                .OrderBy(side => channels.IndexOf(side.Runtime))
                .ThenBy(side => side.RightSide)
                .Select(side => (
                    (IAlignmentChannel)side,
                    side.Runtime,
                    side.Settings,
                    side.Name)),
            engineAlignment, decisions, gains);
        string report = VirtualCrossoverAutoDelayReport.Format(
            outcomes, stereo: true, request, leftSumLoss, rightSumLoss);
        WriteAlignmentLog(log.ToString());
        return new AutoDelayRunResult(outcomes, Stereo: true, request, report, log);
    }

    internal static AlignmentReprocessor ComputeStereoAlignment(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        List<VirtualCrossoverSideAlignmentChannel> union,
        VirtualCrossoverSideAlignmentChannel bridgeLeft,
        VirtualCrossoverSideAlignmentChannel bridgeRight,
        double bridgeBandLowHz,
        double bridgeBandHighHz,
        double sceneOffsetMs,
        bool rightHandDrive,
        int processorSampleRateHz,
        double maxDelayMs,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        System.Text.StringBuilder log)
    {
        // Crop to the direct sound: final delays identical to a full-length run (validated), FFTs much shorter.
        var reprocessor = new AlignmentReprocessor(
            CleanCrosstalkHeads(
                union.Select(side => new AlignmentReprocessInput(
                    side,
                    side.State.TransferImpulseResponse!,
                    side.State.SampleRate,
                    processorSampleRateHz,
                    side.Settings.ToChain(side.Runtime.Pair.Zone))).ToList(),
                log));

        IReadOnlyList<AlignmentSnapshot> initialSnapshots = reprocessor.Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        Dictionary<VirtualCrossoverSideAlignmentChannel, AlignmentSnapshot> initial = union
            .Select((side, i) => (side, snapshot: initialSnapshots[i]))
            .ToDictionary(item => item.side, item => item.snapshot);
        List<AlignmentSnapshot> ByBand(List<VirtualCrossoverSideAlignmentChannel> sides) => sides
            .OrderBy(side => VirtualCrossoverJunctions.BandCenterHz(side.Settings))
            .Select(side => initial[side])
            .ToList();
        List<AlignmentJunction> Pairs(List<AlignmentSnapshot> byBand)
        {
            var pairs = new List<AlignmentJunction>();
            for (int i = 0; i < byBand.Count - 1; i++)
            {
                double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                    ((VirtualCrossoverSideAlignmentChannel)byBand[i].Channel).Settings,
                    ((VirtualCrossoverSideAlignmentChannel)byBand[i + 1].Channel).Settings);
                (double bandLowHz, double bandHighHz) =
                    VirtualCrossoverJunctions.OverlapBand(pairHz);
                pairs.Add(new AlignmentJunction(
                    byBand[i], byBand[i + 1], pairHz, bandLowHz, bandHighHz));
            }

            return pairs;
        }

        // The plan is in reference/far ROLES; RHD hands it mirrored, so a positive offset makes the left side lead.
        // Pair links aim the descent's prior at the cross-side-consistent delay.
        var pairLinks = new List<StereoPairLink>();
        foreach (VirtualCrossoverSideAlignmentChannel right in rightSide.Where(side => side.RightSide))
        {
            VirtualCrossoverSideAlignmentChannel? left = leftSide.FirstOrDefault(
                side => side.Runtime == right.Runtime && !side.RightSide);
            if (left == null)
            {
                continue;
            }

            (double leftLow, double leftHigh) =
                VirtualCrossoverJunctions.GetChannelBand(left.Settings);
            (double rightLow, double rightHigh) =
                VirtualCrossoverJunctions.GetChannelBand(right.Settings);
            double lowHz = Math.Max(leftLow, rightLow);
            double highHz = Math.Min(leftHigh, rightHigh);
            // Must satisfy the arrival analysis' admission rule, or the link could never measure.
            if (highHz >= lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
            {
                pairLinks.Add(rightHandDrive
                    ? new StereoPairLink(right, left, lowHz, highHz)
                    : new StereoPairLink(left, right, lowHz, highHz));
            }
        }

        List<AlignmentSnapshot> referenceByBand =
            ByBand(rightHandDrive ? rightSide : leftSide);
        List<AlignmentSnapshot> farByBand =
            ByBand(rightHandDrive ? leftSide : rightSide);
        AutoAlignmentEngine.ComputeStereo(
            new StereoAlignmentPlan(
                referenceByBand,
                Pairs(referenceByBand),
                farByBand,
                Pairs(farByBand),
                // Monos from the walked left side, NOT the union: a staged union keeps a mono centre/rear that trips the engine's
                // "mono must be in the left walk" guard.
                leftSide.Where(side => side.Runtime.Pair.Mono)
                    .Cast<IAlignmentChannel>()
                    .ToList(),
                rightHandDrive ? bridgeRight : bridgeLeft,
                rightHandDrive ? bridgeLeft : bridgeRight,
                bridgeBandLowHz,
                bridgeBandHighHz,
                sceneOffsetMs,
                pairLinks),
            reprocessor.Reprocess,
            alignment,
            log,
            decisions,
            maxDelayMs);
        return reprocessor;
    }

    // Best effort: a failed write must never break the alignment.
    private static void WriteAlignmentLog(string text)
    {
        try
        {
            AtomicFile.WriteAllText(
                ApplicationDataPaths.Current.VirtualDspAlignmentLogFile, text);
        }
        catch
        {
        }
    }

    // Always the FIXED gate. Pinned: one absolute window; Auto: anchored at the caller's sample (shared earliest front
    // keeps Sum = vector sum of channels, loss <= 0 dB). PLINQ workers read only the immutable snapshot.
    private GatedMagnitude BuildMagnitudeCurve(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        MeasuredBand band,
        CalibrationFile? calibration)
    {
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        return BuildGatedMagnitudeCurve(
            snapshot,
            impulseResponse,
            peakIndex,
            sampleRate,
            snapshot.ResolveGateOffsetMs(oppositeSide: false, peakIndex, sampleRate),
            band,
            calibration);
    }



    // Its own pin or anchor, never the active side's.
    private AnalysisCurve BuildOppositeMagnitudeCurve(VirtualCrossoverSideSum side)
    {
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        return BuildMeasuredSumCurve(
            snapshot,
            side.Channels,
            side.AnchorIndex,
            snapshot.ResolveGateOffsetMs(
                oppositeSide: true, side.AnchorIndex, side.SampleRate)).Display;
    }

    /// <summary>Opposite side's hybrid sum from its own captures and loss, but with the ACTIVE side's offset.</summary>
    /// <remarks>See docs/tech/virtual-dsp-panel.md#opposite-side-hybrid-sum.</remarks>
    private AnalysisCurve? BuildOppositeHybridSumCurve(
        VirtualCrossoverSideSum side, double offsetDb, MagnitudeGateSnapshot? snapshot = null)
    {
        bool oppositeRight = !project.ActiveSideRight;
        if (!CanDrawOppositeHybridSum(oppositeRight))
        {
            return null;
        }

        // One anchor and offset for channels AND sum, as on the active side.
        snapshot ??= magnitudeGate;
        double gateOffsetMs = snapshot.ResolveGateOffsetMs(
            oppositeSide: true, side.AnchorIndex, side.SampleRate);
        GatedMagnitude sum = BuildMeasuredSumCurve(
            snapshot, side.Channels, side.AnchorIndex, gateOffsetMs);
        var channelMagnitudes = new List<GatedMagnitude>(side.Channels.Count);
        foreach (ProcessedChannel item in side.Channels)
        {
            channelMagnitudes.Add(BuildGatedMagnitudeCurve(
                snapshot,
                item.ImpulseResponse,
                side.AnchorIndex,
                item.SampleRate,
                gateOffsetMs,
                item.MeasuredBand,
                CalibrationFor(item)));
        }

        HybridMagnitudes? hybrid = BuildHybridMagnitudes(
            side.Channels,
            channelMagnitudes.Select(curve => curve.Display).ToList(),
            oppositeRight,
            snapshot.SmoothingInverseOctaves);
        if (hybrid == null)
        {
            return null;
        }

        List<IReadOnlyList<SignalPoint>> operands = channelMagnitudes
            .Select(curve => (IReadOnlyList<SignalPoint>)curve.Unsmoothed.Points)
            .ToList();
        // Raw, smoothed only at the end (see BuildHybridSumCurve).
        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(
            sum.Unsmoothed.Points, operands);
        // Its own offset is replaced: using it would level the sides separately.
        List<SignalPoint>? points = BuildHybridSumCurve(
            hybrid with { OffsetDb = offsetDb },
            side.Channels,
            side.AnchorIndex,
            snapshot,
            gateOffsetMs,
            channelMagnitudes.Select(curve => (IReadOnlyList<SignalPoint>)curve.Display.Points)
                .ToList());
        return points == null ? null : new AnalysisCurve("Sum opposite", points);
    }

    // Anchor and gate recomputed as pure functions of the processed set and snapshot, so they match the measured Sum.
    private List<SignalPoint>? BuildActiveHybridSumCurve(
        List<ProcessedChannel> processed,
        List<AnalysisCurve> magnitudes,
        HybridMagnitudes hybrid,
        MagnitudeGateSnapshot? snapshot = null)
    {
        if (processed.Count == 0)
        {
            return null;
        }

        snapshot ??= magnitudeGate;
        int anchorIndex = ProcessedChannels.SharedStartAnchorIndex(processed);
        return BuildHybridSumCurve(
            hybrid,
            processed,
            anchorIndex,
            snapshot,
            snapshot.ResolveGateOffsetMs(
                oppositeSide: false, anchorIndex, processed[0].SampleRate),
            magnitudes.Select(curve => (IReadOnlyList<SignalPoint>)curve.Points).ToList());
    }

    // Raw curves anchor on their own START; see docs/tech/virtual-dsp-panel.md#raw-curve-anchor.
    private AnalysisCurve BuildRawMagnitudeCurve(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        MeasuredBand band,
        CalibrationFile? calibration,
        MagnitudeGateSnapshot? snapshot = null)
    {
        int anchorIndex = ProcessedChannels.StartAnchorIndex(
            impulseResponse, peakIndex, sampleRate);
        return BuildGatedMagnitudeCurve(
            snapshot ?? magnitudeGate,
            impulseResponse,
            anchorIndex,
            sampleRate,
            anchorIndex * 1_000.0 / sampleRate,
            band,
            calibration).Display;
    }

    /// <summary>Gated magnitude of the channels' SUM, each contributing only where it measured.
    /// See docs/tech/virtual-dsp-panel.md#measured-sum.</summary>
    // Resolves the active side's placement so the drawn Sum and the metric share one window.
    private GatedMagnitude BuildMeasuredSumCurve(
        IReadOnlyList<ProcessedChannel> channels,
        int anchorIndex)
    {
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        return BuildMeasuredSumCurve(
            snapshot,
            channels,
            anchorIndex,
            snapshot.ResolveGateOffsetMs(
                oppositeSide: false,
                anchorIndex,
                channels.Count > 0 ? channels[0].SampleRate : 0));
    }

    /// <remarks>Each channel's own correction goes INSIDE the sum (Σ HᵢCᵢ): one outside cannot undo two microphones.</remarks>
    private GatedMagnitude BuildMeasuredSumCurve(
        MagnitudeGateSnapshot snapshot,
        IReadOnlyList<ProcessedChannel> channels,
        int anchorIndex,
        double gateOffsetMs)
    {
        PhaseAnalysisSettings gate = snapshot.Template with
        {
            GateOffsetMs = gateOffsetMs
        };
        var views = new List<IImpulseMeasurement>(channels.Count);
        var calibrations = new List<CalibrationFile?>(channels.Count);
        foreach (ProcessedChannel channel in channels)
        {
            views.Add(new ImpulseMeasurementView(
                channel.ImpulseResponse, anchorIndex, channel.SampleRate)
            {
                LowestMeasuredFrequencyHz = channel.MeasuredBand.LowEdgeHz,
                HighestMeasuredFrequencyHz = channel.MeasuredBand.HighEdgeHz
            });
            calibrations.Add(CalibrationFor(channel));
        }

        (AnalysisCurve display, AnalysisCurve unsmoothed) =
            DataHelper.GetGatedMeasuredMagnitudeSumPair(
                views, gate, calibrations, snapshot.SmoothingInverseOctaves);
        return new GatedMagnitude(display, unsmoothed).MeasuredBySomeChannel(channels);
    }

    private GatedMagnitude BuildGatedMagnitudeCurve(
        MagnitudeGateSnapshot snapshot,
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        double gateOffsetMs,
        MeasuredBand band,
        CalibrationFile? calibration)
    {
        PhaseAnalysisSettings gate = snapshot.Template with
        {
            GateOffsetMs = gateOffsetMs
        };
        (AnalysisCurve display, AnalysisCurve unsmoothed) =
            DataHelper.GetGatedPrimarySpectrumPair(
                new ImpulseMeasurementView(impulseResponse, peakIndex, sampleRate)
                {
                    LowestMeasuredFrequencyHz = band.LowEdgeHz,
                    HighestMeasuredFrequencyHz = band.HighEdgeHz
                },
                gate,
                calibration,
                snapshot.SmoothingInverseOctaves);
        return new GatedMagnitude(display, unsmoothed);
    }

    private List<AcousticCurve> BuildPhaseCurves(
        List<ProcessedChannel> processed,
        IReadOnlyList<ProcessedChannel>? summed = null)
    {
        summed ??= processed;
        using var _ = AppProfiler.Zone("VirtualDSP.BuildPhaseCurves");
        // One shared absolute τ keeps relative phase; windows may follow each channel's arrival because BuildMeasuredPhase
        // re-references to τ (exact while no window cuts its own channel, enforced by ResolvePhaseGateOffsets).
        int sampleRate = processed[0].SampleRate;
        double referenceOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(processed, sampleRate);
        double detrendMs = ResolveCommonDetrendMs(
            processed, referenceOffsetMs, sampleRate);

        // Spectra built ONCE per redraw for curves and Sum (the cache does not serialize bank computation).
        // The Sum uses every SUMMING channel, hidden or not, matching the magnitude Sum.
        bool includeSum = summed.Count >= 2 && checkBoxShowSum.Checked;
        List<ProcessedChannel> gatedChannels = processed
            .Where(item => (includeSum && summed.Contains(item)) ||
                item.Channel.Pair.ShowProcessedCurve)
            .ToList();

        // Read gate and project state once on the UI thread; placements over the gated set only.
        List<double> offsets = ResolvePhaseGateOffsets(
            gatedChannels, referenceOffsetMs, sampleRate);
        double referenceSamples = detrendMs * sampleRate / 1_000.0;

        List<(ProcessedChannel Item, Complex[] Spectrum, int ExtractionStart)> gated =
            gatedChannels
                .Select((item, index) => (item, Settings: CreateVirtualPhaseSettings(
                    offsets[index], PhaseDetrendMode.Manual, detrendMs)))
                .AsParallel()
                .AsOrdered()
                .Select(input =>
                {
                    Complex[] spectrum = DataHelper.GetPhaseAnalysisSpectrum(
                        new ImpulseMeasurementView(
                            input.item.ImpulseResponse, 0, sampleRate),
                        input.Settings,
                        out int extractionStart);
                    return (input.item, spectrum, extractionStart);
                })
                .ToList();

        var jobs = new List<(string Title, OxyColor Color, double Thickness,
            Complex[] Spectrum, int ExtractionStart)>();
        foreach ((ProcessedChannel item, Complex[] spectrum, int extractionStart)
            in gated)
        {
            if (item.Channel.Pair.ShowProcessedCurve)
            {
                jobs.Add((
                    item.Channel.Name, item.Color, 1.8, spectrum, extractionStart));
            }
        }

        if (includeSum)
        {
            // Vector sum of individually gated SPECTRA, not a gate over the summed IR (FDW HF windows < arrival spread).
            List<(ProcessedChannel Item, Complex[] Spectrum, int ExtractionStart)>
                summedParts = [.. gated.Where(part => summed.Contains(part.Item))];
            if (summedParts.Count >= 2)
            {
                int targetExtractionStart =
                    summedParts.Min(part => part.ExtractionStart);
                Complex[] combined = DataHelper.SumGatedSpectra(
                    [.. summedParts.Select(part => (part.Spectrum, part.ExtractionStart))],
                    targetExtractionStart);
                jobs.Add(("Sum", SumColor, 2.4, combined, targetExtractionStart));
            }
        }

        return jobs
            .AsParallel()
            .AsOrdered()
            .SelectMany(job =>
            {
                GatedPhaseCurve curve = GatedPhaseCurves.Read(
                    job.Spectrum,
                    job.ExtractionStart,
                    referenceSamples,
                    sampleRate,
                    job.Title,
                    job.Color,
                    job.Thickness);
                var curves = new List<AcousticCurve>(2);
                // Wrap verticals: faded, dashed, drawn under the curve; empty title keeps them out of plot labels.
                if (curve.WrapSegments.Count > 0)
                {
                    curves.Add(new AcousticCurve(
                        string.Empty,
                        curve.WrapSegments,
                        OxyColor.FromAColor(110, job.Color),
                        job.Thickness * 0.4,
                        LineStyle.Dash));
                }
                curves.Add(new AcousticCurve(
                    curve.Title,
                    curve.Points,
                    job.Color,
                    job.Thickness,
                    LineStyle.Solid));
                return curves;
            })
            .ToList();
    }

    // Same window and placement as the phase view; absolute ms, no detrend. Plain GD and the Sum only.
    // See docs/tech/virtual-dsp-panel.md#group-delay-view.
    private List<AcousticCurve> BuildGroupDelayCurves(
        List<ProcessedChannel> processed,
        IReadOnlyList<ProcessedChannel>? summed = null)
    {
        summed ??= processed;
        using var _ = AppProfiler.Zone("VirtualDSP.BuildGroupDelayCurves");
        int sampleRate = processed[0].SampleRate;
        double referenceOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(processed, sampleRate);

        bool includeSum = summed.Count >= 2 && checkBoxShowSum.Checked;
        List<ProcessedChannel> gatedChannels = processed
            .Where(item => (includeSum && summed.Contains(item)) ||
                item.Channel.Pair.ShowProcessedCurve)
            .ToList();
        if (gatedChannels.Count == 0)
        {
            return [];
        }

        // Psychoacoustic smoothing is a level model: it reads as 1/12 octave here (what the AI diagnostic reads).
        List<double> offsets = ResolvePhaseGateOffsets(
            gatedChannels, referenceOffsetMs, sampleRate);
        // The code, not the stored width: the project stores psychoacoustic as base width plus a flag.
        double smoothingInverseOctaves =
            SpectrumSmoothing.IsPsychoacoustic(project.SmoothingCode)
                ? FrequencyResponseOptions.DefaultGroupDelaySmoothingInverseOctaves
                : project.SmoothingCode;
        List<(ProcessedChannel Item, PhaseAnalysisSettings Settings)> inputs = gatedChannels
            .Select((item, index) => (item, CreateVirtualPhaseSettings(
                offsets[index], PhaseDetrendMode.Off, manualDetrendMilliseconds: 0.0)))
            .ToList();

        List<(ProcessedChannel Item, PhaseAnalysisSettings Settings,
            GroupDelaySpectra Spectra, int ExtractionStart)> gated = inputs
            .AsParallel()
            .AsOrdered()
            .Select(input =>
            {
                GroupDelaySpectra spectra = DataHelper.GetGroupDelayAnalysisSpectra(
                    new ImpulseMeasurementView(input.Item.ImpulseResponse, 0, sampleRate),
                    input.Settings,
                    out int extractionStart);
                return (input.Item, input.Settings, spectra, extractionStart);
            })
            .ToList();

        var jobs = new List<(string Title, OxyColor Color, double Thickness,
            GroupDelaySpectra Spectra, int ExtractionStart, PhaseAnalysisSettings Settings,
            MeasuredBand Band, IReadOnlyList<ProcessedChannel>? MaskBy)>();
        foreach ((ProcessedChannel item, PhaseAnalysisSettings settings,
            GroupDelaySpectra spectra, int extractionStart) in gated)
        {
            if (item.Channel.Pair.ShowProcessedCurve)
            {
                jobs.Add((item.Channel.Name, item.Color, 1.8, spectra, extractionStart,
                    settings, item.MeasuredBand, null));
            }
        }

        if (includeSum)
        {
            // Sum of individually gated operand pairs re-referenced to one extraction start (as in the phase view).
            List<(ProcessedChannel Item, PhaseAnalysisSettings Settings,
                GroupDelaySpectra Spectra, int ExtractionStart)> summedParts =
                [.. gated.Where(part => summed.Contains(part.Item))];
            if (summedParts.Count >= 2)
            {
                int targetExtractionStart =
                    summedParts.Min(part => part.ExtractionStart);
                GroupDelaySpectra combined = DataHelper.SumGatedSpectraPairs(
                    [.. summedParts.Select(part => (part.Spectra, part.ExtractionStart))],
                    targetExtractionStart,
                    sampleRate);
                // Masked where no channel measured, like the magnitude Sum.
                jobs.Add(("Sum", SumColor, 2.4, combined, targetExtractionStart,
                    summedParts[0].Settings, MeasuredBand.Everything,
                    [.. summedParts.Select(part => part.Item)]));
            }
        }

        // The Group Delay mode's validity gate blanks a crossover's stop band on purpose.
        return jobs
            .AsParallel()
            .AsOrdered()
            .Select(job =>
            {
                GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
                    job.Spectra,
                    job.ExtractionStart,
                    sampleRate,
                    job.Settings,
                    smoothingInverseOctaves,
                    PlotModelFactory.GroupDelayMagnitudeGateDb,
                    includeMinimumPhase: false,
                    job.Band.LowEdgeHz,
                    job.Band.HighEdgeHz);
                IReadOnlyList<SignalPoint> points = job.MaskBy == null
                    ? curves.Measured.Points
                    : ProcessedChannels.MeasuredBySomeChannel(curves.Measured.Points, job.MaskBy);
                return new AcousticCurve(
                    job.Title, points, job.Color, job.Thickness, LineStyle.Solid);
            })
            .ToList();
    }

    // The gate dialog's IR preview promoted to the main plot; each trace normalized to its envelope's in-window peak.
    private AcousticImpulseRender? BuildImpulseRender(List<ProcessedChannel> processed)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildImpulseRender");
        // Only shown traces set the gate offset and axis window.
        List<ProcessedChannel> shown = processed
            .Where(item => item.Channel.Pair.ShowProcessedCurve)
            .ToList();
        if (shown.Count == 0)
        {
            return null;
        }

        int sampleRate = shown[0].SampleRate;
        double gateOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(shown, sampleRate);

        var traces = shown
            .Select(item => new IrPreviewTrace(
                item.ImpulseResponse,
                item.Channel.Name,
                item.Color))
            .ToList();

        return new AcousticImpulseRender(
            traces,
            sampleRate,
            gateOffsetMs,
            gatePreview?.LeftMs ?? project.PhaseGateLeftMs,
            gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs,
            gatePreview?.RightMs ?? project.PhaseGateRightMs);
    }

    // Sum = sample-wise sum of the SUMMING channels' IRs (hidden too), so its step is the sum of steps.
    // One common scale; the opposite Sum needs the shown side's rate. See docs/tech/virtual-dsp-panel.md#step-view.
    private AcousticImpulseRender? BuildStepRender(
        List<ProcessedChannel> processed,
        IReadOnlyList<ProcessedChannel> summed,
        VirtualCrossoverSideSum? oppositeSide)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildStepRender");
        List<ProcessedChannel> shown = processed
            .Where(item => item.Channel.Pair.ShowProcessedCurve)
            .ToList();
        if (shown.Count == 0)
        {
            return null;
        }

        int sampleRate = shown[0].SampleRate;
        double gateOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(shown, sampleRate);

        var traces = shown
            .Select(item => new IrPreviewTrace(
                item.ImpulseResponse,
                item.Channel.Name,
                item.Color))
            .ToList();
        if (summed.Count >= 2 && checkBoxShowSum.Checked)
        {
            traces.Add(new IrPreviewTrace(
                VirtualCrossoverAnalysis.SumImpulseResponses(
                    [.. summed.Select(item => item.ImpulseResponse)]),
                "Sum",
                SumColor,
                2.4));
            if (oppositeSide != null && oppositeSide.SampleRate == sampleRate)
            {
                traces.Add(new IrPreviewTrace(
                    oppositeSide.ImpulseResponse,
                    $"Sum {(project.ActiveSideRight ? "L" : "R")}",
                    OxyColor.FromAColor(110, SumColor),
                    1.0,
                    LineStyle.Dash));
            }
        }

        return new AcousticImpulseRender(
            traces,
            sampleRate,
            gateOffsetMs,
            gatePreview?.LeftMs ?? project.PhaseGateLeftMs,
            gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs,
            gatePreview?.RightMs ?? project.PhaseGateRightMs,
            Step: true);
    }

    // Each side keeps its own placement: its drivers arrive at different times.
    private VirtualCrossoverPhaseGateSettings ActiveGate =>
        project.PhaseGateFor(project.ActiveSideRight);

    // Null = Auto: magnitude anchors one shared window, phase curves follow each arrival START (see ResolvePhaseGateOffsets).
    private double? PinnedGateOffsetMs => gatePreview is { } preview
        ? preview.AutoOffset ? null : preview.OffsetMs
        : ActiveGate.OffsetMs;

    // Placement arithmetic lives in PhaseGatePlacement, shared with the EQ Wizard so both read the same windows and τ.
    private List<double> ResolvePhaseGateOffsets(
        IReadOnlyList<ProcessedChannel> gatedChannels,
        double sharedOffsetMs,
        int sampleRate) =>
        PhaseGatePlacement.ResolvePerCurveOffsets(
            PlacementChannel.From(gatedChannels),
            sharedOffsetMs,
            sampleRate,
            PinnedGateOffsetMs,
            gatePreview?.LeftMs ?? project.PhaseGateLeftMs,
            gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs,
            gatePreview?.RightMs ?? project.PhaseGateRightMs);

    private double ResolveGateOffsetMs(
        IReadOnlyList<ProcessedChannel> processed,
        int sampleRate) =>
        PhaseGatePlacement.ResolveSharedOffsetMs(
            PlacementChannel.From(processed), sampleRate, ActiveGate.OffsetMs);

    internal enum GateCutKind
    {
        OpensAfterArrival,

        ClosesBeforeArrival
    }

    /// <summary>LeadingEdgeLossDb (<see cref="DataHelper.GateLeadingEdgeLossDb"/>) is meaningful for <see cref="GateCutKind.OpensAfterArrival"/> only.</summary>
    internal readonly record struct GateCutChannel(
        string Name,
        double StartMs,
        GateCutKind Kind,
        double LeadingEdgeLossDb);

    internal sealed record GatePlacementVerdict(
        double OffsetMs,
        double PlateauMs,
        double RightMs,
        bool Pinned,
        bool RightSide,
        IReadOnlyList<GateCutChannel> Cut)
    {
        public bool CutsChannels => Cut.Count > 0;

        public string SideLabel => RightSide ? "R" : "L";

        public double PlateauEndMs => OffsetMs + PlateauMs;

        /// <summary>Content between <see cref="PlateauEndMs"/> and here is attenuated, not absent.</summary>
        public double WindowEndMs => PlateauEndMs + RightMs;

        public bool Any(GateCutKind kind) => Cut.Any(item => item.Kind == kind);
    }

    /// <summary>Judges the magnitude view's window (an ABSOLUTE time) against every processed channel.
    /// See docs/tech/virtual-dsp-panel.md#gate-placement-verdict.</summary>
    private GatePlacementVerdict? JudgeGatePlacement(
        IReadOnlyList<ProcessedChannel> processed)
    {
        if (processed.Count == 0)
        {
            return null;
        }

        MagnitudeGateSnapshot snapshot = magnitudeGate;
        int sampleRate = processed[0].SampleRate;
        double offsetMs = snapshot.ResolveGateOffsetMs(
            oppositeSide: false,
            ProcessedChannels.SharedStartAnchorIndex(processed),
            sampleRate);
        var cut = new List<GateCutChannel>();
        foreach (ProcessedChannel item in processed)
        {
            double startMs = TransferIrStartCache.ResolveStartMs(
                item.ImpulseResponse, sampleRate, item.PeakIndex,
                item.ValidRange);
            var view = new ImpulseMeasurementView(item.ImpulseResponse, 0, sampleRate);
            double lossDb = Loss(offsetMs);
            if (JudgeGateCut(
                    startMs,
                    offsetMs,
                    snapshot.Template.PlateauMs,
                    snapshot.Template.RightMs,
                    lossDb,
                    Loss(startMs)) is { } kind)
            {
                cut.Add(new GateCutChannel(item.Channel.Name, startMs, kind, lossDb));
            }

            double Loss(double placementMs) => DataHelper.GateLeadingEdgeLossDb(
                view,
                placementMs,
                snapshot.Template.LeftMs,
                snapshot.Template.PlateauMs,
                snapshot.Template.RightMs);
        }

        return new GatePlacementVerdict(
            offsetMs,
            snapshot.Template.PlateauMs,
            snapshot.Template.RightMs,
            snapshot.PinnedOffsetMs is not null,
            project.ActiveSideRight,
            cut);
    }

    /// <summary>Null when the window holds the channel. Far side judged by geometry (front inside plateau + fade),
    /// near side by leading-edge loss vs the channel's own arrival. See docs/tech/virtual-dsp-panel.md#gate-placement-verdict.</summary>
    internal static GateCutKind? JudgeGateCut(
        double startMs,
        double gateOffsetMs,
        double plateauMs,
        double rightMs,
        double placementLossDb,
        double ownArrivalLossDb)
    {
        if (startMs >= gateOffsetMs + plateauMs + rightMs)
        {
            return GateCutKind.ClosesBeforeArrival;
        }

        return startMs < gateOffsetMs &&
            placementLossDb > PhaseGatePlacement.MaxLeadingEdgeLossDb &&
            placementLossDb > ownArrivalLossDb + GateMisplacementMarginDb
                ? GateCutKind.OpensAfterArrival
                : null;
    }

    /// <summary>3 dB: field misplacements are 44–70 dB apart on this comparison, short gates 0.0 dB.</summary>
    private const double GateMisplacementMarginDb = 3.0;

    internal static string FormatGateCutWarning(GatePlacementVerdict verdict)
    {
        string names = string.Join(", ", verdict.Cut.Select(item => item.Name));
        double earliest = verdict.Cut.Min(item => item.StartMs);
        double latest = verdict.Cut.Max(item => item.StartMs);
        bool one = verdict.Cut.Count == 1;
        string arrivals = one || Math.Abs(latest - earliest) < 0.005
            ? $"{earliest:0.00} ms"
            : $"{earliest:0.00}–{latest:0.00} ms";
        string curves = one ? "that curve reads" : "those curves read";
        string opening = $"⚠ {verdict.SideLabel} gate at {verdict.OffsetMs:0.00} ms ";
        if (!verdict.Any(GateCutKind.ClosesBeforeArrival))
        {
            return opening +
                $"opens after {names} {(one ? "arrives" : "arrive")} ({arrivals}) — " +
                $"{curves} the reverberant tail.";
        }

        if (!verdict.Any(GateCutKind.OpensAfterArrival))
        {
            return opening +
                $"is over before {names} {(one ? "arrives" : "arrive")} ({arrivals}) — " +
                $"{(one ? "that curve holds" : "those curves hold")} none of " +
                $"{(one ? "it" : "them")}.";
        }

        return opening + $"misses {names} (arriving {arrivals}) — " +
            $"{(one ? "that curve is" : "those curves are")} not the response.";
    }

    // Shared by the tooltip and the refusals so they cannot describe a placement differently.
    internal static string FormatGateCutDetail(GatePlacementVerdict verdict)
    {
        bool opensLate = verdict.Any(GateCutKind.OpensAfterArrival);
        bool closesEarly = verdict.Any(GateCutKind.ClosesBeforeArrival);
        bool one = verdict.Cut.Count == 1;
        var text = new System.Text.StringBuilder();
        text.Append($"The gate's plateau runs from {verdict.OffsetMs:0.00} to ")
            .Append($"{verdict.PlateauEndMs:0.00} ms")
            .Append(closesEarly
                ? $", with its fade-out over at {verdict.WindowEndMs:0.00} ms, "
                : ", ")
            .Append(one
                ? "and this channel falls outside it, so its curve — and the sum-loss " +
                    "read-out built from it — does not describe the driver:"
                : "and these channels fall outside it, so their curves — and the " +
                    "sum-loss read-out built from them — do not describe the drivers:")
            .AppendLine()
            .AppendLine();
        foreach (GateCutChannel item in verdict.Cut)
        {
            text.AppendLine(item.Kind == GateCutKind.OpensAfterArrival
                ? $"    {item.Name} — arrives {item.StartMs:0.00} ms, ahead of the plateau; " +
                    "the curve is the reverberant tail, leading-edge loss " +
                    FormatLeadingEdgeLossDb(item.LeadingEdgeLossDb)
                : $"    {item.Name} — arrives {item.StartMs:0.00} ms, after the window " +
                    $"closes at {verdict.WindowEndMs:0.00} ms; it is over before the " +
                    "channel starts and the curve holds none of it");
        }

        if (opensLate)
        {
            text.AppendLine()
                .Append("(Leading-edge loss is what the window throws away ahead of its ")
                .Append("plateau against what it keeps, so ")
                .Append(
                    $"{PhaseGatePlacement.MaxLeadingEdgeLossDb:0} dB is already the ceiling.)")
                .AppendLine();
        }

        text.AppendLine();
        if (opensLate)
        {
            text.Append(verdict.Pinned
                ? "Open Gate… and press Auto: the window then follows this side's own " +
                    "earliest arrival instead of a time fixed to other measurements. "
                : "The gate is on Auto and still opens late, which means a shoulder too " +
                    "short for these arrivals: widen the left fade in Gate…, or check " +
                    "what those channels' sources hold ahead of their front. ");
        }

        if (closesEarly)
        {
            double latest = verdict.Cut
                .Where(item => item.Kind == GateCutKind.ClosesBeforeArrival)
                .Max(item => item.StartMs);
            text.Append(opensLate ? "The window also has to be " : "The window has to be ")
                .Append("long enough to reach ")
                .Append(one ? "it" : "them")
                .Append(": raise the plateau in Gate… past ")
                .Append($"{latest:0.00} ms, or take back the delay that pushes ")
                .Append(one ? "it" : "them")
                .Append(" out of the window. ");
        }

        text.Append("Each side keeps its own gate placement, so the one fitted on ")
            .Append(verdict.SideLabel)
            .Append(" says nothing about ")
            .Append(verdict.RightSide ? "L" : "R")
            .Append(" — switch sides and check it too.");
        return text.ToString();
    }

    private static string FormatLeadingEdgeLossDb(double lossDb) =>
        double.IsFinite(lossDb) ? $"{lossDb:+0.0;-0.0} dB" : "∞ (the window holds none of it)";

    // Both automatic commands are verified on the gated view, so a misplaced gate refuses them.
    private bool GateIsMisplaced => gatePlacement is { CutsChannels: true };

    private bool RefuseOnMisplacedGate(string command)
    {
        if (gatePlacement is not { CutsChannels: true } verdict)
        {
            return false;
        }

        ShowError(
            $"{command} cannot run while the {verdict.SideLabel} side's gate is misplaced.",
            FormatGateCutDetail(verdict));
        return true;
    }

    // One τ for every curve keeps relative phase through the detrend.
    private double ResolveDetrendMs(int referenceSample, int sampleRate) =>
        ActiveGate.DetrendMs ?? referenceSample * 1_000.0 / sampleRate;

    private double ResolveCommonDetrendMs(
        List<ProcessedChannel> processed,
        double gateOffsetMs,
        int sampleRate) =>
        PhaseGatePlacement.ResolveCommonDetrendMs(
            PlacementChannel.From(processed),
            sampleRate,
            CreateVirtualPhaseSettings(
                gateOffsetMs,
                PhaseDetrendMode.Auto,
                manualDetrendMilliseconds: 0.0),
            gatePreview?.DetrendMode ?? project.PhaseDetrendMode,
            gatePreview?.DetrendMs ?? ActiveGate.DetrendMs);

    private PhaseAnalysisSettings CreateVirtualPhaseSettings(
        double gateOffsetMs,
        PhaseDetrendMode detrendMode,
        double manualDetrendMilliseconds) => new(
            gatePreview?.WindowMode ?? project.PhaseWindowMode,
            gatePreview?.FdwCycles ?? project.PhaseFdwCycles,
            detrendMode,
            manualDetrendMilliseconds,
            gateOffsetMs,
            gatePreview?.LeftMs ?? project.PhaseGateLeftMs,
            gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs,
            gatePreview?.RightMs ?? project.PhaseGateRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

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

        using var dialog = new VirtualCrossoverGateDialog();
        dialog.Init(
            traces,
            sampleRate,
            ResolveGateOffsetMs(processed, sampleRate),
            project.PhaseGateLeftMs,
            project.PhaseGatePlateauMs,
            project.PhaseGateRightMs,
            ResolveDetrendMs(reference, sampleRate),
            project.PhaseWindowMode,
            project.PhaseFdwCycles,
            project.PhaseDetrendMode,
            fitOffsetMs,
            autoOffset: ActiveGate.OffsetMs == null);
        // Wired after Init so seeding the controls does not redraw.
        dialog.PreviewChanged = (offsetMs, autoOffset, leftMs, plateauMs, rightMs,
            windowMode, fdwCycles, detrendMode, detrendMs) =>
        {
            gatePreview = (offsetMs, autoOffset, leftMs, plateauMs, rightMs,
                windowMode, fdwCycles, detrendMode, detrendMs);
            RequestRedraw();
        };

        try
        {
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            {
                // Only the placement is per side; lengths and modes are project-wide.
                VirtualCrossoverPhaseGateSettings gate = ActiveGate;
                // Auto = null: keeps following the earliest channel IR start.
                gate.OffsetMs = dialog.AutoOffset ? null : dialog.GateOffsetMs;
                gate.DetrendMs = dialog.DetrendMs;
                project.PhaseGateLeftMs = dialog.LeftMs;
                project.PhaseGatePlateauMs = dialog.PlateauMs;
                project.PhaseGateRightMs = dialog.RightMs;
                project.PhaseWindowMode = dialog.WindowMode;
                project.PhaseFdwCycles = dialog.FdwCycles;
                project.PhaseDetrendMode = dialog.DetrendMode;
                ScheduleSave();
            }
        }
        finally
        {
            gatePreview = null;
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
            ? channel.SideState(rightSide).SpatialAverageFor(SpatialAverageMode)
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
            metrics.BuildCurves(render.Channels, magnitudeGate.SmoothingInverseOctaves);
        if (magnitudes == null ||
            BuildHybridMagnitudes(
                render.Channels,
                magnitudes,
                rightSide,
                magnitudeGate.SmoothingInverseOctaves) is not { } hybrid)
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
        for (int i = 0; i < channels.Count; i++)
        {
            VirtualCrossoverChannel channel = channels[i];
            if (!channel.Pair.Enabled || channel.TransferImpulseResponse == null)
            {
                continue;
            }

            // Drawn without the delay term: a bulk delay wraps phase into a sawtooth and swamps the filter GD.
            DspChannelChain chain = channel.Pair.Bypass
                ? DspChannelChain.Identity
                : channel.Settings.ToChain(channel.Pair.Zone) with { DelayMs = 0 };
            curves.Add(new DspChainCurve(
                $"{channel.Name} filter", chain, ProcessorSampleRateHz, ChannelColors[i]));
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
            project.CorrelationPairIndex, 0, Math.Max(0, labels.Count - 1));
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
            project.CorrelationPairIndex, 0, pairs.Count - 1)];
        JunctionCorrelationView? correlation = null;
        JunctionCoherenceView? coherence = null;
        try
        {
            List<ProcessedChannel> scope = lastProcessedRender is { } render
                ? render.Channels.ToList()
                : [pair.Lower, pair.Upper];
            if (mode == DspPlotMode.Coherence)
            {
                coherence = await Task.Run(() => BuildCoherenceView(pair, scope));
            }
            else
            {
                correlation = await Task.Run(() => BuildCorrelationView(pair, scope));
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

    // Both channels PROCESSED, so lag 0 is the current alignment; the score is the surface Auto delay searches.
    // See docs/tech/virtual-dsp-panel.md#junction-views. Internal for the correlation-view harness.
    internal static JunctionCorrelationView BuildCorrelationView(
        AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildCorrelationView");
        int sampleRate = pair.Lower.SampleRate;
        (Complex[] lower, Complex[] upper,
            ValidSampleRange lowerRange, ValidSampleRange upperRange) =
            CropJunctionPair(pair, scope, sampleRate);
        // No anchor: each channel windowed at its own band-limited front, as Auto delay measures junctions.

        // 1.5 crossover periods each side (floor 3 ms) keeps neighbouring comb lobes in view at 80 Hz.
        double windowMs = Math.Max(3.0, 1.5 * 1000.0 / pair.CrossoverHz);
        double passOctaves = Math.Log2(pair.BandHighHz / pair.BandLowHz);

        // The comb repeats per period: a tenth of a period avoids aliasing at high junctions; window/300 bounds the sweep.
        double stepMs = Math.Max(
            Math.Min(windowMs / 60.0, 100.0 / pair.CrossoverHz),
            Math.Max(0.005, windowMs / 300.0));

        List<SignalPoint> whitened = null!;
        List<SignalPoint> whitenedDirect = null!;
        List<SignalPoint> scoreNormal = null!;
        List<SignalPoint> scoreInverted = null!;
        double lowerArrivalMs = 0;
        double upperArrivalMs = 0;
        Parallel.Invoke(
            // UNTRIMMED: reflections are this curve's subject (honest at bass junctions).
            () => whitened = VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                lower, upper, sampleRate, pair.CrossoverHz, passOctaves,
                windowMs, centerLagMs: 0, phaseTransform: true),
            // Direct sound only: the cut the engine's direct-coherence witness reads.
            () =>
            {
                (Complex[] directLower, Complex[] directUpper) =
                    VirtualCrossoverAnalysis.CutDirectSoundPair(
                        lower, upper, sampleRate,
                        pair.BandLowHz, pair.BandHighHz, pair.CrossoverHz,
                        searchRangeMs: windowMs, lowerRange, upperRange);
                whitenedDirect = VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                    directLower, directUpper, sampleRate,
                    pair.CrossoverHz, passOctaves,
                    windowMs, centerLagMs: 0, phaseTransform: true);
            },
            // The search's own settings (null anchor, level match), or the drawn surface is not the searched one.
            () =>
            {
                (List<VirtualCrossoverAnalysis.JunctionSweepPoint> normal,
                    List<VirtualCrossoverAnalysis.JunctionSweepPoint> inverted) =
                    VirtualCrossoverAnalysis.JunctionLossSweepBothPolarities(
                        upper, lower, sampleRate,
                        pair.BandLowHz, pair.BandHighHz,
                        -windowMs, windowMs, stepMs,
                        gateAnchorSample: null,
                        levelMatch: true,
                        variableValidRange: upperRange,
                        fixedValidRange: lowerRange);
                scoreNormal = Penalized(normal);
                scoreInverted = Penalized(inverted);
            },
            () => lowerArrivalMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                lower, sampleRate, pair.BandLowHz, pair.BandHighHz, lowerRange),
            () => upperArrivalMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                upper, sampleRate, pair.BandLowHz, pair.BandHighHz, upperRange));

        return new JunctionCorrelationView(
            $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}",
            pair.Upper.Channel.Name,
            pair.CrossoverHz,
            pair.BandLowHz,
            pair.BandHighHz,
            whitened,
            whitenedDirect,
            scoreNormal,
            scoreInverted,
            lowerArrivalMs - upperArrivalMs);
    }

    private static List<SignalPoint> Penalized(
        List<VirtualCrossoverAnalysis.JunctionSweepPoint> sweep) =>
        sweep
            .Select(point => new SignalPoint(
                point.DelayMs,
                point.LossDb +
                    VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
                    (point.DipDb - point.LossDb)))
            .ToList();

    // See VirtualCrossoverAnalysis.ArrivalCoherenceLadder. Internal for the harness.
    internal static JunctionCoherenceView BuildCoherenceView(
        AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildCoherenceView");
        int sampleRate = pair.Lower.SampleRate;
        (Complex[] lower, Complex[] upper,
            ValidSampleRange lowerRange, ValidSampleRange upperRange) =
            CropJunctionPair(pair, scope, sampleRate);
        return new JunctionCoherenceView(
            $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}",
            pair.Upper.Channel.Name,
            pair.CrossoverHz,
            pair.BandLowHz,
            pair.BandHighHz,
            VirtualCrossoverAnalysis.ArrivalCoherenceLadder(
                lower, upper, sampleRate,
                pair.BandLowHz, pair.BandHighHz, pair.CrossoverHz,
                lowerRange, upperRange));
    }

    // Valid ranges are shifted into the crop frame so front detections match the search's (matters on glitch-headed records).
    private static (Complex[] Lower, Complex[] Upper,
        ValidSampleRange LowerRange, ValidSampleRange UpperRange)
        CropJunctionPair(
            AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope, int sampleRate)
    {
        List<ProcessedChannel> all = scope.Contains(pair.Lower)
            ? scope.ToList()
            : [pair.Lower, pair.Upper];
        Complex[][] cropped = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            all.Select(item => item.ImpulseResponse).ToList(),
            AlignmentReprocessor.SearchCropLength(sampleRate),
            AlignmentReprocessor.SearchCropPrePeakSamples(sampleRate),
            out int cropStart);
        Complex[] lower = cropped[all.IndexOf(pair.Lower)];
        Complex[] upper = cropped[all.IndexOf(pair.Upper)];
        ValidSampleRange Shifted(ProcessedChannel item, Complex[] croppedIr) =>
            item.ValidRange.IsKnown
                ? new ValidSampleRange(
                    Math.Max(0, item.ValidRange.StartSample - cropStart),
                    Math.Clamp(
                        item.ValidRange.EndSample - cropStart,
                        0,
                        croppedIr.Length))
                : item.ValidRange;
        return (lower, upper,
            Shifted(pair.Lower, lower), Shifted(pair.Upper, upper));
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
        MagnitudeGateSnapshot overlayGate = magnitudeGate;
        AnalysisCurve sumCurve = BuildMeasuredSumCurve(
            overlayGate,
            processed,
            overlayAnchor,
            overlayGate.ResolveGateOffsetMs(
                oppositeSide: false, overlayAnchor, processed[0].SampleRate)).Display;

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
            metrics.BuildCurves(metricChannels, magnitudeGate.SmoothingInverseOctaves);
        string metricLine = VirtualCrossoverMetric.FormatLabel(
            metrics.BuildEntries(metricChannels, metricLoss));
        // The chain graph shows the filters, so it uses the processor's rate, not the measurement rate.
        int sampleRate = ProcessorSampleRateHz;
        try
        {
            if (dialog.FilterIndex == 1)
            {
                VirtualCrossoverSheetPdf.Export(
                    dialog.FileName, project, metricLine, sampleRate, qConvention.Value);
            }
            else
            {
                AtomicFile.WriteAllText(
                    dialog.FileName,
                    VirtualCrossoverSheet.FormatText(
                        project, metricLine, qConvention.Value));
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
        DspProcessorProfile profile = ProcessorProfile;
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

    /// <summary>Writes the analytic crossover proposal (LR24, cut-only gains); delay and polarity are Auto delay's job.</summary>
    /// <returns>Null when written; otherwise a refusal phrase an import's summary can quote.</returns>
    private string? OpenAutoSetupWizard()
    {
        var participating = channels
            .Where(channel => channel.Pair.Enabled &&
                channel.TransferImpulseResponse != null)
            .ToList();
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

        var wizardOptions = new FrequencyResponseOptions { SmoothingInverseOctaves = 3 };
        var dialogChannels = new List<AutoSetupWizardChannel>();
        try
        {
            foreach (VirtualCrossoverChannel channel in participating)
            {
                AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
                    new ImpulseMeasurementView(
                        channel.TransferImpulseResponse!,
                        channel.TransferPeakIndex,
                        channel.SampleRate)
                    {
                        // Or the band read runs down the window's leakage an octave below the real low corner.
                        LowestMeasuredFrequencyHz = channel
                            .SideState(project.ActiveSideRight).MeasuredBand.LowEdgeHz,
                        HighestMeasuredFrequencyHz = channel
                            .SideState(project.ActiveSideRight).MeasuredBand.HighEdgeHz
                    },
                    wizardOptions,
                    CalibrationFor(channel.SideState(project.ActiveSideRight)));
                // Discount frequencies the measurement's coherence did not trust.
                IReadOnlyList<double>? coherence =
                    channel.TransferCoherence is { Length: > 1 } linear
                        ? CoherencePerPoint(linear, curve.Points, channel.SampleRate)
                        : null;
                IReadOnlyList<SignalPoint>? distortion = channel.DistortionCurve;
                OxyColor accent = ChannelColors[channels.IndexOf(channel)];
                // With two similar drivers, existing corners decide which plays lower.
                VirtualCrossoverChannelSettings settings =
                    channel.SideSettings(project.ActiveSideRight);
                dialogChannels.Add(new AutoSetupWizardChannel(
                    $"{channel.Name} — {channel.Settings.DisplayName}",
                    Color.FromArgb(accent.R, accent.G, accent.B),
                    VirtualCrossoverAlignmentStages.StageOf(channel.Pair.Zone),
                    curve.Points,
                    coherence,
                    distortion,
                    CrossoverAutoSetup.EstimateBand(curve.Points, coherence, distortion),
                    // FIR corners stand in where the IIR crossover is off.
                    settings.EffectiveHighPassHz,
                    settings.EffectiveLowPassHz,
                    channel.TransferImpulseResponse));
            }
        }
        catch (ArgumentException exception)
        {
            ShowError("A channel's response has no usable band.", exception.Message);
            return "a channel's response has no usable band";
        }

        using var dialog = new VirtualCrossoverAutoSetupDialog();
        dialog.Init(
            participating[0].SampleRate,
            ProcessorSampleRateHz,
            dialogChannels);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } proposals)
        {
            return "cancelled in the wizard";
        }

        int clearedRotations = 0;
        for (int i = 0; i < participating.Count; i++)
        {
            VirtualCrossoverChannel channel = participating[i];
            CrossoverProposal proposal = proposals[i];
            // A crossover is one electrical filter: both sides get the same frequencies, families, slopes and gain.
            foreach (bool rightSide in new[] { false, true })
            {
                if (channel.Pair.Mono && rightSide)
                {
                    continue;
                }

                VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
                settings.CrossoverKind = proposal.Kind;
                if (proposal.HighPassEdge is { } highPass)
                {
                    settings.HighPassEdge = highPass;
                }
                if (proposal.LowPassEdge is { } lowPass)
                {
                    settings.LowPassEdge = lowPass;
                }
                settings.GainDb = proposal.GainDb;
                // The phase angle is stated AT the crossover, so a wizard rewrite resets rotations (and says so).
                if (settings.PhaseRotationDegrees != 0)
                {
                    settings.PhaseRotationDegrees = 0;
                    clearedRotations++;
                }
            }

            ApplySettingsToControl(channel);
        }

        if (dialog.ChainOrder is { } chainOrder)
        {
            IReadOnlyList<VirtualCrossoverChannel> sorted = ReorderIntoSlots(
                channels,
                chainOrder.Select(index => participating[index]).ToList());
            ApplyChannelOrder(
                sorted.Select(channel => channels.IndexOf(channel)).ToList());
        }

        // The wizard wrote both sides; the lock must not carry the shown side's other edge over.
        sideLock.Remember(channels.Select(channel => channel.Pair));
        ScheduleSave();
        RedrawAll();
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

    /// <summary>Only the reordered members' slots are reused; blocks the wizard did not look at keep theirs.</summary>
    internal static IReadOnlyList<T> ReorderIntoSlots<T>(
        IReadOnlyList<T> all,
        IReadOnlyList<T> reordered)
        where T : class
    {
        var slots = new HashSet<T>(reordered);
        var result = new List<T>(all.Count);
        int next = 0;
        foreach (T item in all)
        {
            result.Add(slots.Contains(item) ? reordered[next++] : item);
        }

        return result;
    }

    // γ² on a linear grid (bin k -> k·rate/(2·(len−1))) averaged per 1/3-octave point to match the wizard's magnitude curve.
    private static IReadOnlyList<double> CoherencePerPoint(
        double[] coherence,
        IReadOnlyList<SignalPoint> points,
        int sampleRate)
    {
        int fftLength = 2 * (coherence.Length - 1);
        double lowFactor = Math.Pow(2.0, -1.0 / 6.0);
        double highFactor = Math.Pow(2.0, 1.0 / 6.0);
        var values = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            double frequency = points[i].X;
            int lo = Math.Max(0, (int)Math.Floor(frequency * lowFactor * fftLength / sampleRate));
            int hi = Math.Min(
                coherence.Length - 1,
                (int)Math.Ceiling(frequency * highFactor * fftLength / sampleRate));
            double sum = 0;
            int count = 0;
            for (int bin = lo; bin <= hi; bin++)
            {
                sum += coherence[bin];
                count++;
            }

            values[i] = count > 0 ? sum / count : 1.0;
        }

        return values;
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
            project.SaveTo(dialog.FileName);
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
            MissingSourceSides().ToList();
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
            SelectedPath = project.ProjectDirectory ?? string.Empty
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        relinkDirectory = dialog.SelectedPath;
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
            MissingSourceSides().ToList();
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

    // Only sides naming a FILE: a history-only reference from another machine is not fixable by a folder.
    private IEnumerable<(VirtualCrossoverChannel Channel, bool RightSide)>
        MissingSourceSides()
    {
        foreach (VirtualCrossoverChannel channel in channels)
        {
            foreach (bool rightSide in new[] { false, true })
            {
                if (channel.Pair.Mono && rightSide)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(
                        channel.SideSettings(rightSide).SourceFilePath) &&
                    channel.SideState(rightSide).TransferImpulseResponse == null)
                {
                    yield return (channel, rightSide);
                }
            }
        }
    }

    private static string DescribeMissingSources(
        IReadOnlyList<(VirtualCrossoverChannel Channel, bool RightSide)> missing)
    {
        string sides = string.Join(
            ", ",
            missing.Select(item => SideLabel(item.Channel, item.RightSide)));
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
                when sessionCalibration is { } session:
                OfferSessionCalibration(session);
                break;

            case VirtualCrossoverCalibrationNotice.MatchedBySlotName:
                MessageBox.Show(
                    FindForm(),
                    "This session names its microphone calibration by a slot only " +
                    $"('{SelectedCalibrationName()}'), without the curve itself — it " +
                    "was written by an older version. This computer's entry of the " +
                    "same name is selected, but nothing says the two files agree: " +
                    "check that it is the calibration of the microphone these " +
                    "measurements were taken with.",
                    "Virtual DSP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                break;

            case VirtualCrossoverCalibrationNotice.KeptPrevious:
                string kept = SelectedCalibrationName() is { } name
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
    private void OfferSessionCalibration(VirtualCrossoverSessionCalibration session)
    {
        if (calibrationAdder == null)
        {
            MessageBox.Show(
                FindForm(),
                $"This session carries the microphone calibration {session.Description} " +
                "it was tuned with, and it is selected, so the curves match the ones " +
                "its author saw.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        DialogResult answer = MessageBox.Show(
            FindForm(),
            $"This session carries the microphone calibration {session.Description} " +
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

        string? addedId = calibrationAdder(session);
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
