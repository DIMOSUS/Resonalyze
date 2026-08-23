using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.History;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>
/// The Virtual DSP tool: up to eight measured transfer IRs (as left/right pairs)
/// are run through per-channel DSP chains (gain, delay, polarity, crossover, PEQ)
/// and summed as complex responses, predicting the combined output before
/// touching the hardware. The acoustic plot shows the raw/processed channels,
/// their complex sum and the sum loss; the DSP plot shows each chain's own
/// magnitude, phase or group delay. The whole state persists as a project file
/// across restarts.
/// </summary>
public partial class VirtualCrossoverPanel : UserControl
{
    private const int SaveDebounceMilliseconds = 2_000;

    // The channel-list bounds. The minimum matches the summed-response metric's
    // need for at least two channels; the maximum matches the project format's
    // capacity. The default is the count shown before the list became resizable.
    private const int MinChannelCount = 2;
    private const int MaxChannelCount = VirtualCrossoverProjectFile.MaximumChannelCount;
    private const int DefaultChannelCount = 3;

    private const string NoSourcesHint =
        "Pick a measurement for at least one channel (Source...).\n" +
        "Every source needs a loopback transfer IR recorded at the same\n" +
        "microphone position and sample rate.";

    private static readonly OxyColor SumColor = OxyColors.White;
    private static readonly OxyColor LossColor = OxyColor.FromRgb(230, 184, 0);
    private static readonly OxyColor[] ChannelColors =
    [
        OxyColor.FromRgb(86, 156, 255),   // A: blue
        OxyColor.FromRgb(255, 150, 64),   // B: orange
        OxyColor.FromRgb(96, 210, 120),   // C: green
        OxyColor.FromRgb(200, 130, 255),  // D: purple
        OxyColor.FromRgb(80, 210, 220),   // E: cyan
        OxyColor.FromRgb(240, 100, 140),  // F: pink
        OxyColor.FromRgb(210, 200, 90),   // G: yellow
        OxyColor.FromRgb(140, 200, 90)    // H: lime
    ];

    private readonly System.Windows.Forms.Timer saveTimer = new()
    {
        Interval = SaveDebounceMilliseconds
    };

    // The magnitude view reads through the SAME gate as the phase and impulse
    // views (the gate dialog's offset, shoulders and Fixed/FDW mode), so the
    // three views describe one time window. One immutable record, refreshed on
    // the UI thread by RequestRedraw and read by reference from the PLINQ
    // magnitude builds on worker threads — a single atomic reference, never
    // the live controls, the project or the gate-preview tuple. The template's
    // offset is a placeholder; each build stamps its own (see
    // BuildMagnitudeCurve). The initial value only bridges construction: every
    // curve is built after the first unsuppressed redraw refreshed it.
    // Internal (with the resolver) so the per-side pin choice is pinned by a
    // unit test without constructing the panel.
    internal sealed record MagnitudeGateSnapshot(
        PhaseAnalysisSettings Template,
        double? PinnedOffsetMs,
        double? OppositePinnedOffsetMs,
        int SmoothingInverseOctaves)
    {
        // The one place the pinned-vs-anchor choice lives. The two sides'
        // arrivals sit at different times and the project stores their pinned
        // offsets separately — the active side's pin must never window the
        // OPPOSITE side's sum (its own pin, or its own anchor when unpinned).
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

    // The EQ Wizard's own export machinery, reused whole so a channel's bank leaves
    // through exactly the formats, shelf/preamp rules and warnings the wizard uses.
    private readonly EqWizardImportExportCoordinator peqExport = new();

    // Which loaded project the blocks currently describe; bumped by every bind.
    // Read only by the EQ Wizard handoff, whose return address has to outlive a
    // trip to another mode and must not survive a project replacing this one.
    private long projectGeneration;

    // The model-to-control binding. VirtualCrossoverChannel is UI-free, so the
    // panel owns the mapping to each block's control; only the binding methods
    // (ApplySettingsToControl, UpdateSourceButton, tooltips…) look it up, and
    // the algorithmic paths read the model directly.
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

    // The folder the user pointed at to relink an imported session's missing
    // measurements: an extra search root for every source this session resolves
    // afterwards (a mono toggle re-resolves a side long after the import). Belongs
    // to the imported session, so binding a project clears it.
    private string? relinkDirectory;

    // Candidate gate values while the gate dialog is open, so the gated plots
    // track the dialog live; null once it closes (Save committed them to the
    // project, Cancel reverts by simply dropping them). AutoOffset mirrors the
    // dialog's Auto button: the preview must gate per-curve exactly as Save
    // will, while OffsetMs still shows where the dialog's window sits (the
    // impulse overlays draw it).
    private (double OffsetMs, bool AutoOffset, double LeftMs, double PlateauMs,
        double RightMs, PhaseWindowMode WindowMode, int FdwCycles,
        PhaseDetrendMode DetrendMode, double DetrendMs)? gatePreview;
    // The Q convention the last tuning sheet WRITTEN this session was stated in; null
    // until one is, when the shared setting pre-selects the dialog instead. Session
    // state on purpose: the answer describes the sheet being printed, not the panel
    // or the project (see AskSheetQConvention).
    private PeqQConvention? sheetQConvention;
    // The verdict on the window the side on screen is gated at, from the last
    // redraw (null before the first one, or with no processed channels). The
    // Auto commands read this instead of judging the placement themselves:
    // both are disabled until the redraw that fills it has settled, which is
    // the same condition that puts their curves on screen — see
    // RefreshAutoActionsEnabled.
    private GatePlacementVerdict? gatePlacement;
    private VirtualCrossoverAcousticPlot acousticPlot = null!;
    private VirtualCrossoverDspChainPlot dspChainPlot = null!;
    private bool initialized;
    private bool suppressProjectEvents;

    // Single-flight coalescing for the interactive redraw. While a redraw's heavy
    // work (the ApplyChain FFTs) runs on a background task the UI stays live; a
    // change that arrives mid-flight only flags a rerun, so exactly one redraw is
    // in flight at a time and it always ends on the latest settings.
    private Task? redrawTask;
    private bool redrawPending;
    private bool savePending;
    // Save runs on a debounce; the failure notice is shown once per session and
    // re-armed by the next successful save.
    private bool reportedSaveFailure;
    private bool loadingProject;
    private int pendingSourceLoads;

    // The shared EQ target, null until the host wires it (and in the designer).
    private EqTargetCurve? targetCurve;

    // The colour the Target toggle wears while it is live: its curve's own, the
    // way the Sum and Sum loss toggles wear theirs. Seeded from the designer and
    // following the shared target from then on, so muting the toggle for a view
    // that cannot show it has a colour to come back to.
    private Color targetToggleColor;

    public VirtualCrossoverPanel()
    {
        InitializeComponent();
        // The scrolling channel list (and the panel itself when the window is
        // narrow) use native scrollbars; theme them dark so they match the app
        // instead of showing the default light bar.
        Ui.DarkScrollBars.Apply(channelListPanel);
        Ui.DarkScrollBars.Apply(this);
        // The channel blocks are created dynamically into the scrolling list so
        // the tool can host more channels than fit the window. Start with the
        // default count; the loaded project resizes the list to its own count.
        SetChannelCount(DefaultChannelCount);

        // Same idea for the shared curves: the toggles wear their plot colors.
        checkBoxShowSum.ForeColor = Color.FromArgb(SumColor.R, SumColor.G, SumColor.B);
        checkBoxShowLoss.ForeColor = Color.FromArgb(LossColor.R, LossColor.G, LossColor.B);
        targetToggleColor = checkBoxShowTarget.ForeColor;

        metrics = new VirtualCrossoverMetrics(processingCoordinator, BuildMagnitudeCurve);
        acousticPlot = new VirtualCrossoverAcousticPlot(
            mainPlotView, NoSourcesHint, CurrentAcousticView());
        dspChainPlot = new VirtualCrossoverDspChainPlot(dspPlotView, CurrentDspPlotMode());
        mainPlotView.Paint += (_, _) => AppProfiler.FrameMark("vdsp-main");
        dspPlotView.Paint += (_, _) => AppProfiler.FrameMark("vdsp-dsp");
        InitializeSmoothingComboBox();
        WirePanelEvents();
        InitializeToolTips();

        buttonAutoDelay.Click += (_, _) => AutoAlignDelay();
        buttonAutoSetup.Click += (_, _) => OpenAutoSetupWizard();
        buttonCaptureOverlay.Click += async (_, _) => await CaptureSumToOverlayAsync();
        buttonExport.Click += async (_, _) => await ExportTuningSheetAsync();
        buttonPhaseGate.Click += async (_, _) => await OpenPhaseGateDialogAsync();
        buttonTargetSettings.Click += (_, _) => OpenTargetSettings();
        buttonSessionImport.Click += async (_, _) => await ImportSessionAsync();
        buttonSessionExport.Click += (_, _) => ExportSession();
        buttonAudition.Click += async (_, _) => await AuditionTrackAsync();
        buttonAddChannel.Click += (_, _) => AddChannel();
        buttonRemoveChannel.Click += (_, _) => RemoveChannel();
        buttonCopyLeftToRight.Click += (_, _) => CopySideSettings(fromRight: false);
        buttonCopyRightToLeft.Click += (_, _) => CopySideSettings(fromRight: true);

        saveTimer.Tick += (_, _) => FlushProject();
        // The designer file owns Dispose; the unsaved project state and the
        // helper components are released through the Disposed event instead.
        Disposed += (_, _) =>
        {
            FlushProject();
            processingCoordinator.Dispose();
            saveTimer.Dispose();
            toolTip.Dispose();
        };
    }

    /// <summary>The measurement history used by the source pickers. Wired by the host form.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal MeasurementHistoryService? HistoryService { get; set; }

    /// <summary>
    /// How the DSP being tuned defines Q, mirrored here from the application settings
    /// (the EQ Wizard owns the selector). Only pre-selects the export's own question —
    /// the simulated chain itself is always the RBJ realization, so this cannot change
    /// what the panel plots or what a project file holds.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal PeqQConvention TargetDspQConvention { get; set; } = PeqQConvention.Rbj;

    /// <summary>
    /// Microphone calibration applied to the magnitude curves, resolved from the
    /// panel's own <see cref="comboBoxCalibration"/> selection. Null when
    /// calibration is off or unavailable.
    /// </summary>
    private CalibrationFile? Calibration { get; set; }

    // Resolves a calibration by id; supplied by the host form, which owns the
    // configured calibrations. Null until the host wires it.
    private Func<string?, CalibrationFile?>? calibrationResolver;
    private IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries = [];

    // Adds a curve to the host's calibration list and returns the new entry's id;
    // supplied by the host form. Null until wired (the offer is then not made).
    private Func<VirtualCrossoverSessionCalibration, string?>? calibrationAdder;

    // The curve the bound project carries that no configured entry matches,
    // offered in the selector as its own item (see
    // VirtualCrossoverCalibrationSelection). Set when a project binds, dropped
    // once a configured entry with the same curve appears.
    private VirtualCrossoverSessionCalibration? sessionCalibration;

    // What the last bind has to say about its calibration, shown once the import
    // finishes (after the relink prompt, which is about the measurements).
    private VirtualCrossoverCalibrationNotice pendingCalibrationNotice;

    /// <summary>
    /// Saves the given curve as a Captured Frequency Response overlay and returns
    /// the slot it landed in (null when all slots are taken). Wired by the host form.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<string, OverlayPoint[], int?>? OverlayCaptureRequested { get; set; }

    /// <summary>
    /// Pushes the sum-loss read-out to the host: a compact per-junction column for
    /// display and the full banded breakdown for a tooltip. Wired by the host form,
    /// which shows it in the right-side panel where overlays sit in analysis modes.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<string, string>? MetricChanged { get; set; }

    /// <summary>
    /// Pushes the one warning line to the host: the text to show, the whole
    /// explanation for its tooltip, and the colour to draw the text in. An empty
    /// text means there is nothing to warn about and the host hides the line.
    /// Wired by the host form, which shows it above the sum-loss read-out: a
    /// warning belongs beside the numbers it invalidates, and the panel's own
    /// area is plot and controls edge to edge.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<string, string, Color>? WarningChanged { get; set; }

    /// <summary>
    /// The EQ target curve this tool can draw over its predicted sum. Pushed by
    /// the host, which holds the one definition shared with the EQ Wizard. A
    /// value equal to the current one is ignored, so the host may push it on
    /// every settings change without costing a redraw.
    /// </summary>
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

    /// <summary>
    /// Raised when this tool's own Target dialog edited the shared curve. The
    /// host writes it back to the EQ Wizard, which owns and persists it — that
    /// write-back is what makes the two panels show one target rather than two
    /// that drifted apart.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<EqTargetCurve>? TargetCurveChanged { get; set; }

    /// <summary>
    /// Raised when the user picks "Edit in EQ Wizard" on a channel's PEQ menu. The
    /// host hands the request to the wizard and switches the mode; the result comes
    /// back through <see cref="TryApplyPeqFromWizard"/>.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<VirtualDspEqHandoffRequest>? EditPeqInWizardRequested { get; set; }

    /// <summary>
    /// Raised when the user picks "Open in analyzers" on a channel's source menu:
    /// the host loads this side's measurement into the analysis modes and lands on
    /// Frequency Response. The arguments mirror the persisted source reference in
    /// the priority the panel itself resolves it — the history entry when it still
    /// exists, else the located file path; at least one is non-null.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<Guid?, string?>? OpenSourceInAnalyzersRequested { get; set; }

    /// <summary>
    /// Called by the host whenever the tool tab becomes active. The first call
    /// loads the saved project and re-resolves its sources.
    /// </summary>
    internal void OnPanelShown()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        LoadProjectSafely();
    }

    // ---------------------------------------------------------------- project

    // Fire-and-forget with a guard: an exception in the async load would
    // otherwise vanish into an unobserved task.
    private async void LoadProjectSafely()
    {
        try
        {
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadOrDefault();
            await ApplyProjectAsync(loaded, imported: false);
            NotifyIfProjectBackedUp(loaded.BackupNoticePath);
        }
        catch (Exception exception)
        {
            // The tool still opens on defaults, but the user's stored crossover
            // is not what they are looking at — saying nothing invites them to
            // re-tune on top of a silently discarded project.
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

    // Tell the user, once, when their unreadable session file was moved aside so
    // they know a .backup exists to recover from.
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

    // Locks the panel while a project applies. Re-resolving every channel's
    // source reads and reprocesses the stored transfer IRs, which takes several
    // seconds; until this the panel sat enabled showing the "no sources" hint,
    // so the last session looked lost right up until it snapped into place. The
    // whole control tree is disabled (a load rebuilds the channel blocks, so
    // covering not-yet-created controls means disabling the parent), the plot
    // shows a loading note, and the cursor turns to a wait cursor.
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

    // Binds a project (the internal autosave or an imported session) to the UI:
    // controls, view flags, and freshly re-resolved sources. `imported` says which
    // of the two it is: a session from a file may have been written on another
    // machine, whose calibration ids mean nothing here.
    private async Task ApplyProjectAsync(VirtualCrossoverProjectFile newProject, bool imported)
    {
        SetProjectLoading(true);
        try
        {
            await BindProjectAsync(newProject, imported);
        }
        finally
        {
            // Clear the loading state BEFORE the redraw so the final frame shows
            // the real plot/metric, not the loading note — the bind's own
            // interim redraws (e.g. the calibration combo refresh, which runs
            // before the sources resolve) are what kept resetting the note back
            // to the "no sources" hint.
            SetProjectLoading(false);
            RedrawAll();
        }
    }

    private async Task BindProjectAsync(VirtualCrossoverProjectFile newProject, bool imported)
    {
        // Read before the project is swapped: a legacy session naming a calibration
        // this machine lacks keeps the selection the panel had.
        string? previousCalibrationId =
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration);
        VirtualCrossoverSessionCalibration? previousSession = sessionCalibration;
        project = newProject;
        relinkDirectory = null;
        // A new project on the same blocks. The channel OBJECTS are reused when the
        // count matches (see the rebind below), so nothing about a channel reference
        // says which session it now describes — this counter does, and an EQ Wizard
        // handoff taken from the old one is refused by it rather than landing on a
        // channel the user never opened.
        projectGeneration++;
        // Match the block list to the project's channel count (validated into the
        // supported range on load), so an imported 2- or 6-channel session shows
        // exactly its channels.
        SetChannelCount(project.Pairs.Count);

        suppressProjectEvents = true;
        try
        {
            checkBoxShowLoss.Checked = project.ShowLossCurve;
            checkBoxShowTarget.Checked = project.ShowTargetCurve;
            numericTargetLevel.Value =
                numericTargetLevel.ClampValue(project.TargetLevelDb);
            radioViewImpulse.Checked = project.ShowImpulseView;
            radioViewPhase.Checked =
                !project.ShowImpulseView && project.ShowPhaseView;
            radioViewMagnitude.Checked =
                !project.ShowImpulseView && !project.ShowPhaseView;
            // After the radios: the Sum is remembered per view, so which answer
            // applies is decided by the view this project opens on.
            ApplySumToggleForView();
            ApplyProjectTarget();
            radioSideRight.Checked = project.ActiveSideRight;
            radioSideLeft.Checked = !project.ActiveSideRight;
            acousticPlot.ConfigureForView(CurrentAcousticView());
            comboBoxSmoothing.SelectedItem =
                OverlaySmoothing.IsValid(project.SmoothingCode)
                    ? project.SmoothingCode
                    : 12;
            radioDspMagnitude.Checked =
                project.EffectiveDspPlotMode == DspPlotMode.Magnitude;
            radioDspPhase.Checked =
                project.EffectiveDspPlotMode == DspPlotMode.Phase;
            radioDspGroupDelay.Checked =
                project.EffectiveDspPlotMode == DspPlotMode.GroupDelay;
            radioDspCorrelation.Checked =
                project.EffectiveDspPlotMode == DspPlotMode.Correlation;
            comboBoxCorrelationPair.Enabled = radioDspCorrelation.Checked &&
                comboBoxCorrelationPair.Items.Count > 0;

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

        UpdateSideRadioTexts();
        // The final redraw is issued by ApplyProjectAsync after the loading
        // state clears, so it draws the real plot instead of the loading note.
    }

    // The restore ORDER is the cross-rate import contract: BOTH physical
    // slots of EVERY channel are wiped before the first source resolves.
    // Per slot, because through the effective accessor a mono pair's real
    // right slot is unreachable, and a stale measurement from the previous
    // project would otherwise resurface the moment the pair stops being
    // mono. Across ALL channels up front, because the rate guard in
    // TryAssignSource scans every still-resolved side: cleared one channel
    // at a time, an imported session at a different sample rate would lose
    // that vote against the previous project's channels — each source
    // silently refused against the not-yet-replaced rest, leaving only the
    // last channel resolved (field bug). Then both sides of each channel
    // resolve up front (the stereo Auto delay needs them together); a mono
    // pair resolves its single slot once. Static and delegate-fed so the
    // order itself is unit-testable.
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
            // Still must not break the tool (a read-only install directory used
            // to be the motivating case) — but it cannot stay silent either.
            // This runs on a debounce, so every crossover edit was quietly
            // failing to persist and the user only found out on the next launch,
            // when the whole tuning session was gone. Reported once per session
            // so the message does not fire on every keystroke.
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

    // ------------------------------------------------------------ calibration

    /// <summary>
    /// Wires the microphone calibration source. The host owns the configured
    /// calibrations, so it supplies both a resolver and the list of entries; the
    /// panel offers them in its own selector. Called again whenever the
    /// configured calibrations change, refreshing the selector.
    /// </summary>
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

    // The selector after the configured list changed: the selection stays where
    // it was, marked if its entry is gone or unusable, and a session-carried curve
    // hands over to a configured entry the moment one holds the same curve — the
    // user just added it (or already had it, and the list arrived after the
    // project). The project's stored form follows, so an autosave written after
    // the list changed says the same thing the selector shows.
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
        // A handover (or a re-read of an edited file) changed what the session
        // says; the autosave must not wait for an unrelated edit to learn it. Only
        // once the project is the loaded one: before that this is the placeholder,
        // and saving it would overwrite the real autosave before it was read.
        if (PersistCalibrationSelection() && initialized)
        {
            ScheduleSave();
        }
    }

    // The selector for a project that was just bound: the curve the project
    // carries decides, the id it names is a hint, and a legacy id that resolves
    // to nothing keeps what the panel had (see VirtualCrossoverCalibrationSelection).
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
        // The bound project's own statement is re-derived from the selection:
        // a kept previous choice or an entry matched by curve is what the
        // session now says, and the next autosave must agree with the selector.
        PersistCalibrationSelection();
    }

    // Rebuilds the selector's items — the configured calibrations plus the
    // session's own curve, when it offers one — selects the given item, then
    // resolves the calibration the curves use and redraws. A selection that is
    // no longer configured keeps its entry, marked, so the stored preference is
    // not overwritten by the rebuild.
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

    // Resolves the selector's selection to a curve: the session's own curve, one of
    // the configured entries, or nothing for Off (and for an absent resolver),
    // matching the loopback-referenced default.
    private CalibrationFile? ResolveSelectedCalibration(string? calibrationId) =>
        VirtualCrossoverCalibrationSelection.IsSession(calibrationId)
            ? sessionCalibration?.Curve
            : calibrationResolver?.Invoke(calibrationId);

    private void ResolveCalibration() =>
        Calibration = ResolveSelectedCalibration(
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration));

    // Writes the selector's selection into the project in its persisted form: the
    // curve the session is tuned with, plus the id of the configured entry it came
    // from (none for the session's own curve). Resolved through the SAME path the
    // curves use, so what the file carries is what the plot shows. True when the
    // stored form changed.
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

    // The calibration the EQ Wizard pins for a handoff: the curve itself, not an
    // id, because the session's own curve has no id the wizard's list could
    // resolve — and the identity the handoff promises is with the curve the plot
    // draws, whatever it is called.
    private string? SelectedCalibrationName() =>
        MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration) is { } id
            ? CalibrationEntriesWithSession()
                .FirstOrDefault(entry =>
                    string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                ?.Name
            : null;

    // ----------------------------------------------------------------- wiring

    private void WirePanelEvents()
    {
        checkBoxShowSum.CheckedChanged += (_, _) => OnViewChanged();
        checkBoxShowLoss.CheckedChanged += (_, _) => OnViewChanged();
        checkBoxShowTarget.CheckedChanged += (_, _) => OnViewChanged();
        numericTargetLevel.ValueChanged += (_, _) => OnViewChanged();
        // Three-radio group: each fires on both the check and the uncheck, so
        // act only on the one that became checked to run the switch exactly
        // once per mode change.
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
        comboBoxSmoothing.SelectedIndexChanged += (_, _) => OnViewChanged();
        comboBoxCalibration.SelectedIndexChanged += (_, _) => OnCalibrationChanged();
        // The DSP-mode radios span TWO containers (the chain trio on
        // dspModePanel, Correlation on its own panel beside the pair
        // selector), and WinForms only auto-excludes radios within one
        // container — so the exclusivity across the panels is wired by hand.
        // Each handler still acts only on the radio that became CHECKED (a
        // check-and-uncheck pair fires both), and it clears the other
        // container FIRST, so OnDspPlotModeChanged never reads a transient
        // two-checked state. Clearing fires the cleared radios' handlers with
        // Checked == false, which the guards ignore.
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
        comboBoxCorrelationPair.SelectedIndexChanged +=
            (_, _) => OnCorrelationPairChanged();
        radioDspGroupDelay.CheckedChanged += (_, _) =>
        {
            if (radioDspGroupDelay.Checked) OnChainDspModeChecked();
        };
        // Two-radio group: listening to one of them reacts exactly once per
        // side switch.
        radioSideRight.CheckedChanged += (_, _) => OnActiveSideChanged();
    }

    // Flips the whole tool to the other side of every pair: the channel
    // controls rebind to that side's settings, and the plots, metric and delay
    // read-outs recompute from its measurements. Each side keeps its own
    // processed-IR cache, so switching back and forth is cheap.
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

    // The "L→R" / "R→L" commands: copy one side's chain onto the other for the
    // channels AND the chain parts the user picks in the dialog (see
    // VirtualCrossoverCopySideDialog for what is ticked by default and why).
    // The source is never copied — each side has its own measurement — and mono
    // pairs, having one settings set, are not offered.
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

    // The parts of one side's chain the dialog ticked, written onto the other side;
    // everything else is left as the target had it. The default ticks are the
    // crossover and the PEQ — the magnitude shape, which describes the driver —
    // while gain, delay, polarity and the all-pass are opt-in, because each aligns a
    // driver against its own side's level and geometry, and a left tweeter's arrival
    // is not a right tweeter's. The source measurement is never among them.
    // PeqBand is an immutable record, so a fresh list is a deep enough copy.
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

        if (scope.AllPass)
        {
            to.AllPassType = from.AllPassType;
            to.AllPassFrequencyHz = from.AllPassFrequencyHz;
            to.AllPassQ = from.AllPassQ;
        }

        if (scope.Peq)
        {
            to.PeqPreampDb = from.PeqPreampDb;
            to.PeqBands = from.PeqBands.ToList();
            to.PeqSourceName = from.PeqSourceName;
        }
    }

    // The side radios double as source indicators (● has at least one source,
    // ○ none), so switching to an empty side is never a surprise blank plot.
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

    // ----------------------------------------------------------- channel list

    // A channel block is created per runtime and added to the scrolling list, so
    // the block count is a plain runtime decision (persisted in the project) with
    // no fixed designer controls. Colour and name follow the block's index.
    private VirtualCrossoverChannel CreateChannel(int index)
    {
        // The block keeps its own designer-defined size, which the control scales
        // for the current DPI (AutoScaleMode.Font); overriding it here with raw
        // pixels would clip its scaled content on high-DPI displays.
        var control = new VirtualCrossoverChannelControl
        {
            BackColor = Color.FromArgb(46, 51, 62),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 0, 6),
            ChannelName = ChannelNameFor(index)
        };

        // The block header and curve checkboxes carry the channel's plot colour,
        // so a curve is traceable to its block at a glance.
        OxyColor color = ChannelColors[index];
        control.SetAccentColor(Color.FromArgb(color.R, color.G, color.B));

        // Register the block's per-field tooltips at creation, not once in the
        // constructor: blocks added later (a loaded project with more channels, the
        // Add-channel button) are created here too and would otherwise show none.
        control.ApplyTooltips(toolTip);

        var channel = new VirtualCrossoverChannel(ChannelNameFor(index));
        channelControls[channel] = control;
        control.SettingsChanged += (_, _) => OnChannelSettingsChanged(channel);
        control.SourceClicked += (_, _) => ShowSourceMenu(channel);
        control.PeqMenuClicked += (_, _) => ShowPeqMenu(channel);
        control.CollapsedChanged += (_, _) => OnChannelCollapsedChanged(channel);
        return channel;
    }

    // The control bound to a runtime channel. Only the WinForms binding methods
    // look it up; the algorithmic paths read the model directly.
    private VirtualCrossoverChannelControl ControlFor(VirtualCrossoverChannel channel) =>
        channelControls[channel];

    // The project runs at ONE sample rate — a measurement that disagrees is rejected on
    // load — so the first resolved side answers for the whole project. Both physical
    // sides are read because the side currently on screen may be the empty one. A project
    // with no source yet has no rate of its own, and the blocks keep their default.
    private double ProjectSampleRateHz
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

            return VirtualCrossoverChannelControl.DefaultSampleRateHz;
        }
    }

    // Every block's all-pass readout evaluates the digital filter, so every block needs
    // the project's rate — including the ones with no source, which have none to report
    // but are tuned against the same project. Broadcast rather than pushed per channel:
    // resolving a source on ONE channel changes the rate every other block must use.
    private void PushProjectSampleRateToChannels()
    {
        double sampleRateHz = ProjectSampleRateHz;
        foreach (VirtualCrossoverChannel channel in channels)
        {
            ControlFor(channel).SampleRateHz = sampleRateHz;
        }
    }

    // Channel names run A, B, C… by index; shared with the tuning sheets.
    private static string ChannelNameFor(int index) =>
        VirtualCrossoverSheet.ChannelName(index);

    // Grows or shrinks the block list to the requested count (clamped to the
    // valid range) without touching the project — the callers own persistence.
    private void SetChannelCount(int count)
    {
        count = Math.Clamp(count, MinChannelCount, MaxChannelCount);

        while (channels.Count > count)
        {
            VirtualCrossoverChannel removed = channels[^1];
            // Invalidate its slots BEFORE detaching the control: any source load
            // still reading a file for this channel captured a revision that
            // Clear() now supersedes, so it refuses to write back or touch the
            // control we are about to dispose (a KeyNotFoundException otherwise).
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
    }

    // Appends a channel pair: a fresh block and a matching empty project entry,
    // so the new channel simply has no sources until the user picks them.
    private void AddChannel()
    {
        if (channels.Count >= MaxChannelCount)
        {
            return;
        }

        var pair = new VirtualCrossoverChannelPairSettings();
        project.Pairs.Add(pair);
        SetChannelCount(channels.Count + 1);
        // Bind the new block to its pair the same way ApplyProjectAsync does.
        VirtualCrossoverChannel added = channels[^1];
        added.Pair = pair;
        added.ActiveRight = project.ActiveSideRight;
        ApplySettingsToControl(added);

        ScheduleSave();
        RedrawAll();
    }

    // Drops the last channel pair and its project entry. Its resolved
    // measurements go with the disposed block; the remaining pairs are untouched.
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

    // Folding a block only changes how much of it the list shows: the flow layout
    // reflows the blocks below it on its own, and no curve, sum or metric depends on
    // it — so this persists the state and stops there, no recompute, no redraw.
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

        // Flipping Mono while the RIGHT side is shown swaps which settings
        // object the control edits (a mono pair always answers with the left
        // side), so the values just read from the control belong to the OLD
        // binding and must not be written through the new one — rebind the
        // control instead.
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
                // The right slot becomes unreachable behind the mono routing;
                // dropping its runtime now means nothing stale can hide there.
                // The right SETTINGS survive, so unchecking restores the side
                // through a normal re-resolve below.
                channel.PhysicalSideState(true).Clear();
            }
            else
            {
                // Back to stereo: the right side re-resolves from its persisted
                // source reference through the usual compatibility validation
                // instead of resurfacing whatever cache the slot last held.
                ReresolveRightSide(channel);
            }

            UpdateSideRadioTexts();
        }

        ScheduleSave();
        RedrawAll();
    }

    // Fire-and-forget with a guard, like LoadProjectSafely: called from a
    // synchronous settings-changed handler.
    private async void ReresolveRightSide(VirtualCrossoverChannel channel)
    {
        try
        {
            channel.PhysicalSideState(true).Clear();
            await ResolveSourceAsync(channel, rightSide: true, showErrors: false);
            // The channel may have been removed (or the panel disposed) while the
            // re-resolve read from disk; ControlFor would then miss the entry.
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
        // Before the write-back below: the toggle has to be carrying the new
        // view's answer, or OnViewChanged would copy the old view's into it.
        ApplySumToggleForView();
        OnViewChanged();
    }

    // Each curve toggle is muted on the views that cannot draw that curve: the
    // Sum exists on the magnitude and phase plots but not among the impulse
    // traces, the sum loss and the target are magnitude-only (a target is a dB
    // shape — the same rule OverlayTargets.SupportsMode applies to overlay
    // targets). Fractional-octave smoothing shapes the frequency-domain curves,
    // so it is dead in the impulse view alone. The Target... button stays live
    // everywhere: it switches to the view its dialog can preview on rather than
    // sitting there greyed.
    private void UpdateViewDependentControls()
    {
        comboBoxSmoothing.Enabled = !radioViewImpulse.Checked;
        // These two wear a fixed plot colour, so the shared helper — which
        // memorizes the colour it mutes — is safe for them.
        Ui.UiStyle.SetTextEnabledLook(
            checkBoxShowSum, !radioViewImpulse.Checked, interactive: true);
        Ui.UiStyle.SetTextEnabledLook(
            checkBoxShowLoss, radioViewMagnitude.Checked, interactive: true);
        UpdateTargetToggleLook();
        // DarkNumericUpDown paints its own disabled state in the palette's muted
        // text, so this one can simply be disabled.
        numericTargetLevel.Enabled = radioViewMagnitude.Checked;
    }

    // A CheckBox WinForms has disabled paints its text in a system grey that
    // reads as near-black on this theme, so the toggle is muted the way
    // UiStyle.SetTextEnabledLook mutes one — kept enabled, coloured by hand,
    // with AutoCheck and TabStop carrying the disabling. Not through that helper
    // itself: it memorizes the colour it muted, and this toggle is recoloured
    // whenever the shared target is, so what came back could be a stale target's
    // colour.
    private void UpdateTargetToggleLook()
    {
        bool magnitude = radioViewMagnitude.Checked;
        checkBoxShowTarget.ForeColor =
            magnitude ? targetToggleColor : Ui.UiPalette.TextMuted;
        checkBoxShowTarget.AutoCheck = magnitude;
        checkBoxShowTarget.TabStop = magnitude;
    }

    // The Sum toggle carries one answer per view (see VirtualCrossoverProjectFile).
    // The impulse view has no sum trace, so it writes nothing and shows the
    // magnitude answer while its toggle is muted.
    private void ApplySumToggleForView()
    {
        bool suppressed = suppressProjectEvents;
        suppressProjectEvents = true;
        try
        {
            checkBoxShowSum.Checked = radioViewPhase.Checked
                ? project.ShowSumCurveOnPhase
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
        else if (radioViewMagnitude.Checked)
        {
            project.ShowSumCurve = checkBoxShowSum.Checked;
        }

        project.ShowLossCurve = checkBoxShowLoss.Checked;
        project.ShowTargetCurve = checkBoxShowTarget.Checked;
        project.TargetLevelDb = (double)numericTargetLevel.Value;
        project.ShowPhaseView = radioViewPhase.Checked;
        project.ShowImpulseView = radioViewImpulse.Checked;
        project.SetSmoothingCode(comboBoxSmoothing.SelectedItem is int value
            ? value
            : 12);
        ScheduleSave();
        RedrawAll();
    }

    // ------------------------------------------------------- settings mapping

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
            // Family first: selecting it repopulates the slope list the slope
            // selection then lands in.
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
            control.AllPassTypeComboBox.SelectedItem = settings.AllPassType;
            control.AllPassFrequencyInput.Value = control.AllPassFrequencyInput
                .ClampValue(settings.AllPassFrequencyHz);
            control.AllPassQInput.Value = control.AllPassQInput.ClampValue(settings.AllPassQ);
            // The four block-wide switches come off the PAIR, so the block keeps
            // showing the same answer whichever side is on screen.
            control.ShowRawCheckBox.Checked = channel.Pair.ShowRawCurve;
            control.ShowProcessedCheckBox.Checked = channel.Pair.ShowProcessedCurve;
            control.BypassCheckBox.Checked = channel.Pair.Bypass;
            control.MonoCheckBox.Checked = channel.Pair.Mono;
            control.Muted = !channel.Pair.Enabled;
            control.Collapsed = channel.Pair.Collapsed;
        });

        UpdateSourceButton(channel);
        UpdatePeqReadouts(channel);
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
        AllPassSpec allPass = control.AllPassStage;
        settings.AllPassType = allPass.Type;
        settings.AllPassFrequencyHz = allPass.FrequencyHz;
        settings.AllPassQ = allPass.Q;
        channel.Pair.ShowRawCurve = control.ShowRawCheckBox.Checked;
        channel.Pair.ShowProcessedCurve = control.ShowProcessedCheckBox.Checked;
        channel.Pair.Enabled = !control.Muted;
        channel.Pair.Bypass = control.BypassCheckBox.Checked;
        channel.Pair.Mono = control.MonoCheckBox.Checked;
    }

    // ---------------------------------------------------------------- sources

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

        // The jump out of the tune: this side's measurement, inspected with the full
        // analysis toolset. Enabled only when the reference actually resolves — a
        // stored path that no longer exists (and no surviving history entry) would
        // otherwise offer a jump to nowhere.
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
            // Re-resolved at click time: the file can vanish (or the history entry
            // be deleted) while the menu is open.
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

        Button sourceButton = ControlFor(channel).SourceButton;
        menu.Show(sourceButton, new Point(0, sourceButton.Height));
    }

    // The source reference in openable form, resolved the same way the silent
    // restore resolves it (LoadSnapshotFromReferenceAsync): the history entry
    // first — it survives file moves — else the file path located through the
    // session-folder search. (null, null) when nothing resolves.
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
        // The CONCRETE slot and settings are captured NOW: the user can flip
        // the L/R selector, toggle Mono (which reroutes SideState) — or
        // import a whole different session — while the file loads below, and
        // the measurement must land in the slot whose Source button was
        // clicked, or nowhere at all. The revision (taken when the load
        // starts) guards the landing: any Clear() of the slot or a newer
        // pick into it refuses this one.
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
        // Same slot/settings/revision capture as ChooseSourceFileAsync: the
        // snapshot load is asynchronous and the L/R selector, the Mono
        // checkbox and session import all stay live meanwhile.
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

    // Whether a source assignment may prompt the user to resolve a sample-rate
    // conflict (an interactive pick) or must decline silently (a background
    // restore cannot ask).
    private enum SourceConflictPolicy
    {
        Prompt,
        RejectSilently
    }

    // The shared source-assignment core for the interactive pickers and the
    // silent restore alike: the revision guard, the loopback-transfer-IR
    // requirement, the sample-rate conflict handling and the runtime-state
    // write. The policy is the only difference — an interactive pick may prompt
    // to clear mismatched sides (and reports a missing transfer IR), while a
    // silent reload cannot ask, so it just leaves the side unresolved. Returns
    // true when the measurement landed; the caller owns the persisted source
    // reference and the UI refresh, which differ between the two paths.
    //
    // targetState is the caller's PRE-AWAIT capture — never re-derived here,
    // where a mid-load Mono toggle would reroute it — and the revision refuses a
    // landing the slot has moved past (cleared by a project import or mono
    // toggle, or superseded by a newer pick).
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

        // A project is locked to one sample rate: mixed rates are refused outright
        // rather than partially supported, because the analysis reads a single
        // shared rate. The compatibility decision scans EVERY resolved side of
        // every pair — the virtual sums of both sides read that one rate.
        List<(VirtualCrossoverChannel Channel, bool RightSide, VirtualCrossoverChannelState State)> others =
            ResolvedSidesExcept(targetState).ToList();
        VirtualCrossoverSourceRules.Decision decision = VirtualCrossoverSourceRules.Evaluate(
            hasTransferIr: true,
            candidateSampleRate: resolved.SampleRate,
            otherResolvedSampleRates: others.Select(item => item.State.SampleRate));
        if (decision == VirtualCrossoverSourceRules.Decision.RejectSampleRateMismatch)
        {
            // A silent reload cannot prompt, so an incompatible source stays
            // unresolved (the button shows the warning glyph); an interactive pick
            // explains why it was refused and how to switch the project's rate.
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

    // The persisted source reference and UI refresh that follow an interactive
    // pick landing in a slot. A silent restore skips both: the reference is
    // already stored and BindProjectAsync refreshes once at the end.
    private void OnSourceAssigned(
        VirtualCrossoverChannel channel,
        VirtualCrossoverChannelSettings settings,
        VirtualCrossoverSourceReference reference)
    {
        reference.ApplyTo(settings);
        UpdateSourceButton(channel);
        UpdateSideRadioTexts();
        ScheduleSave();
        RedrawAll();
    }

    // Every resolved (channel, side) except the given side state; mono pairs
    // expose only their single left-side slot.
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
        UpdateSourceButton(channel);
        UpdateSideRadioTexts();
    }

    // Re-resolves one side's persisted source reference: the history entry
    // first (it survives file moves), then the file path, then that file beside
    // an imported session. A source that no longer exists degrades to an
    // unresolved side instead of failing the project load.
    private async Task ResolveSourceAsync(
        VirtualCrossoverChannel channel, bool rightSide, bool showErrors)
    {
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        if (!settings.HasSource)
        {
            return;
        }

        // The same in-flight guard as the interactive pickers: rapid mono
        // off→on→off leaves several of these resolves airborne at once, and
        // only the latest one — or none, if the slot was cleared after it
        // started — may land. Snapshot loading below is the await that lets
        // the UI act meanwhile.
        int revision = state.BeginSourceLoad();
        pendingSourceLoads++;
        RefreshAutoActionsEnabled();
        try
        {
            (MeasurementHistorySnapshot? snapshot, string? relocatedPath) =
                await LoadSnapshotFromReferenceAsync(settings);
            // The same assignment core as the interactive pickers (the file
            // behind a stored path may have been replaced since the project was
            // saved), but under RejectSilently: an incompatible source — no
            // transfer IR, or a rate that clashes with the other sides — stays
            // unresolved (the button shows the warning glyph) instead of
            // prompting, because a silent reload cannot ask.
            if (snapshot != null &&
                TryAssignSource(
                    state, revision, snapshot, SourceConflictPolicy.RejectSilently) &&
                relocatedPath != null)
            {
                // Only a measurement that LANDED may repoint the channel, the same
                // rule the interactive pickers follow (see OnSourceAssigned). A file
                // found under the search folders can still be refused — no transfer
                // IR, or the wrong sample rate — and pinning it anyway would bury the
                // reference the search itself needs: the stored path always wins once
                // it exists, so the next relink would reopen the refused file instead
                // of looking in the folder the user just pointed at.
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

    // Loads the measurement behind a persisted source reference: the history
    // entry first (it survives file moves), then the file path — and, when that
    // path no longer exists, the same file beside the session file the project
    // was imported from. A null snapshot means nothing resolved and the side stays
    // unresolved instead of failing the load. RelocatedPath is where the file was
    // actually read from when that differs from the stored path — the caller pins
    // it only if the measurement is accepted, because this project becomes the
    // internal autosave right after the import and that copy has no session file
    // beside it to search from a second time.
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

    // The stored path, then the folder the session was imported from, then the
    // folder the user pointed at when relinking. The first call already answers
    // for a path that still exists, so the second only ever runs when nothing
    // resolves without the user's help.
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
        string? name = channel.Settings.DisplayName;
        bool resolved = channel.TransferImpulseResponse != null;
        control.SourceButton.Text = string.IsNullOrWhiteSpace(name)
            ? "Source..."
            : resolved ? name : $"⚠ {name}";
        // The as-measured driver polarity, read from the raw transfer IR (the
        // Invert switch is a separate, virtual stage on top of it).
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

    // -------------------------------------------------------------------- PEQ

    // The PEQ button's action menu. Rebuilt on every click: the enabled states
    // follow channel state (a measurement for the wizard entries, a loaded PEQ for
    // Clear) that changes while the panel is open.
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

        // Both wizard entries need a measurement to show a curve; the choice is what
        // the curve travels through — the DSP chain without its PEQ, or nothing.
        bool hasMeasurement =
            channel.SideState(channel.ActiveRight).TransferImpulseResponse != null;
        // Said HERE, before the trip: a bypassed block draws its raw response on
        // this plot, so the wizard's curve will not be the one on screen. It is
        // still the right curve to tune a PEQ against — the chain the bank will
        // live in — and the item names the exception rather than hiding it.
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

        Button anchor = ControlFor(channel).PeqMenuButton;
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    // Builds the handoff for the active side and hands it to the host. The gate
    // pieces mirror what the magnitude view draws with: the shared template, the
    // active side's pin, and the last redraw's window anchor so an unpinned gate
    // opens exactly where the plot's did.
    private void RequestPeqHandoff(VirtualCrossoverChannel channel, bool withChain)
    {
        if (EditPeqInWizardRequested is not { } requested)
        {
            return;
        }

        MagnitudeGateSnapshot snapshot = magnitudeGate;
        // Only a render that still describes the CURRENT settings may place the
        // window: a delay or crossover edit invalidates the coordinator and queues a
        // new pass, and until it lands this capture belongs to the previous chain —
        // pairing a new chain with an old anchor. Stale means no anchor, and the
        // builder falls back to reading the channel's own front.
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
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                renderAnchor,
                (double)numericTargetLevel.Value,
                (double)numericTargetLevel.Minimum,
                (double)numericTargetLevel.Maximum,
                snapshot.SmoothingInverseOctaves,
                Calibration,
                SelectedCalibrationName(),
                projectGeneration);
        }
        catch (InvalidOperationException)
        {
            // The measurement vanished between opening the menu and choosing — a
            // silent no-op, like a deleted history entry in the source picker.
            return;
        }

        requested(request);
    }

    /// <summary>
    /// Lands a bank edited in the EQ Wizard back on the side it was taken from.
    /// False — and nothing written — when that channel is no longer here (removed,
    /// or replaced wholesale by a project import); the host tells the user and
    /// leaves the wizard open so the tune is not lost.
    /// </summary>
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
                Calibration,
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                (double)numericTargetLevel.Value))
        {
            return false;
        }

        // The level travels back with the bank: it is where the tune was fitted to
        // hang, and the guard above has already established that this panel's own
        // answer is still the one the wizard started from — so writing it cannot
        // overwrite a decision made here meanwhile.
        if (!((double)numericTargetLevel.Value).Equals(targetLevelDb))
        {
            numericTargetLevel.Value = numericTargetLevel.ClampValue(targetLevelDb);
        }

        UpdatePeqReadouts(token.Channel);
        ScheduleSave();
        RedrawAll();
        return true;
    }

    // Writes one channel's bank out through the SAME coordinator, formats and
    // warnings the EQ Wizard exports with — the tune can now be built entirely in
    // these two panels, so this is the door to the hardware and it must not be a
    // second, subtly different exporter.
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
            !ConfirmPeqExportLoss(EqExportWarnings.PreampDropped(target, curve)))
        {
            return;
        }

        // The band the sheet states it was tuned over: this channel's own passband
        // when it has a crossover, the full range when it does not.
        (double minHz, double maxHz) =
            VirtualDspEqHandoff.PassbandFor(settings) ?? (20.0, 20_000.0);
        EqWizardFileResult result = peqExport.Export(
            new EqWizardExportRequest(
                dialog.FileName,
                target,
                curve,
                ProjectSampleRateHz,
                $"Channel {channel.Name} ({side})",
                minHz,
                maxHz,
                // No fit statistics: these bands were not necessarily fitted here,
                // and a sheet is better with the figure absent than invented.
                Stats: null,
                TargetDspQConvention));
        if (!result.Success)
        {
            ShowError("PEQ could not be exported.", result.Exception!.Message);
        }
    }

    // Nothing to say means nothing to ask — the same rule the wizard's export follows.
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

        // No trailing entry, so the index always resolves to a format.
        IEqProfileFormat chosen =
            EqFormatFileDialogs.ResolveFormat(formats, dialog.FilterIndex)!;
        EqualizationCurve curve;
        try
        {
            // An unrecognised file must not reach the channel: the assignment
            // below replaces its bands and preamp outright, so a wrong pick in
            // the file dialog would silently clear the channel's PEQ.
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

    private void UpdatePeqReadouts(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        bool noPeq = settings.PeqBands.Count == 0 && settings.PeqPreampDb == 0;
        string text = noPeq
            ? "No PEQ"
            : $"{settings.PeqSourceName ?? "PEQ"}: {settings.PeqBands.Count} bands, " +
              $"preamp {settings.PeqPreampDb:0.0} dB";
        // The preamp also lands in the block's gain row, which reads out the level the
        // two stages come to together; the block keeps that readout in step with its
        // own gain field, so the preamp is all it needs from here.
        VirtualCrossoverChannelControl control = ControlFor(channel);
        control.PeqPreampDb = settings.PeqPreampDb;
        Label peqInfoLabel = control.PeqInfoLabel;
        peqInfoLabel.Text = text;
        // The label is narrow and clips the file name; the full text lives in the
        // tooltip. Nothing worth hovering when there is no PEQ.
        toolTip.SetToolTip(peqInfoLabel, noPeq ? string.Empty : text);
    }

    // ------------------------------------------------------------------ plots

    private AcousticView CurrentAcousticView() =>
        radioViewImpulse.Checked ? AcousticView.Impulse
        : radioViewPhase.Checked ? AcousticView.Phase
        : AcousticView.Magnitude;

    private DspPlotMode CurrentDspPlotMode() =>
        radioDspPhase.Checked ? DspPlotMode.Phase
        : radioDspGroupDelay.Checked ? DspPlotMode.GroupDelay
        : radioDspCorrelation.Checked ? DspPlotMode.Correlation
        : DspPlotMode.Magnitude;

    // One of the chain-view radios (magnitude / phase / group delay) became
    // checked: retract the cross-container Correlation radio before acting —
    // its own container cannot do it (see the wiring comment).
    private void OnChainDspModeChecked()
    {
        radioDspCorrelation.Checked = false;
        OnDspPlotModeChanged();
    }

    private void OnDspPlotModeChanged()
    {
        comboBoxCorrelationPair.Enabled =
            radioDspCorrelation.Checked && comboBoxCorrelationPair.Items.Count > 0;
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
            checkBoxShowLoss,
            "How many dB the complex sum falls short of the\r\n" +
            "phase-blind magnitude sum (<= 0).\r\n" +
            "0 dB means the channels are perfectly in phase.\r\n" +
            "Tip: invert one channel and tune the delay for the\r\n" +
            "deepest null — flipping polarity back then gives\r\n" +
            "the best summation.");
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
            comboBoxSmoothing,
            "Fractional-octave smoothing of the magnitude curves —\r\n" +
            "and of the curves the Sum loss read-out is measured from,\r\n" +
            "so it still moves those numbers in the Phase and Impulse\r\n" +
            "views, where the drawn traces are not smoothed at all.\r\n" +
            "Psychoacoustic: variable 1/3 to 1/6 octave with extra peak weighting\r\n" +
            "narrower than half its window — narrow interference nulls\r\n" +
            "the ear barely hears drop out, peaks and broad valleys stay.\r\n" +
            "The junction metric numbers stay unsmoothed and honest.");
        toolTip.SetToolTip(
            radioDspGroupDelay,
            "What the lower plot shows for each channel's DSP chain:\r\n" +
            "Magnitude, Phase, or filter Group delay (the crossover/PEQ\r\n" +
            "group delay in ms, excluding the channel's bulk delay).");
        toolTip.SetToolTip(
            radioDspCorrelation,
            "Junction correlation: the selected adjacent pair's band-limited\r\n" +
            "cross-correlation (corr + PHAT; negative lobes = the upper\r\n" +
            "channel inverted) and the PRIOR-FREE acoustic score — the\r\n" +
            "dip-penalized junction loss, honestly re-gated per point — versus\r\n" +
            "an extra delay on the upper channel, in both polarities: the comb\r\n" +
            "of alignment lobes. Auto delay weighs this acoustics TOGETHER\r\n" +
            "with the arrival prior and the lobe/onset/scene gates, so its\r\n" +
            "pick may deliberately sit off this curve's deepest lobe — the\r\n" +
            "gap to the dashed envelope-arrival marker shows that trade.\r\n" +
            "Channels enter with their current delays: 0 ms is the alignment\r\n" +
            "as it stands.");
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
            "Shape the target: preset, tilt, bass and treble shelves,\r\n" +
            "presence, colour and line style, previewed live on this plot\r\n" +
            "(which switches to the Magnitude view, the only one a dB shape\r\n" +
            "means anything on). Saving writes the SAME target the EQ Wizard\r\n" +
            "equalizes towards.");
        toolTip.SetToolTip(
            comboBoxCalibration,
            "Microphone calibration applied to the magnitude curves —\r\n" +
            "and to the curves the Sum loss read-out is measured from,\r\n" +
            "so it still moves those numbers in the Phase and Impulse\r\n" +
            "views, where no calibrated trace is drawn.\r\n" +
            "The measurement is loopback-referenced, so this is\r\n" +
            "optional. The entries are the calibrations configured in\r\n" +
            "Record Settings; a loaded session that carries its own\r\n" +
            "curve adds it here as '(from session)'. The selection is\r\n" +
            "saved into the session as the curve itself, so it travels.");
        toolTip.SetToolTip(
            buttonAutoDelay,
            "Open the Auto delay dialog: align the channels in two\r\n" +
            "stages (band-limited first arrivals, then a phase search\r\n" +
            "that fine-tunes delays and polarity), review the proposed\r\n" +
            "before/after table and apply or discard it. The dialog\r\n" +
            "holds the L/R scene offset and can also balance channel\r\n" +
            "gains (cut-only).\r\n" +
            "With both sides loaded the run is STEREO: the left side\r\n" +
            "aligns first, the right top driver is timed to the left\r\n" +
            "one (honoring the L/R offset), and the right side\r\n" +
            "descends from it — so the stereo image stays put.\r\n" +
            "Set the crossover filters first — the search targets\r\n" +
            "the overlap region around their corner frequencies.");
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
            buttonAutoSetup,
            "Crossover wizard: detect each channel's driver type from\r\n" +
            "its response, confirm the types, and get a starting point —\r\n" +
            "LR24 splits where the responses intersect and cut-only\r\n" +
            "gains that level the channels.\r\n" +
            "Run Auto delay afterward to phase-align the result.");
        toolTip.SetToolTip(
            buttonPhaseGate,
            "Configure the gate for the phase and impulse views: offset\r\n" +
            "and Tukey fades, with an IR preview — cut the window before\r\n" +
            "the first reflection for clean traces.\r\n" +
            "The MAGNITUDE view deliberately ignores these durations: it\r\n" +
            "reads a long fixed steady-state window (what the ear hears,\r\n" +
            "cabin included — and the full depth of a bass EQ band, which\r\n" +
            "a junction-length gate cannot contain). Only the gate's\r\n" +
            "OFFSET carries over, saying where that window opens.\r\n" +
            "Where the gate SITS — its offset and the detrend τ — belongs\r\n" +
            "to the side you are viewing (L or R): their drivers arrive at\r\n" +
            "different times, so fitting one no longer disturbs the other.\r\n" +
            "The Tukey lengths, window mode, detrend mode and FDW cycles\r\n" +
            "are shared, so both sides read at one resolution and method.");
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
            "Render a music file through the current tune and save it\r\n" +
            "as a WAV: the left side's summed response on channel 1,\r\n" +
            "the right side's on channel 2. Microphone calibration can\r\n" +
            "be applied as a linear-phase FIR baked into both sides.\r\n" +
            "Listen through HEADPHONES only: each ear gets its side's\r\n" +
            "measured acoustic path (drivers, cabin, capsule) at the mic\r\n" +
            "position — not a binaural head simulation. Played through\r\n" +
            "the same system it would convolve the car twice.\r\n" +
            "A track at another sample rate is converted to the project's;\r\n" +
            "the measured responses are never resampled.");
        // The per-channel block tooltips are applied in CreateChannel, so every
        // block — including ones added after construction — carries them.
    }

    // ---------------------------------------------------------------- redraw

    private void RedrawAll()
    {
        using var _ = AppProfiler.Zone("VirtualDSP.RedrawAll");
        // Ahead of the suppress guard, and here rather than at each of the several places
        // a source resolves: every one of them ends in a redraw, so this is the single
        // point that cannot be forgotten. The setter no-ops when the rate is unchanged.
        PushProjectSampleRateToChannels();
        if (suppressProjectEvents)
        {
            return;
        }

        RequestRedraw();
    }

    // Starts the redraw loop, or — if one is already running — marks its current
    // pass stale so it repeats once more with the latest settings. Called only on
    // the UI thread, so the flag and the task handle need no synchronization.
    private void RequestRedraw()
    {
        // Every redraw path funnels through here on the UI thread, so this is
        // the one place the magnitude-gate snapshot can be refreshed without
        // the worker-thread builds ever touching live state.
        magnitudeGate = new MagnitudeGateSnapshot(
            CreateVirtualPhaseSettings(
                gateOffsetMs: 0.0,
                PhaseDetrendMode.Off,
                manualDetrendMilliseconds: 0.0) with
            {
                // The magnitude reads the FIXED steady-state window, not the
                // dialog's gate. Two reasons, one per parameter. Mode: FDW cannot
                // hold the summed response — its high-frequency windows are
                // shorter than the channels' arrival spread, so no single window
                // keeps every channel's treble inside the one summed IR, and the
                // drawn Sum and the loss read-out collapse. Length: tonal balance
                // is a steady-state question — a short junction gate cannot even
                // contain a bass EQ band's own ringing, so under it a Q 5 cut at
                // 100 Hz draws at a fraction of its real depth. The dialog's
                // durations, window mode and FDW cycles therefore shape the
                // PHASE and IMPULSE views only; its OFFSET (the pin, or the
                // shared front anchor when unpinned) still says where this
                // window opens.
                WindowMode = PhaseWindowMode.Fixed,
                LeftMs = FrequencyResponseOptions.SteadyStateLeftMs,
                PlateauMs = FrequencyResponseOptions.SteadyStatePlateauMs,
                RightMs = FrequencyResponseOptions.SteadyStateRightMs
            },
            PinnedGateOffsetMs,
            project.PhaseGateFor(!project.ActiveSideRight).OffsetMs,
            comboBoxSmoothing.SelectedItem is int smoothing ? smoothing : 12);

        // Every settings/source/view change invalidates the captured render
        // snapshot, not only side switches. A running FFT may finish, but the
        // coordinator will neither cache nor publish its stale result.
        processingCoordinator.Invalidate();
        if (redrawTask is { IsCompleted: false })
        {
            redrawPending = true;
            return;
        }

        redrawTask = RunRedrawLoopAsync();
        RefreshAutoActionsEnabled();
    }

    // Auto crossover and Auto delay run on the PROCESSED channel set. While a
    // source is still loading or the redraw/processing pass is mid-flight, the
    // curves those searches read are not on screen yet — pressing either then
    // aligns against stale or absent data — so both are disabled until the panel
    // settles. (During a project load the whole tree is already disabled.)
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
        // The audition sums both sides through the coordinator at one revision;
        // starting it mid-redraw would race the invalidation and render nothing.
        buttonAudition.Enabled = !busy;
    }

    // The redraw loop coalesces edits into one trailing pass. Snapshot revision,
    // cancellation and processed-response cache ownership live in the coordinator;
    // this method only applies a current result to OxyPlot.
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
                // A redraw is best-effort: keep the last good frame and let the
                // next change try again rather than tearing down the tool.
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

    // Captures the active channel set on the UI thread. SourceSnapshot owns a
    // write-once copy made when the measurement was loaded, and ChannelSnapshot
    // deep-copies the PEQ values, so the coordinator never reads controls or
    // mutable project settings after this method awaits.
    private async Task<ProcessedRender?> ProcessChannelsAsync()
    {
        // Tracy zones are thread-bound and strictly LIFO, so no zone may span
        // an await: only the synchronous snapshot section is zoned here, and
        // the heavy per-channel DSP is zoned inside the coordinator's worker
        // threads where it actually runs.
        long revision = processingCoordinator.CurrentRevision;
        var snapshots = new List<VirtualCrossoverChannelSnapshot>();
        var bindings = new Dictionary<int, (VirtualCrossoverChannel Channel, OxyColor Color)>();
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
                    : channel.Settings.ToChain();
                snapshots.Add(new VirtualCrossoverChannelSnapshot(
                    i,
                    new ProcessingSlotId(
                        i,
                        !channel.Pair.Mono && channel.ActiveRight),
                    source,
                    state.SampleRate,
                    chain));
                bindings.Add(i, (channel, ChannelColors[i]));
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
            (VirtualCrossoverChannel channel, OxyColor color) = bindings[result.Id];
            processed.Add(new ProcessedChannel(
                channel,
                result.ImpulseResponse,
                result.PeakIndex,
                color,
                result.ValidRange));
        }
        return new ProcessedRender(render.Revision, processed);
    }

    private async Task RedrawMainPlotAsync()
    {
        // The heavy ApplyChain FFTs run off the UI thread; the existing curves stay
        // on screen until the new data is ready, so there is no clear-then-fill
        // flicker during the compute. No Tracy zone spans the awaits (zones are
        // per-thread LIFO); the synchronous frame build at the end carries one.
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

        // The correlation view of the lower plot reads the same processed
        // snapshot the acoustic plot draws; the redraw loop calls
        // RedrawDspPlot right after this method, so the capture is fresh.
        lastProcessedRender = render;

        // The stereo Δ block and the opposite-side sum read BOTH sides'
        // processed responses; their caches make an unchanged configuration
        // free. Same staleness rule as above.
        List<VirtualCrossoverMetric.StereoDelta> stereoDeltas =
            await metrics.ComputeStereoDeltasAsync(channels, revision);
        // The side sum comes from metrics (shared coordinator cache); the CURVE
        // is built here so it windows through the OPPOSITE side's gate
        // placement — the active side's pin must not gate the other side.
        AnalysisCurve? oppositeSum = null;
        if (checkBoxShowSum.Checked && radioViewMagnitude.Checked)
        {
            VirtualCrossoverSideSum? oppositeSide = await metrics.ComputeSideSumAsync(
                channels, !project.ActiveSideRight, revision, minimumChannels: 2);
            if (oppositeSide != null)
            {
                oppositeSum = BuildOppositeMagnitudeCurve(oppositeSide);
            }
        }
        if (mainPlotView.IsDisposed || !processingCoordinator.IsCurrent(revision))
        {
            return;
        }

        // The processed magnitudes and the complex sum feed both the drawn
        // curves and the sum-loss metric, so they are built once here. This is
        // the synchronous UI-thread part of the frame (curve building — the
        // phase view's gated FFTs included — metric update, OxyPlot draw), so
        // it takes the redraw zone. The steps carry a zone each: this stretch
        // dominates the frame, and the split says which one to answer for.
        using var _ = AppProfiler.Zone("VirtualDSP.RedrawMainPlot");
        List<AnalysisCurve>? magnitudes;
        AnalysisCurve? sumCurve;
        List<SignalPoint>? lossCurve;
        using (AppProfiler.Zone("VirtualDSP.BuildCurves"))
        {
            (magnitudes, sumCurve, lossCurve) = metrics.BuildCurves(
                processed, magnitudeGate.SmoothingInverseOctaves);
        }

        using (AppProfiler.Zone("VirtualDSP.UpdateMetric"))
        {
            UpdateMetric(processed, lossCurve, stereoDeltas);
        }

        using (AppProfiler.Zone("VirtualDSP.UpdateWarnings"))
        {
            UpdateWarnings(processed);
        }

        // Split from the draw on purpose: building the curves (the phase view's
        // gated FFTs) and handing them to OxyPlot are different suspects, and as
        // one expression they were indistinguishable.
        AcousticRender acousticRender;
        using (AppProfiler.Zone("VirtualDSP.BuildAcousticRender"))
        {
            acousticRender = BuildAcousticRender(
                processed, magnitudes, sumCurve, lossCurve, oppositeSum);
        }

        using (AppProfiler.Zone("VirtualDSP.AcousticPlotDraw"))
        {
            acousticPlot.Draw(acousticRender);
        }
    }

    // Assembles the ready-to-draw frame for the active view. While a session
    // loads, interim redraws (the calibration combo refresh, etc.) run before the
    // sources resolve, so processed is empty then; keep the loading note instead
    // of flashing the "no sources" hint.
    private AcousticRender BuildAcousticRender(
        List<ProcessedChannel> processed,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve,
        List<SignalPoint>? lossCurve,
        AnalysisCurve? oppositeSum)
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
            return new AcousticRender(hint, BuildPhaseCurves(processed), null);
        }
        if (radioViewImpulse.Checked)
        {
            return new AcousticRender(hint, [], BuildImpulseRender(processed));
        }

        return new AcousticRender(
            hint,
            BuildMagnitudeCurves(processed, magnitudes, sumCurve, lossCurve, oppositeSum),
            null);
    }

    // The target travels with the session. It is handed to the HOST rather than
    // kept here, because the app aims at one target: the EQ Wizard owns and
    // persists it, and this panel gets it straight back through SetTargetCurve.
    // A session written before targets were stored carries none — then the
    // current target stays, and this session starts carrying it.
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

    // The frequency grid the target shape is drawn on. A target is parametric
    // over frequency, not a measurement, so it spans the audio band on its own
    // grid instead of borrowing whatever the loaded channels happen to cover.
    private const double TargetGridLowHz = 20;
    private const double TargetGridHighHz = 20_000;
    private const int TargetGridPoints = 512;

    // The EQ target as an acoustic curve: the shared shape (relative dB) hung at
    // the level this session set. The level is asked for rather than fitted to
    // the sum because Virtual DSP curves are transfer-function dB with no
    // absolute reference — there is no level here that a fit could be honest
    // about, so the one the user reads the sum at is the one that counts.
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

    // The same isolated target dialog the EQ Wizard opens (no source picker, no
    // overlay side effects), previewing on THIS plot. Cancel puts back what was
    // there; Save hands the curve to the host, and that hand-off is what carries
    // the edit to the wizard.
    private void OpenTargetSettings()
    {
        if (targetCurve is not { } before)
        {
            return;
        }

        // Settings for an invisible curve are settings for nothing, so opening
        // the dialog puts the target on screen: the Magnitude view, the only one
        // a dB shape means anything on, with the curve shown. Both stay that way
        // afterwards — the view radios and the checkbox are right there.
        radioViewMagnitude.Checked = true;
        checkBoxShowTarget.Checked = true;
        // Opened as the EQ Wizard's dialog, not as this tool's: the mode is what
        // decides which smoothing vocabulary the dialog offers, and one target
        // edited from two places must not come back different depending on which
        // button opened it.
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
        // Save is where the session learns about it — see ApplyTargetLocally.
        StoreTargetInProject(edited);
        TargetCurveChanged?.Invoke(edited);
    }

    // The dialog's live preview. It reports every field except the preset (which
    // it names only on Save), so the current preset rides through untouched —
    // nothing drawn here reads it, and nothing stores it either.
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

    // Memory and plot only, deliberately NOT the session: this runs on every
    // twitch of the dialog's live preview, and the autosave timer keeps ticking
    // inside a modal dialog's message loop — a couple of seconds spent dragging
    // a shelf would write an uncommitted preview to disk, to be found by the
    // next launch if the app never got to Cancel. The preview does not even
    // carry a preset, so what landed there would be the old preset's name over
    // the new shape. Save stores; Cancel has nothing to undo.
    private void ApplyTargetLocally(EqTargetCurve value)
    {
        targetCurve = value;
        targetToggleColor = value.Color;
        UpdateTargetToggleLook();
        RedrawAll();
    }

    // The session stores the target it was tuned against, shape and all. Nothing
    // is written before the project has loaded: the host pushes the app's target
    // in while the form is built, long before this tool is first opened, and
    // storing it then would schedule a save of the default project over the real
    // one on disk.
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
        AnalysisCurve? oppositeSumCurve)
    {
        // The processed curves arrive prebuilt from BuildCurves, but a shown RAW
        // curve is spectrum-built right here, one channel after another.
        using var _ = AppProfiler.Zone("VirtualDSP.BuildMagnitudeCurves");
        var curves = new List<AcousticCurve>();
        // First, so the measured curves and the sum read on top of the reference
        // rather than under it.
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
                    item.Channel.SampleRate);
                curves.Add(new AcousticCurve(
                    $"{item.Channel.Name} raw",
                    raw.Points,
                    OxyColor.FromAColor(90, item.Color),
                    1.2,
                    LineStyle.Solid));
            }

            if (item.Channel.Pair.ShowProcessedCurve)
            {
                AnalysisCurve curve = magnitudes != null
                    ? magnitudes[i]
                    : BuildMagnitudeCurve(
                        item.ImpulseResponse,
                        // No shared anchor to follow (BuildCurves yields no
                        // metric below two channels), so the channel opens on
                        // its own front — the same rule, one channel wide.
                        ProcessedChannels.StartAnchorIndex(
                            item.ImpulseResponse,
                            item.PeakIndex,
                            item.Channel.SampleRate,
                            item.ValidRange),
                        item.Channel.SampleRate).Display;
                curves.Add(new AcousticCurve(
                    item.Channel.Name, curve.Points, item.Color, 1.8, LineStyle.Solid));
            }
        }

        if (magnitudes == null || sumCurve == null)
        {
            return curves;
        }

        if (checkBoxShowSum.Checked)
        {
            curves.Add(new AcousticCurve(
                "Sum", sumCurve.Points, SumColor, 2.4, LineStyle.Solid));
            if (oppositeSumCurve != null)
            {
                // The other side's sum, dashed and translucent: the two tunes
                // compare at a glance without flipping the L/R selector.
                curves.Add(new AcousticCurve(
                    $"Sum {(project.ActiveSideRight ? "L" : "R")}",
                    oppositeSumCurve.Points,
                    OxyColor.FromAColor(110, SumColor),
                    1.8,
                    LineStyle.Dash));
            }
        }

        if (checkBoxShowLoss.Checked && lossCurve != null)
        {
            // The signed dB gap between the complex sum and the phase-blind
            // magnitude sum of the processed channels (<= 0 by the triangle
            // inequality), built once in BuildCurves out of the UNSMOOTHED
            // magnitudes and smoothed as a ratio afterwards — the very list the
            // read-out averages, so the drawn curve and the measured loss cannot
            // drift apart.
            curves.Add(new AcousticCurve(
                "Sum loss", lossCurve, LossColor, 1.8, LineStyle.Dash));
        }

        return curves;
    }

    // ------------------------------------------------- metric and auto delay

    private void UpdateMetric(
        List<ProcessedChannel> processed,
        List<SignalPoint>? lossCurve,
        IReadOnlyList<VirtualCrossoverMetric.StereoDelta>? stereoDeltas = null)
    {
        // The read-out lives in the host's right-side panel (where overlays sit in
        // analysis modes), as a compact per-junction column with the full banded
        // breakdown on hover. The stereo Δ block (final L−R envelope arrival
        // difference per pair) appends below the sum-loss column.
        // Zoned apart from the formatting and the host callback below it: the
        // per-junction banded analysis is the part with real work in it.
        List<VirtualCrossoverMetric.Entry> entries;
        using (AppProfiler.Zone("VirtualDSP.BuildEntries"))
        {
            entries = metrics.BuildEntries(processed, lossCurve);
        }

        // The junction phase block is informative only: it renders alongside
        // the sum loss but feeds nothing back into the alignment engine.
        List<VirtualCrossoverMetric.PhaseEntry> phaseEntries;
        using (AppProfiler.Zone("VirtualDSP.BuildPhaseEntries"))
        {
            phaseEntries = metrics.BuildPhaseEntries(processed);
        }

        string compact = VirtualCrossoverMetric.FormatCompact(entries);
        string detail = entries.Count > 0 ? VirtualCrossoverMetric.FormatDetail(entries) : string.Empty;
        if (phaseEntries.Count > 0)
        {
            compact += "\r\n\r\n" +
                VirtualCrossoverMetric.FormatPhaseCompact(phaseEntries);
            detail += (detail.Length > 0 ? "\r\n\r\n" : string.Empty) +
                VirtualCrossoverMetric.FormatPhaseDetail(phaseEntries);
        }
        if (stereoDeltas is { Count: > 0 })
        {
            compact += "\r\n\r\n" +
                VirtualCrossoverMetric.FormatStereoDeltasCompact(stereoDeltas);
            detail += (detail.Length > 0 ? "\r\n\r\n" : string.Empty) +
                VirtualCrossoverMetric.FormatStereoDeltasDetail(stereoDeltas);
        }

        MetricChanged?.Invoke(compact, detail);
    }

    // One warning line for the host to show, and only one: the gate placement
    // comes first because it decides whether the curves describe the channels
    // at all — a window that opens after the drivers arrive turns every one of
    // them into its own reverberant tail, and the crossover spread below is
    // read off the applied delays, which stay true meanwhile.
    private void UpdateWarnings(List<ProcessedChannel> processed)
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

        UpdateCrossoverWarning(processed);
    }

    private void ShowWarning(string text, string detail, Color color) =>
        WarningChanged?.Invoke(text, detail, color);

    // The colour rides along with the empty text so the host needs one handler
    // and no separate "clear" call; with nothing to say, it is never painted.
    private void HideWarning() =>
        WarningChanged?.Invoke(string.Empty, string.Empty, CrossoverWarningColor);

    // Amber for the gate: it says the view cannot be read yet, not that the
    // tuning is wrong. The crossover spread keeps the red it always had.
    private static readonly Color GateWarningColor = Color.FromArgb(230, 184, 0);
    private static readonly Color CrossoverWarningColor = Color.FromArgb(235, 110, 95);

    // The spread of alignment delays, above which the setup is flagged. A driver
    // whose crossover has pathological group delay (a narrow or steep low-
    // frequency band-pass) arrives so late that Auto delay must push every other
    // driver out by this much to match it — a spread this large is the symptom.
    private const double CrossoverGroupDelayWarningMs = 15.0;

    // Warns, live, when the alignment delays span more than the threshold: the
    // latest driver (the one the others are delayed to catch up to) lags by that
    // much. This reads the applied delays directly, so it exactly mirrors what
    // Auto delay produced — no group-delay proxy that measures the wrong point
    // (a narrow low-frequency band-pass peaks late in its own band, and only its
    // arrival across the whole overlap, i.e. the alignment delay, tells the
    // truth). Bypassed channels carry the raw signal and are excluded.
    private void UpdateCrossoverWarning(List<ProcessedChannel> processed)
    {
        List<ProcessedChannel> active = processed
            .Where(item => !item.Channel.Pair.Bypass)
            .ToList();
        if (active.Count < 2)
        {
            HideWarning();
            return;
        }

        // The latest driver holds the smallest delay (everyone else is delayed
        // toward it); the spread is how far ahead the earliest driver sits.
        ProcessedChannel latest = active.MinBy(item => item.Channel.Settings.DelayMs)!;
        double earliestDelay = active.Max(item => item.Channel.Settings.DelayMs);
        double spread = earliestDelay - latest.Channel.Settings.DelayMs;
        if (spread <= CrossoverGroupDelayWarningMs)
        {
            HideWarning();
            return;
        }

        string name = latest.Channel.Name;
        ShowWarning(
            $"⚠ {name} lags the others by ~{spread:0} ms — check its crossover.",
            $"{name} arrives ~{spread:0} ms after the other drivers, so Auto delay pushes " +
            "them out by that much to match it.\r\n\r\n" +
            "This is usually excessive crossover group delay — a narrow or steep low-frequency " +
            "band-pass. Reduce its slope or widen its band to bring the alignment delays down.",
            CrossoverWarningColor);
    }

    // The two alignment stages, their tuning constants and the selection
    // tie-breaks live in AutoAlignmentEngine / AlignmentSelection
    // (Resonalyze.Dsp), where they are unit-tested. Previous Auto/manual delay
    // and polarity settings are ignored: the command recomputes an absolute
    // proposal from the current sources, crossover filters, gains and PEQ
    // every time.
    private async void AutoAlignDelay()
    {
        // Stereo whenever the data allows it: some non-mono pair has BOTH
        // sides resolved (the highest such pair becomes the L/R bridge) and
        // the left side can hold its own walk. Otherwise the classic
        // single-side run on whatever side is displayed.
        (List<VirtualCrossoverSideAlignmentChannel> leftSide, List<VirtualCrossoverSideAlignmentChannel> rightSide) =
            CollectStereoSides();
        VirtualCrossoverSideAlignmentChannel? bridgeRight = rightSide
            .Where(item => item.RightSide &&
                leftSide.Any(left =>
                    left.Runtime == item.Runtime && !left.RightSide))
            .OrderBy(item => VirtualCrossoverJunctions.BandCenterHz(item.Settings))
            .LastOrDefault();
        if (bridgeRight != null && leftSide.Count >= 2)
        {
            await AutoAlignStereoAsync(leftSide, rightSide, bridgeRight);
            return;
        }

        await AutoAlignSingleSideAsync();
    }

    // The single-side Auto delay command: participant validation up front,
    // then the modal Auto delay dialog. The proposal (delays, polarities and
    // optionally gains) is computed by the dialog's Run and written only on
    // its Apply — Discard leaves every channel setting as it was. The
    // dialog's modality is also what keeps the channel configuration stable
    // under the background compute.
    private async Task AutoAlignSingleSideAsync()
    {
        // Cheap participant snapshot: enabled channels with a resolved
        // measurement. No DSP runs here — the shared crop and every ApplyChain
        // happen later, off the UI thread, inside ComputeAutoAlignment's
        // AlignmentReprocessor.
        List<VirtualCrossoverChannel> participants = channels
            .Where(channel =>
                channel.Pair.Enabled && channel.TransferImpulseResponse != null)
            .ToList();
        if (participants.Count < 2)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        // A bypassed channel processes through the identity chain, so the
        // engine's delay/polarity overrides would not move it — yet it would
        // still take part in the junction walk (even as the settled neighbor
        // or the reference) and receive a delay that bypass silently ignores
        // now and applies later, once bypass is switched off. Refuse the run
        // instead of computing an alignment that is wrong on both counts.
        List<VirtualCrossoverChannel> bypassed = participants
            .Where(channel => channel.Pair.Bypass)
            .ToList();
        if (bypassed.Count > 0)
        {
            ShowError(
                "Auto delay cannot run with bypassed channels.",
                "Bypass feeds the raw measured signal, so the computed delays " +
                "and polarities would not apply to: " +
                string.Join(", ", bypassed.Select(channel => channel.Name)) +
                ".\r\n\r\nDisable Bypass on every participating channel " +
                "(or mute the channel to exclude it) and run Auto delay again.");
            return;
        }

        if (RefuseOnMisplacedGate("Auto delay"))
        {
            return;
        }

        // Without crossovers the search falls back to a broad midband window and
        // the result will shift once the filters are configured — the alignment
        // only matters (and is only well-defined) in the overlap region.
        bool anyCrossover = participants.Any(
            channel => channel.Settings.CrossoverKind != CrossoverKind.Off);
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
                return;
            }
        }

        (double minHz, double maxHz) = VirtualCrossoverJunctions.GetCrossoverWindow(
            participants.Select(channel => channel.Settings));

        using var dialog = new VirtualCrossoverAutoDelayDialog();
        dialog.Init(
            stereo: false,
            project.StereoSceneOffsetMagnitudeMs,
            project.StereoRightHandDrive,
            Math.Abs(project.StereoLevelDifferenceDb),
            request => RunSingleSideProposalAsync(
                participants, minHz, maxHz, request));
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } result ||
            IsDisposed)
        {
            return;
        }

        await ApplyConfirmedAutoDelayAsync(result);
    }

    // Compute errors surface inside the dialog; here the confirmed proposal
    // is COMMITTED first and the outcome metric is appended afterwards as a
    // separate best-effort stage (also guarding the async-void caller from
    // an unhandled exception after the await). A metric failure after the
    // settings are already written must not read as a failed Apply — the
    // user would naturally re-apply and only add confusion.
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

    // Computes the single-side proposal on a background thread: the alignment
    // cascade, then (when asked) the cut-only gain balance from the run's
    // final snapshots. Board levelling only — a single side has no L/R
    // relation, so neither the scene offset nor the level difference plays a
    // part here.
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
            AlignmentReprocessor reprocessor =
                ComputeAutoAlignment(participants, alignment, decisions, log);
            // The "before" snapshots carry the CURRENT delays and polarities —
            // the alignment itself deliberately ignores them, so they exist
            // only for the report's before/after sum-loss forecast.
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
        // The diagnostic trace is written already at the proposal stage, so a
        // discarded (or failed-looking) run can still be shared and analyzed;
        // Apply rewrites it with the results and the outcome metric appended.
        WriteAlignmentLog(log.ToString());
        return new AutoDelayRunResult(outcomes, Stereo: false, request, report, log);
    }

    // Bridges the run's channels to the dsp GainBalanceEngine: bands from the
    // crossover corners, levels from the run's FINAL snapshots (the current
    // gain is baked into the chain and subtracted back out by the engine, so
    // the proposal is absolute, not incremental). Runs on the background
    // thread, reusing the reprocessor's per-channel FFT cache.
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
                    item.Settings.CrossoverKind != CrossoverKind.Off,
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

    // The report's headline figure for one side: the same averaged summation
    // loss the metric read-out shows, predicted from the run's snapshots for
    // the CURRENT settings and for the proposal. Proposed gain changes enter
    // as spectrum scales — the reprocessor's chains still carry the current
    // gains. Null when the side cannot form a sum (fewer than two channels).
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

    // Assembles the report rows: the current settings as "before", the engine
    // override (and gain proposal, when present) as "after", with the
    // decisions' confidence attached. Pure shaping — nothing is written.
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

    // Writes the CONFIRMED proposal into the channels and their controls,
    // persists and redraws — the transactional part of Apply, synchronous so
    // it either fully lands or fails before anything is half-written.
    // Reached only through the dialog's Apply — Discard never gets here, so
    // the channels keep their previous settings. The diagnostic log is
    // rewritten with the results immediately: a later metric failure must
    // not lose them.
    private void CommitAutoDelayResult(AutoDelayRunResult result)
    {
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
            // The inputs the proposal was computed with become the persisted
            // values only now, so a discarded experiment does not overwrite
            // them. Both figures are stored with the layout in their signs
            // (the scene offset via SetStereoScene, the tilt via the L-R
            // convention LevelDifferenceDb restates), keeping the file
            // readable — and safely resavable — by older builds.
            project.SetStereoScene(
                result.Request.SceneOffsetMs, result.Request.RightHandDrive);
            project.StereoLevelDifferenceDb = result.Request.LevelDifferenceDb;
        }

        ScheduleSave();
        RedrawAll();
        WriteAlignmentLog(result.Log.ToString());
    }

    // The best-effort epilogue of Apply: recompute the metric from the
    // just-applied settings and close the diagnostic log with it.
    private async Task AppendOutcomeMetricAsync(AutoDelayRunResult result)
    {
        // RedrawAll pushes the read-out asynchronously (the ApplyChain FFTs run off
        // the UI thread), so recompute the metric synchronously from the just-
        // applied settings so the log ends with this run's true outcome. The
        // side label is captured BEFORE the await: the panel is live again
        // after the modal closed, and a side switch mid-computation would
        // otherwise caption the snapshot with the other side's name.
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

    // The measured records may carry a playback-crosstalk click at one fixed
    // early sample (an electrical copy of the playback, ahead of any acoustic
    // arrival — seen in every record of the same session on the field data).
    // Field-measured effect on the search: sub-sample GCC-PHAT bias on most
    // configs and a wrong solution branch on gentle slopes. Head-gate every
    // record the detector convicts before the search and name it in the log.
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

    // Bridges the panel's channel model to the dsp AutoAlignmentEngine (where
    // the FFT-heavy alignment stages live, unit-tested): snapshots + junctions
    // in, an override map (plus the per-channel decisions for the report) out.
    // Runs on a background thread; the AlignmentReprocessor owns the run-scoped
    // FFT cache, so between consecutive junction searches only the one or two
    // channels that changed their overrides are re-FFT'd, and the shared
    // UI-thread coordinator cache is never touched. Returned so the gain stage
    // can reuse the same cache for the final snapshots.
    private AlignmentReprocessor ComputeAutoAlignment(
        List<VirtualCrossoverChannel> participants,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        System.Text.StringBuilder log)
    {
        // Order along the spectrum by band center; adjacent drivers form the
        // junctions the search walks (the same ordering and pair bands the metric
        // read-out reads, straight from VirtualCrossoverJunctions).
        List<VirtualCrossoverChannel> ordered = participants
            .OrderBy(channel => VirtualCrossoverJunctions.BandCenterHz(channel.Settings))
            .ToList();

        // Same shared direct-sound crop + parallel cache-miss processing as the
        // stereo run: identical final delays at a fraction of the FFT cost,
        // because every search stage reads only the gated direct sound. The crop
        // and every ApplyChain first run HERE, on the background thread.
        var reprocessor = new AlignmentReprocessor(
            CleanCrosstalkHeads(
                ordered.Select(channel => new AlignmentReprocessInput(
                    channel,
                    channel.TransferImpulseResponse!,
                    channel.SampleRate,
                    channel.Settings.ToChain())).ToList(),
                log));

        IReadOnlyList<AlignmentSnapshot> initial = reprocessor.Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        var snapshots = ordered
            .Select((channel, i) => (channel, snapshot: initial[i]))
            .ToDictionary(item => item.channel, item => item.snapshot);
        var junctions = new List<AlignmentJunction>();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                ordered[i].Settings, ordered[i + 1].Settings);
            (double bandLowHz, double bandHighHz) =
                VirtualCrossoverJunctions.OverlapBand(pairHz);
            junctions.Add(new AlignmentJunction(
                snapshots[ordered[i]], snapshots[ordered[i + 1]],
                pairHz, bandLowHz, bandHighHz));
        }

        AutoAlignmentEngine.Compute(
            ordered.Select(channel => snapshots[channel]).ToList(),
            junctions,
            reprocessor.Reprocess,
            alignment,
            log,
            decisions);
        return reprocessor;
    }

    // The per-side participants of a stereo Auto delay run: every enabled
    // channel side with a resolved measurement. A mono pair contributes ONE
    // instance (its left side), shared by both lists — the engine tunes it in
    // the left pass and treats it as fixed on the right.
    private (List<VirtualCrossoverSideAlignmentChannel> Left, List<VirtualCrossoverSideAlignmentChannel> Right)
        CollectStereoSides()
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

    // The stereo Auto delay: the driver's side first (left on LHD, right on
    // RHD), then the L/R bridge at the top pair honoring the scene offset,
    // then the far side's descent — the cascade itself lives in
    // AutoAlignmentEngine.ComputeStereo (dsp, unit-tested on synthetic
    // systems and real car measurements), fed a mirrored plan for RHD.
    private async Task AutoAlignStereoAsync(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        VirtualCrossoverSideAlignmentChannel bridgeRight)
    {
        List<VirtualCrossoverSideAlignmentChannel> union = leftSide.Concat(rightSide)
            .Distinct()
            .ToList();

        // Same reasoning as the single-side run: a bypassed channel processes
        // through the identity chain, so the computed delay would silently not
        // apply — refuse instead of proposing a wrong alignment. Bypass belongs
        // to the block, so it takes both of its sides out at once.
        List<VirtualCrossoverSideAlignmentChannel> bypassed = union
            .Where(item => item.Runtime.Pair.Bypass)
            .ToList();
        if (bypassed.Count > 0)
        {
            ShowError(
                "Auto delay cannot run with bypassed channels.",
                "Bypass feeds the raw measured signal, so the computed delays " +
                "and polarities would not apply to: " +
                string.Join(", ", bypassed.Select(item => item.Name)) +
                ".\r\n\r\nDisable Bypass on every participating channel " +
                "(or mute the channel to exclude it) and run Auto delay again.");
            return;
        }

        // The verdict describes the side on screen; the detail text says so and
        // asks for the other one to be checked after switching, because only
        // the shown side's channels have been processed to judge it against.
        if (RefuseOnMisplacedGate("Auto delay"))
        {
            return;
        }

        bool anyCrossover = union.Any(
            item => item.Settings.CrossoverKind != CrossoverKind.Off);
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
                return;
            }
        }

        VirtualCrossoverSideAlignmentChannel bridgeLeft = leftSide.First(
            item => item.Runtime == bridgeRight.Runtime && !item.RightSide);
        // The two sides carry independent crossover settings, so the bridge
        // band is the INTERSECTION of their playing bands: measured in one
        // side's exclusive range, the arrival would time signal the other
        // side does not even reproduce. No usable overlap → refuse with the
        // reason instead of bridging on noise.
        (double leftBandLowHz, double leftBandHighHz) =
            VirtualCrossoverJunctions.GetChannelBand(bridgeLeft.Settings);
        (double rightBandLowHz, double rightBandHighHz) =
            VirtualCrossoverJunctions.GetChannelBand(bridgeRight.Settings);
        double bridgeBandLowHz = Math.Max(leftBandLowHz, rightBandLowHz);
        double bridgeBandHighHz = Math.Min(leftBandHighHz, rightBandHighHz);
        if (bridgeBandHighHz <
            bridgeBandLowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
        {
            ShowError(
                "The stereo bridge has no usable shared band.",
                $"The top pair's crossover bands barely overlap: " +
                $"{bridgeLeft.Name} plays {leftBandLowHz:0}-{leftBandHighHz:0} Hz, " +
                $"{bridgeRight.Name} plays {rightBandLowHz:0}-{rightBandHighHz:0} Hz. " +
                "Align the pair's crossover settings so the sides share at " +
                "least a third of an octave and run Auto delay again.");
            return;
        }

        using var dialog = new VirtualCrossoverAutoDelayDialog();
        // The dialog edits both tuning figures as layout-neutral magnitudes;
        // the project stores each with the layout in its sign (the scene
        // offset's wire format, the gain engine's L-R convention), so older
        // builds read the same file — hence the magnitudes here and the
        // layout-signed write-back in CommitAutoDelayResult.
        dialog.Init(
            stereo: true,
            project.StereoSceneOffsetMagnitudeMs,
            project.StereoRightHandDrive,
            Math.Abs(project.StereoLevelDifferenceDb),
            request => RunStereoProposalAsync(
                leftSide, rightSide, union, bridgeLeft, bridgeRight,
                bridgeBandLowHz, bridgeBandHighHz, request),
            DescribeLeftRightPolarityMismatch(leftSide, rightSide));
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } result ||
            IsDisposed)
        {
            return;
        }

        await ApplyConfirmedAutoDelayAsync(result);
    }

    // A launch-time red heads-up for the Auto delay dialog when a driver's LEFT
    // and RIGHT measured polarities disagree — one side's impulse response
    // reads inverted relative to the other, typically a swapped speaker wire.
    // Read from the raw transfer IRs (the same figure the channel's "IR:"
    // badge shows), so it reflects the MEASUREMENT, not the virtual Invert
    // switch: the alignment can mask the fault by inverting one side, but the
    // physical wiring stays wrong. Null when every measured pair agrees.
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

    // The dialog status line for a set of drivers whose L/R measured polarities
    // disagree; null (no warning) when the set is empty.
    internal static string? FormatPolarityMismatchWarning(
        IReadOnlyList<string> mismatchedDrivers) =>
        mismatchedDrivers.Count == 0
            ? null
            : $"⚠ L/R polarity mismatch on {string.Join(", ", mismatchedDrivers)} — " +
              "one side measured inverted (check wiring).";

    // Computes the stereo proposal on a background thread: the alignment
    // cascade, then (when asked) the cut-only gain balance from the run's
    // final snapshots — right channels judged against their left peers, tilted
    // by the L-R level difference the tuner entered.
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
            AlignmentReprocessor reprocessor = ComputeStereoAlignment(
                leftSide, rightSide, union, bridgeLeft, bridgeRight,
                bridgeBandLowHz, bridgeBandHighHz, request.SceneOffsetMs,
                request.RightHandDrive, engineAlignment, decisions, log);
            // The "before" snapshots carry the CURRENT delays and polarities —
            // the alignment itself deliberately ignores them, so they exist
            // only for the report's before/after sum-loss forecast.
            IReadOnlyList<AlignmentSnapshot> beforeSnapshots =
                reprocessor.Reprocess(union.ToDictionary(
                    side => (IAlignmentChannel)side,
                    side => new AlignmentOverride(
                        side.Settings.DelayMs, side.Settings.InvertPolarity)));
            if (request.AdjustGains)
            {
                // request.LevelDifferenceDb: the near-side cut restated in
                // the gain engine's signed L-R convention per the layout.
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

        // The report groups the two sides of each block together (A L, A R,
        // B L …) instead of the union's all-left-then-all-right walk order.
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
        // Written already at the proposal stage, so a discarded run can still
        // be shared and analyzed; Apply rewrites it with the outcome metric.
        WriteAlignmentLog(log.ToString());
        return new AutoDelayRunResult(outcomes, Stereo: true, request, report, log);
    }

    // Bridges the pair/side model to the stereo engine on a background thread,
    // sharing the same AlignmentReprocessor (run-scoped FFT cache) as the
    // single-side run.
    private AlignmentReprocessor ComputeStereoAlignment(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        List<VirtualCrossoverSideAlignmentChannel> union,
        VirtualCrossoverSideAlignmentChannel bridgeLeft,
        VirtualCrossoverSideAlignmentChannel bridgeRight,
        double bridgeBandLowHz,
        double bridgeBandHighHz,
        double sceneOffsetMs,
        bool rightHandDrive,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        System.Text.StringBuilder log)
    {
        // The whole search runs on a shared direct-sound crop of the measured
        // IRs: the engine only reads the gated direct sound and band-limited
        // arrivals, so the final delays are identical to a full-length run
        // (validated on real measurements) while every FFT in the cascade
        // shrinks from the capture length to the crop.
        var reprocessor = new AlignmentReprocessor(
            CleanCrosstalkHeads(
                union.Select(side => new AlignmentReprocessInput(
                    side,
                    side.State.TransferImpulseResponse!,
                    side.State.SampleRate,
                    side.Settings.ToChain())).ToList(),
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

        // The engine's plan is written in reference/far ROLES: plan-left is
        // the driver's side the cascade settles first, plan-right the far
        // side fitted to it, and a positive scene offset makes the far side
        // lead. LHD maps the cabin sides onto the roles directly. RHD hands
        // the plan MIRRORED — the right side anchors, the left one is fitted
        // — so the same non-negative offset makes the left side lead (the
        // right lags by it), the dash-center image for a right-seated driver.
        // The L/R pair links (the shared playing band of each stereo pair)
        // aim the descent's gentle prior at the cross-side-consistent delay —
        // the same Δ the metric panel verifies afterwards; their first member
        // is the settled reference-side channel, mirrored alike.
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
            // The link's band must satisfy the arrival analysis' own
            // admission rule — the band is no longer silently widened for a
            // too-narrow intersection, so such a link could never measure.
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
                union.Where(side => side.Runtime.Pair.Mono)
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
            decisions);
        return reprocessor;
    }

    // A diagnostic trace of the last Auto delay run (pair bands, arrivals,
    // deltas, fine results), for sharing when an alignment looks wrong. Best
    // effort: a failed write must never break the alignment itself.
    private static void WriteAlignmentLog(string text)
    {
        try
        {
            AtomicFile.WriteAllText(
                ApplicationDataPaths.Current.VirtualDspAlignmentLogFile, text);
        }
        catch
        {
            // Diagnostics only.
        }
    }

    // The gate-driven magnitude shared by the processed channels, the sums and
    // the metrics — always through the FIXED gate (see the snapshot refresh:
    // FDW is phase-only, it cannot hold a multi-arrival sum). A pinned (or
    // pinned-previewed) offset is one absolute window for every curve;
    // unpinned (Auto), the window anchors at the sample the CALLER passes — the
    // shared earliest FRONT for channels and sums (one window is what keeps
    // the drawn Sum the exact vector sum of the drawn channels and the
    // sum-loss under its 0 dB ceiling; see VirtualCrossoverMetrics.BuildCurves)
    // and the raw curve's own front. Runs on PLINQ worker threads: reads only
    // the immutable snapshot RequestRedraw refreshed.
    private GatedMagnitude BuildMagnitudeCurve(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate)
    {
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        return BuildGatedMagnitudeCurve(
            snapshot,
            impulseResponse,
            peakIndex,
            sampleRate,
            snapshot.ResolveGateOffsetMs(oppositeSide: false, peakIndex, sampleRate));
    }

    // The opposite side's sum window: its OWN pinned offset (or its own
    // earliest-arrival anchor when unpinned) — never the active side's pin,
    // whose placement belongs to different arrival times.
    private AnalysisCurve BuildOppositeMagnitudeCurve(VirtualCrossoverSideSum side)
    {
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        return BuildGatedMagnitudeCurve(
            snapshot,
            side.ImpulseResponse,
            side.AnchorIndex,
            side.SampleRate,
            snapshot.ResolveGateOffsetMs(
                oppositeSide: true, side.AnchorIndex, side.SampleRate)).Display;
    }

    // A RAW channel curve lives in its own time: its arrival predates the
    // processed gate by the channel's delay, so even a pinned processed-view
    // offset would clip it into the left fade. Same gate durations and window
    // mode, anchored on the raw response's own START — the same rule as the
    // processed curves. Its own PEAK was the anchor once, and with the short
    // junction gate that was harmless; the steady-state window made it a
    // defect: a woofer's peak trails its onset by more than the 2 ms fade-in
    // (5.4 ms on the archived Passat woofer), so a peak-anchored window opened
    // after the response had begun and read the record minus its direct
    // arrival — octave bands off by 10+ dB against the same IR read from the
    // front.
    private AnalysisCurve BuildRawMagnitudeCurve(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate)
    {
        int anchorIndex = ProcessedChannels.StartAnchorIndex(
            impulseResponse, peakIndex, sampleRate);
        return BuildGatedMagnitudeCurve(
            magnitudeGate,
            impulseResponse,
            anchorIndex,
            sampleRate,
            anchorIndex * 1_000.0 / sampleRate).Display;
    }

    // Both widths of one gated build: the smoothed curve the plot draws and the
    // unsmoothed one the summation loss divides (see GatedMagnitude). One gate,
    // one FFT, two resamples — the second resample is the cheap half.
    private GatedMagnitude BuildGatedMagnitudeCurve(
        MagnitudeGateSnapshot snapshot,
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        double gateOffsetMs)
    {
        PhaseAnalysisSettings gate = snapshot.Template with
        {
            GateOffsetMs = gateOffsetMs
        };
        (AnalysisCurve display, AnalysisCurve unsmoothed) =
            DataHelper.GetGatedPrimarySpectrumPair(
                new ImpulseMeasurementView(impulseResponse, peakIndex, sampleRate),
                gate,
                Calibration,
                snapshot.SmoothingInverseOctaves);
        return new GatedMagnitude(display, unsmoothed);
    }

    private List<AcousticCurve> BuildPhaseCurves(List<ProcessedChannel> processed)
    {
        // The gate's own path: every shown channel is gated and FFT'd here, one
        // after another, plus one more for the sum.
        using var _ = AppProfiler.Zone("VirtualDSP.BuildPhaseCurves");
        // One shared absolute τ reference (the earliest arrival) keeps the
        // curves' relative phase intact — that relative alignment through the
        // crossover region is exactly what this view is for. The WINDOWS may
        // still follow each channel's own arrival: BuildMeasuredPhase
        // re-references every extraction to the common absolute τ, which is
        // exact as long as no window cuts into its own channel — the condition
        // ResolvePhaseGateOffsets enforces before it hands out per-curve
        // placements.
        int sampleRate = processed[0].Channel.SampleRate;
        double referenceOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(processed, sampleRate);
        double detrendMs = ResolveCommonDetrendMs(
            processed, referenceOffsetMs, sampleRate);

        // The gated spectra are built ONCE per redraw and feed both the shown
        // channels' curves and the Sum — building them twice was possible
        // before: the per-impulse cache serializes lookup and insert but not
        // the bank computation itself, so a channel job and the Sum job racing
        // on a cold cache could each run the same FFTs. The Sum needs every
        // processed channel (hidden or not, matching the magnitude Sum); with
        // the Sum off, hidden channels' banks are skipped entirely.
        bool includeSum = processed.Count >= 2 && checkBoxShowSum.Checked;
        List<ProcessedChannel> gatedChannels = processed
            .Where(item => includeSum || item.Channel.Pair.ShowProcessedCurve)
            .ToList();

        // Read the gate and project state ONCE, here on the UI thread; the
        // workers below must not reach back into gatePreview or project. The
        // placements are resolved over the gated set only, so hiding a curve
        // cannot move the window of the ones still drawn.
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
            // The Sum is the vector sum of the individually gated channel
            // SPECTRA, not a gate over the summed IR: under Auto the windows
            // follow each channel's own arrival, and no single window over one
            // summed IR could hold every channel's treble at once (FDW's
            // high-frequency windows are shorter than the arrival spread).
            // Summing the spectra keeps superposition exact by construction,
            // and with one shared window reduces to the gated summed IR by
            // linearity.
            int targetExtractionStart = gated.Min(part => part.ExtractionStart);
            Complex[] combined = DataHelper.SumGatedSpectra(
                gated.Select(part => (part.Spectrum, part.ExtractionStart)).ToList(),
                targetExtractionStart);
            jobs.Add(("Sum", SumColor, 2.4, combined, targetExtractionStart));
        }

        // One phase read per curve over the spectra built above — every curve
        // against the same absolute τ, across cores, order stable.
        return jobs
            .AsParallel()
            .AsOrdered()
            .SelectMany(job =>
            {
                (List<SignalPoint> points, List<SignalPoint> wrapSegments) =
                    SplitWrapSegments(DataHelper.GetGatedPhaseData(
                        job.Spectrum,
                        job.ExtractionStart,
                        referenceSamples,
                        sampleRate,
                        unwrap: false));
                var curves = new List<AcousticCurve>(2);
                // The ±360° wrap verticals: the channel's color faded and
                // thinned well below the curve, dashed, drawn first (i.e.
                // under the solid curve) — visible as wraps without competing
                // with the phase traces. The empty title keeps them out of
                // the plot-labels panel.
                if (wrapSegments.Count > 0)
                {
                    curves.Add(new AcousticCurve(
                        string.Empty,
                        wrapSegments,
                        OxyColor.FromAColor(110, job.Color),
                        job.Thickness * 0.4,
                        LineStyle.Dash));
                }
                curves.Add(new AcousticCurve(
                    job.Title,
                    points,
                    job.Color,
                    job.Thickness,
                    LineStyle.Solid));
                return curves;
            })
            .ToList();
    }

    // The impulse view is the gate dialog's IR preview promoted to the main
    // plot: every processed channel IR (crossover/PEQ/gain/delay/polarity
    // applied) on the shared absolute timeline, each normalized to its own
    // in-window peak, with the phase-gate Tukey window drawn where it sits.
    // Well-aligned drivers visibly start together.
    private AcousticImpulseRender? BuildImpulseRender(List<ProcessedChannel> processed)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildImpulseRender");
        // Only the shown traces set the gate offset and the ms-axis window, so
        // an auto gate never centers on a channel whose curve is hidden.
        List<ProcessedChannel> shown = processed
            .Where(item => item.Channel.Pair.ShowProcessedCurve)
            .ToList();
        if (shown.Count == 0)
        {
            return null;
        }

        int sampleRate = shown[0].Channel.SampleRate;
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

    // The gate of the side on screen. The view draws one side at a time and the two
    // sides' drivers arrive at different times, so each keeps its own placement:
    // fitting the gate on one no longer throws the other off.
    private VirtualCrossoverPhaseGateSettings ActiveGate =>
        project.PhaseGateFor(project.ActiveSideRight);

    // The one pinned absolute gate offset, or null when the gate is unpinned
    // (Auto) — in the dialog preview and in the committed state alike. Null
    // means automatic placement, which differs by view: the magnitude anchors
    // ONE shared window at the earliest processed PEAK (keeping the drawn Sum
    // the exact vector sum of the drawn channels), while the PHASE curves each
    // follow their own estimated arrival START so FDW's short high-frequency
    // windows land on the right channel's first cycles — see
    // ResolvePhaseGateOffsets for the condition that keeps those curves
    // comparable, and what happens when it does not hold.
    private double? PinnedGateOffsetMs => gatePreview is { } preview
        ? preview.AutoOffset ? null : preview.OffsetMs
        : ActiveGate.OffsetMs;

    /// <summary>
    /// The ceiling on <see cref="DataHelper.GateLeadingEdgeLossDb"/>: above it
    /// a window is cutting into its channel's leading edge, and the curve stops
    /// describing that channel — it starts describing what came after it.
    /// Two placements are put to this figure: whether a phase curve may take
    /// its own window (<see cref="AllowsPerCurvePhaseGate"/>) and whether the
    /// window in use holds the channels at all
    /// (<see cref="JudgeGatePlacement"/>).
    /// <para>
    /// Measured on the v5 field session (four processed channels, gate
    /// 5/50/20 ms): windows placed on each channel's own arrival START read
    /// -28.4 to -72.2 dB, while the arrival-PEAK placement that drew a
    /// summing pair as antiphase read -3.5 to -10.8 dB — it discards a fifth
    /// to nearly half of a steeply low-passed channel's own energy. -20 dB
    /// sits in the middle of that 17.6 dB gap.
    /// </para>
    /// <para>
    /// The Passat session put the other end on the scale: a 15.06 ms gate
    /// inherited from another car's project, against processed arrivals at
    /// 4.10 to 5.97 ms, read +1.0 to +15.2 dB — at the top of that range the
    /// window discards thirty times the energy it keeps — while the same
    /// channels gated on their own arrivals read -42.9 to -55.0 dB.
    /// </para>
    /// </summary>
    private const double MaxGateLeadingEdgeLossDb = -20.0;

    /// <summary>
    /// Whether a per-curve window may be used for a channel, from what it
    /// discards ahead of its plateau against what the shared window would.
    /// <para>
    /// The ceiling alone is not the question, because a gate can be too short
    /// to hold a channel's leading edge WHEREVER it is placed: the project
    /// default (0.5/4/1.5 ms) cannot contain one period of a 55 Hz subwoofer,
    /// and on the field session it read -19.4 dB at the channel's own arrival
    /// and -19.4 dB at the shared one — identical. Refusing there buys no
    /// accuracy and costs the per-curve placement that keeps a late channel
    /// inside FDW's short windows, so the shared window has to be the better
    /// placement for this channel before it is worth taking. The arrival-PEAK
    /// placements this guard exists to catch are 25.7 to 61.4 dB worse than
    /// the shared window, so both conditions hold there with room to spare.
    /// </para>
    /// </summary>
    internal static bool AllowsPerCurvePhaseGate(
        double perCurveLossDb,
        double sharedLossDb) =>
        perCurveLossDb <= MaxGateLeadingEdgeLossDb ||
        perCurveLossDb <= sharedLossDb;

    /// <summary>
    /// Where each phase curve's window sits, aligned with
    /// <paramref name="gatedChannels"/> — the channels that are actually
    /// gated, so a hidden curve can never move the placement of the drawn
    /// ones.
    /// <para>
    /// A pinned gate is one absolute window for every curve. Auto gives each
    /// channel its OWN estimated arrival, which is what lets FDW's short
    /// high-frequency windows sit on that channel's own first cycles instead
    /// of on whichever channel happened to arrive first — the whole point of
    /// reading phase through FDW. Per-curve placement is only comparable while
    /// every window opens before its channel's response does, so each one is
    /// put to <see cref="AllowsPerCurvePhaseGate"/> and the whole set drops
    /// back to the shared window if any fails: mixing the two placements would
    /// be worse than either.
    /// </para>
    /// </summary>
    private List<double> ResolvePhaseGateOffsets(
        IReadOnlyList<ProcessedChannel> gatedChannels,
        double sharedOffsetMs,
        int sampleRate)
    {
        List<double> Shared() => gatedChannels.Select(_ => sharedOffsetMs).ToList();
        if (PinnedGateOffsetMs is not null)
        {
            return Shared();
        }

        double leftMs = gatePreview?.LeftMs ?? project.PhaseGateLeftMs;
        double plateauMs = gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs;
        double rightMs = gatePreview?.RightMs ?? project.PhaseGateRightMs;
        var perCurve = new List<double>(gatedChannels.Count);
        foreach (ProcessedChannel item in gatedChannels)
        {
            var view = new ImpulseMeasurementView(item.ImpulseResponse, 0, sampleRate);
            double startMs = TransferIrStartCache.ResolveStartMs(
                item.ImpulseResponse, sampleRate, item.PeakIndex,
                item.ValidRange);
            if (!AllowsPerCurvePhaseGate(
                    DataHelper.GateLeadingEdgeLossDb(
                        view, startMs, leftMs, plateauMs, rightMs),
                    DataHelper.GateLeadingEdgeLossDb(
                        view, sharedOffsetMs, leftMs, plateauMs, rightMs)))
            {
                return Shared();
            }

            perCurve.Add(startMs);
        }

        return perCurve;
    }

    // A stored gate offset is used as-is; an unconfigured side (Auto) follows
    // the earliest ESTIMATED IR START of the processed channels — the
    // band-limited first-arrival front, robust to head garbage that poisons a
    // bare peak read — so the gate tracks source and delay changes until the
    // user pins it in the gate dialog.
    private double ResolveGateOffsetMs(
        IReadOnlyList<ProcessedChannel> processed,
        int sampleRate) =>
        ActiveGate.OffsetMs ?? EarliestStartMs(processed, sampleRate);

    // The Auto gate anchor: the earliest estimated IR start across the
    // processed channels (memoized per IR in TransferIrStartCache).
    private static double EarliestStartMs(
        IReadOnlyList<ProcessedChannel> processed,
        int sampleRate) =>
        processed.Min(item => TransferIrStartCache.ResolveStartMs(
            item.ImpulseResponse, sampleRate, item.PeakIndex, item.ValidRange));

    /// <summary>Which way the window fails a channel.</summary>
    internal enum GateCutKind
    {
        /// <summary>
        /// It opens after the channel's front, so the curve is built from
        /// whatever came after the response.
        /// </summary>
        OpensAfterArrival,

        /// <summary>
        /// It is over before the channel arrives, so the curve holds none of
        /// the channel at all.
        /// </summary>
        ClosesBeforeArrival
    }

    /// <summary>
    /// One channel the gate placement fails: its label, where its response
    /// starts (ms), which way the window misses it and what the window throws
    /// away ahead of its plateau
    /// (<see cref="DataHelper.GateLeadingEdgeLossDb"/>, dB — meaningful for
    /// <see cref="GateCutKind.OpensAfterArrival"/> only).
    /// </summary>
    internal readonly record struct GateCutChannel(
        string Name,
        double StartMs,
        GateCutKind Kind,
        double LeadingEdgeLossDb);

    /// <summary>
    /// The window the side on screen is actually gated at, judged against the
    /// channels it windows: where its plateau starts and ends, whether the
    /// placement is pinned or Auto, and the channels it fails.
    /// </summary>
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

        /// <summary>
        /// Where the Tukey window reaches zero: the plateau plus the fade-out
        /// behind it. Content between <see cref="PlateauEndMs"/> and here is
        /// attenuated, not absent.
        /// </summary>
        public double WindowEndMs => PlateauEndMs + RightMs;

        public bool Any(GateCutKind kind) => Cut.Any(item => item.Kind == kind);
    }

    /// <summary>
    /// Judges the magnitude view's window against every processed channel: the
    /// gate is an ABSOLUTE time, so a placement that belonged to one set of
    /// measurements windows the reverberant tail of the next set instead of
    /// their response — and nothing about a channel's curve says so, because a
    /// tail has a magnitude too. The magnitude placement is the one judged
    /// (mirroring <see cref="VirtualCrossoverMetrics.BuildCurves"/>'s shared
    /// anchor): it is what the drawn curves, the Sum and the sum-loss read-out
    /// are built from, and both shared placements now open at the same sample
    /// — the earliest estimated START of the processed channels — so judging
    /// this one answers for the phase view's shared placement as well. (It
    /// used to anchor on the earliest PEAK, which is never the earlier of the
    /// two and so also answered for it; the rules were unified when the
    /// junction gate moved to fronts.) The phase view's per-curve placements
    /// keep their own guard in <see cref="ResolvePhaseGateOffsets"/>.
    /// </summary>
    private GatePlacementVerdict? JudgeGatePlacement(
        IReadOnlyList<ProcessedChannel> processed)
    {
        if (processed.Count == 0)
        {
            return null;
        }

        MagnitudeGateSnapshot snapshot = magnitudeGate;
        int sampleRate = processed[0].Channel.SampleRate;
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

    /// <summary>
    /// How the window in use fails a channel, or null when it holds it.
    /// A window has two ways to miss: it can open after the channel's front,
    /// or be over before the channel arrives at all.
    /// <para>
    /// The two are judged differently BECAUSE the leading-edge figure only
    /// answers the first. It is a ratio of what the window discards ahead of
    /// its plateau to what it keeps, and a channel that lands past the window
    /// has nothing ahead of that plateau either — so it reads not as bad but
    /// as EXCELLENT. Measured: a tweeter delayed 20 ms out of a 4 ms plateau
    /// reads -282 dB there, the best figure of any channel in that session,
    /// with its curve holding none of the channel. (The +∞ that
    /// <see cref="DataHelper.GateLeadingEdgeLossDb"/> reserves for "the window
    /// kept nothing" needs a bit-for-bit silent window, which a measured record
    /// does not give.) So the far side is decided on the window's geometry
    /// instead: the channel's front has to be inside the window — the plateau
    /// AND the fade-out behind it, because the fade starts at unity and a
    /// front just past the plateau is attenuated, not missing. How far into a
    /// fade a front may land before the curve stops describing the channel is
    /// a continuum this deliberately does not judge: nothing measured says
    /// where in it to draw a line, and the end of the window is the one place
    /// the answer is not a matter of degree.
    /// </para>
    /// <para>
    /// The near side is the placement question: over the ceiling, and worse by
    /// <see cref="GateMisplacementMarginDb"/> than the same gate on the
    /// channel's own arrival. That comparison is what keeps a short gate from
    /// reading as a misplacement — the project default cannot hold one period
    /// of a 55 Hz subwoofer wherever it is placed, and the field session read
    /// -19.4 dB at the channel's own arrival and -19.4 dB at the shared one —
    /// because a gate the user cannot fix by moving is not something to stop
    /// them with. <see cref="AllowsPerCurvePhaseGate"/> makes the same
    /// comparison with no margin at all, which is right THERE: its penalty for
    /// reading a hair's difference as significant is one curve falling back to
    /// the shared window. Here the penalty is an amber note and two refused
    /// commands, so the difference has to be one the user can act on.
    /// </para>
    /// </summary>
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
            placementLossDb > MaxGateLeadingEdgeLossDb &&
            placementLossDb > ownArrivalLossDb + GateMisplacementMarginDb
                ? GateCutKind.OpensAfterArrival
                : null;
    }

    /// <summary>
    /// How much worse than the channel's own arrival a placement has to read
    /// before it counts as misplaced: 3 dB, the window discarding twice the
    /// energy that moving it would. Nothing separates a short gate from a
    /// misplaced one by a hair — the field misplacements are 44 to 70 dB apart
    /// on this comparison and the short-gate case is 0.0 dB apart, so the
    /// margin only has to be clear of arithmetic noise between two nearby
    /// offsets.
    /// </summary>
    private const double GateMisplacementMarginDb = 3.0;

    // The plot's one-line form of the verdict; the detail below carries the
    // per-channel figures and what to do about them. The two ways a window
    // misses produce different curves — the tail of the response, or none of
    // it — so the line says which one the reader is looking at.
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

    // The tooltip, and the body of the refusal the automatic commands show:
    // the same explanation either way, so the plot and the dialogs cannot
    // describe the same placement differently.
    internal static string FormatGateCutDetail(GatePlacementVerdict verdict)
    {
        bool opensLate = verdict.Any(GateCutKind.OpensAfterArrival);
        bool closesEarly = verdict.Any(GateCutKind.ClosesBeforeArrival);
        bool one = verdict.Cut.Count == 1;
        var text = new System.Text.StringBuilder();
        text.Append($"The gate's plateau runs from {verdict.OffsetMs:0.00} to ")
            .Append($"{verdict.PlateauEndMs:0.00} ms")
            // The fade-out only matters to the reader when a channel fell off
            // the far end: that is the edge it was measured against.
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
                .Append($"{MaxGateLeadingEdgeLossDb:0} dB is already the ceiling.)")
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

    // A window that holds none of the channel at all reads as infinite loss;
    // printing "∞ dB" beats a number nobody can place.
    private static string FormatLeadingEdgeLossDb(double lossDb) =>
        double.IsFinite(lossDb) ? $"{lossDb:+0.0;-0.0} dB" : "∞ (the window holds none of it)";

    // Refuses an automatic command while the side on screen is gated on a
    // window that opens after its own channels arrive. Neither search reads the
    // gate itself, but both are judged on what it produces — the curves, the
    // sum-loss read-out and (for Auto delay) the outcome metric written into
    // the alignment log — so a run started here can only be verified against a
    // view of the reverberant tail.
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

    // The τ detrend follows the same pattern: unconfigured projects reference
    // the earliest arrival. One τ serves every curve, so their relative phase —
    // the whole point of this view — survives the detrend.
    private double ResolveDetrendMs(int referenceSample, int sampleRate) =>
        ActiveGate.DetrendMs ?? referenceSample * 1_000.0 / sampleRate;

    private double ResolveCommonDetrendMs(
        List<ProcessedChannel> processed,
        double gateOffsetMs,
        int sampleRate)
    {
        PhaseDetrendMode detrendMode = gatePreview?.DetrendMode ?? project.PhaseDetrendMode;
        if (detrendMode == PhaseDetrendMode.Off)
        {
            return 0.0;
        }
        if (detrendMode == PhaseDetrendMode.Manual)
        {
            return gatePreview?.DetrendMs ?? ResolveDetrendMs(
                ProcessedChannels.SharedStartAnchorIndex(processed), sampleRate);
        }

        // Estimate once from the existing common anchor (the earliest
        // processed FRONT, the same channel the shared window opens on), then
        // apply that exact value to every driver and the sum.
        ProcessedChannel anchor = processed.MinBy(item => ProcessedChannels.StartAnchorIndex(
            item.ImpulseResponse, item.PeakIndex, item.Channel.SampleRate,
            item.ValidRange))!;
        var view = new ImpulseMeasurementView(anchor.ImpulseResponse, 0, sampleRate);
        PhaseAnalysisSettings settings = CreateVirtualPhaseSettings(
            gateOffsetMs,
            PhaseDetrendMode.Auto,
            manualDetrendMilliseconds: 0.0);
        return DataHelper.ResolveCommonPhaseDetrendMilliseconds(view, settings);
    }

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

    // Wrapped phase jumps from +180° to −180° between adjacent bins. The
    // main curve breaks at the wrap (NaN) so the jump does not read as a
    // real phase transition drawn at full stroke; the jump itself goes
    // into WrapSegments — NaN-separated two-point verticals the caller
    // draws as a thinner dashed twin, keeping the wrap visible.
    private static (List<SignalPoint> Points, List<SignalPoint> WrapSegments)
        SplitWrapSegments(List<SignalPoint> phase)
    {
        var points = new List<SignalPoint>(phase.Count);
        var wrapSegments = new List<SignalPoint>();
        SignalPoint? previous = null;
        foreach (SignalPoint point in phase)
        {
            if (point.X is < 20 or > 20_000)
            {
                continue;
            }

            var current = new SignalPoint(point.X, point.Y / Math.PI * 180.0);
            if (previous is { } before && !double.IsNaN(before.Y) &&
                !double.IsNaN(current.Y) &&
                Math.Abs(current.Y - before.Y) > 180.0)
            {
                points.Add(new SignalPoint(point.X, double.NaN));
                // Strictly vertical, halfway between the two bins (geometric
                // mean = the visual midpoint on the log-frequency axis).
                double wrapHz = Math.Sqrt(before.X * current.X);
                wrapSegments.Add(new SignalPoint(wrapHz, before.Y));
                wrapSegments.Add(new SignalPoint(wrapHz, current.Y));
                wrapSegments.Add(new SignalPoint(wrapHz, double.NaN));
            }

            points.Add(current);
            previous = current;
        }

        return (points, wrapSegments);
    }

    // Opens the manual phase-gate dialog: the gate offset and Tukey shoulders
    // with a live preview of every processed channel IR, so reflections can be
    // cut out of the phase view visually.
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

        int sampleRate = processed[0].Channel.SampleRate;
        int reference = ProcessedChannels.SharedStartAnchorIndex(processed);
        double fitOffsetMs = EarliestStartMs(processed, sampleRate);

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
        // The callback is wired after Init so seeding the controls does not
        // trigger a redundant redraw; from here every dialog change repaints the
        // phase plot immediately.
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
                // Only the PLACEMENT lands on the side being viewed. The window's
                // lengths and the analysis modes are project-wide, so both sides keep
                // reading the phase at the same resolution and by the same method.
                VirtualCrossoverPhaseGateSettings gate = ActiveGate;
                // Auto pressed = unpinned: store null so this side's gate
                // keeps following the earliest estimated channel IR start.
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
            // Save committed the candidate values, Cancel discards them; either
            // way the plot re-renders from the project state.
            gatePreview = null;
            RequestRedraw();
        }
    }

    // The last applied processed snapshot: the correlation view's data source.
    private ProcessedRender? lastProcessedRender;

    // Single-flight for the correlation rebuilds, mirroring the main redraw
    // loop: at most ONE sweep computes at a time, and a request that arrives
    // mid-compute only marks the loop to run once more with the then-latest
    // state — a stamp alone would merely hide stale results while the stacked
    // tasks kept burning a full sweep of inverse FFTs each.
    private Task? correlationRebuildTask;
    private bool correlationRebuildPending;

    // Guards the combo repopulation from feeding its own SelectedIndexChanged
    // back into the project as a user edit.
    private bool suppressCorrelationPairEvents;

    private void RedrawDspPlot()
    {
        if (CurrentDspPlotMode() == DspPlotMode.Correlation)
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

            // The chain is drawn without its delay term: the filters' own shape is
            // the readable part, while a bulk delay would wrap the phase into an
            // unreadable sawtooth and swamp the filter group delay (its effect is
            // visible on the acoustic plot). A bypassed channel draws its flat
            // identity chain.
            DspChannelChain chain = channel.Pair.Bypass
                ? DspChannelChain.Identity
                : channel.Settings.ToChain() with { DelayMs = 0 };
            curves.Add(new DspChainCurve(
                $"{channel.Name} filter", chain, channel.SampleRate, ChannelColors[i]));
        }

        dspChainPlot.Draw(CurrentDspPlotMode(), curves);
    }

    // The adjacent pairs of the correlation view, derived from the LAST
    // processed snapshot so the combo lists exactly what the plot can analyze
    // (enabled channels with sources, active side, ordered by band).
    private List<AdjacentPair> CurrentCorrelationPairs() =>
        lastProcessedRender is { } render
            ? ProcessedChannels.GetAdjacentPairs(
                ProcessedChannels.OrderByBand(render.Channels))
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
            radioDspCorrelation.Checked && labels.Count > 0;
    }

    // Runs on the UI thread. Starts the rebuild loop, or — when one is
    // already computing — marks it to repeat once more with the latest state.
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
            CurrentDspPlotMode() == DspPlotMode.Correlation);

        correlationRebuildTask = null;
    }

    private async Task RedrawCorrelationPlotAsync()
    {
        List<AdjacentPair> pairs = CurrentCorrelationPairs();
        if (pairs.Count == 0)
        {
            dspChainPlot.DrawCorrelation(null);
            return;
        }

        AdjacentPair pair = pairs[Math.Clamp(
            project.CorrelationPairIndex, 0, pairs.Count - 1)];
        JunctionCorrelationView? data = null;
        try
        {
            List<ProcessedChannel> scope = lastProcessedRender is { } render
                ? render.Channels.ToList()
                : [pair.Lower, pair.Upper];
            data = await Task.Run(() => BuildCorrelationView(pair, scope));
        }
        catch (Exception exception)
        {
            // Best-effort like every redraw: keep the last frame.
            System.Diagnostics.Debug.WriteLine(
                $"Correlation view rebuild failed: {exception}");
        }

        if (dspPlotView.IsDisposed ||
            CurrentDspPlotMode() != DspPlotMode.Correlation)
        {
            return;
        }

        // A request that arrived mid-compute means this result is already
        // stale: skip the draw, the loop is about to recompute anyway.
        if (data != null && !correlationRebuildPending)
        {
            dspChainPlot.DrawCorrelation(data);
        }
    }

    // The off-thread compute of one junction's correlation view. Both
    // channels enter PROCESSED (delays, polarity, filters applied), so lag 0
    // is the current alignment and every reading is a correction to the
    // UPPER channel. The gate follows the alignment engine's own basis — the
    // pair's earliest front, in the pair's band (see the gate remarks in
    // VirtualCrossoverAnalysis) — and the sweep probes it by rotating the
    // windowed cut, the same bins and rotation the search's SumLossEvaluator
    // reads, so the drawn score IS the surface Auto delay searches (see the
    // plateau remarks on JunctionLossSweep for what re-gating each probe
    // through the stationary window drew instead). The crop still spans the
    // whole side, because a shared offset is what keeps the channels'
    // relative timing intact; it no longer decides anything the score reads,
    // the anchor being derived from the pair's own content rather than from
    // an index into the crop.
    // Internal so the correlation-view harness can render the exact product
    // curves without constructing the panel.
    internal static JunctionCorrelationView BuildCorrelationView(
        AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildCorrelationView");
        int sampleRate = pair.Lower.Channel.SampleRate;
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
        // The channels' valid ranges, shifted into the crop's frame: the
        // front detections behind the sweep and the direct cuts take them, so
        // the drawn surface reads the same fronts the search reads — on a
        // clean capture the heuristic fallback agrees anyway, but a delayed
        // or glitch-headed record is exactly where the two paths must not
        // part.
        ValidSampleRange Shifted(ProcessedChannel item, Complex[] croppedIr) =>
            item.ValidRange.IsKnown
                ? new ValidSampleRange(
                    Math.Max(0, item.ValidRange.StartSample - cropStart),
                    Math.Clamp(
                        item.ValidRange.EndSample - cropStart,
                        0,
                        croppedIr.Length))
                : item.ValidRange;
        ValidSampleRange lowerRange = Shifted(pair.Lower, lower);
        ValidSampleRange upperRange = Shifted(pair.Upper, upper);
        // No gate anchor is passed: the sweep windows each channel at its own
        // band-limited front, exactly as every junction measurement of an
        // Auto delay run does (see BuildAlignmentBins), so the drawn score
        // stays the search's surface. The read-out beside this plot keeps its
        // own, shared placement (one window for the drawn channels, their Sum
        // and the loss curve, which is what makes the Sum the sum of what is
        // drawn) — the two answer different questions and always did: the
        // read-out measures the WHOLE sum inside the pair band, this
        // measures the pair.

        // The window spans 1.5 crossover periods to each side (floored at the
        // fixed diagnostic span), so the neighboring comb lobes both ways are
        // in view even at an 80 Hz junction.
        double windowMs = Math.Max(3.0, 1.5 * 1000.0 / pair.CrossoverHz);
        double passOctaves = Math.Log2(pair.BandHighHz / pair.BandLowHz);
        List<SignalPoint> whitened =
            VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                lower, upper, sampleRate, pair.CrossoverHz, passOctaves,
                windowMs, centerLagMs: 0, phaseTransform: true);

        // The whitened curve again, on the DIRECT sound alone (see
        // VirtualCrossoverAnalysis.CutDirectSound — the same cut the
        // engine's direct-coherence witness reads, so this curve shows the
        // very figure the search weighed). The full-record curves above read
        // the whole capture — reflections included, which on a thin-overlap
        // junction outvote the drivers — while this one answers the question
        // the view is usually opened with: where do the DRIVERS align.
        List<SignalPoint> whitenedDirect =
            VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                VirtualCrossoverAnalysis.CutDirectSound(
                    lower, sampleRate,
                    pair.BandLowHz, pair.BandHighHz, pair.CrossoverHz,
                    lowerRange),
                VirtualCrossoverAnalysis.CutDirectSound(
                    upper, sampleRate,
                    pair.BandLowHz, pair.BandHighHz, pair.CrossoverHz,
                    upperRange),
                sampleRate, pair.CrossoverHz, passOctaves,
                windowMs, centerLagMs: 0, phaseTransform: true);

        // The score comb repeats per crossover period, so the step must
        // resolve THAT scale — a fixed points-per-window count aliased at
        // high junctions (at a 20 kHz-class split, window/60 equals a whole
        // period and the comb could sample flat). A tenth of a period keeps
        // the lobes drawn; the window/300 floor bounds the sweep at ~600
        // points per polarity for pathological corner setups.
        double stepMs = Math.Max(
            Math.Min(windowMs / 60.0, 100.0 / pair.CrossoverHz),
            Math.Max(0.005, windowMs / 300.0));
        List<SignalPoint> ScoreSweep(bool invert) =>
            VirtualCrossoverAnalysis.JunctionLossSweep(
                upper, lower, sampleRate,
                pair.BandLowHz, pair.BandHighHz,
                -windowMs, windowMs, stepMs, invert,
                // The search's own settings, or the drawn surface is not the
                // searched one: per-channel windows (null anchor) and the
                // search-side level match, whose absence re-shapes the lobes
                // whenever the two channels sit at different gains.
                gateAnchorSample: null,
                levelMatch: true,
                variableValidRange: upperRange,
                fixedValidRange: lowerRange)
            .Select(point => new SignalPoint(
                point.DelayMs,
                point.LossDb +
                    VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
                    (point.DipDb - point.LossDb)))
            .ToList();

        double arrivalLagMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                lower, sampleRate, pair.BandLowHz, pair.BandHighHz, lowerRange)
            - VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                upper, sampleRate, pair.BandLowHz, pair.BandHighHz, upperRange);

        return new JunctionCorrelationView(
            $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}",
            pair.Upper.Channel.Name,
            pair.CrossoverHz,
            pair.BandLowHz,
            pair.BandHighHz,
            whitened,
            whitenedDirect,
            ScoreSweep(invert: false),
            ScoreSweep(invert: true),
            arrivalLagMs);
    }

    // ------------------------------------------------------- capture / export

    // Saves the current complex sum as a Captured overlay in Frequency Response,
    // closing the loop: virtual alignment -> comparison against real measurements
    // and target curves -> EQ Wizard.
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

        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses(
            processed.Select(item => item.ImpulseResponse).ToList());
        AnalysisCurve sumCurve = BuildMagnitudeCurve(
            sum,
            processed.Min(item => item.PeakIndex),
            processed[0].Channel.SampleRate).Display;

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

    // Writes the DSP settings of every participating channel as a tuning sheet:
    // a printable PDF or a plain-text file.
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

        // The sheet subtitle takes the compact single-line summary.
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
        // The sheet prints BOTH sides, so the rate comes from any physically
        // resolved side — reading only the active side through the delegating
        // properties would fall back to 48 kHz when, say, the shown left side
        // is empty and every 44.1 kHz source sits on the right.
        int sampleRate = ResolvedSidesExcept(null)
            .Select(item => item.State.SampleRate)
            .FirstOrDefault(48_000);
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

            // Only a sheet that reached the disk becomes "the previous export": an
            // answer given to a dialog the user then backed out of — at the file
            // picker, at an empty render or on a write failure — is not a choice
            // they made about any sheet, so it must not pre-select the next one.
            sheetQConvention = qConvention;
        }
        catch (Exception exception)
        {
            ShowError("The tuning sheet could not be exported.", exception.Message);
        }
    }

    // The convention the sheet's Q column is stated in is a property of the processor
    // being tuned, and Virtual DSP has no selector of its own. Inheriting the EQ
    // Wizard's meant a crossover sheet silently stated for whatever device THAT mode
    // was last pointed at, so the export asks instead: pre-selected with the session's
    // last EXPORTED convention (the shared setting until one is written), and never
    // written back, so the wizard's own sheets keep following its selector. The answer
    // is only returned here — the caller records it once the sheet exists.
    // Null when the user cancels.
    private PeqQConvention? AskSheetQConvention()
    {
        using var dialog = new TuningSheetQConventionDialog(
            sheetQConvention ?? TargetDspQConvention);
        return dialog.ShowDialog(FindForm()) == DialogResult.OK
            ? dialog.SelectedConvention
            : null;
    }

    // ----------------------------------------------------------------- wizard

    // The crossover wizard: detects each channel's usable band and driver type
    // from the raw magnitude, lets the user confirm the types, and writes the
    // analytic proposal (LR24 splits, cut-only gains) into the channels. Delay
    // and polarity stay untouched — that is Auto delay's job, done against the
    // complex sum afterward.
    private void OpenAutoSetupWizard()
    {
        var participating = channels
            .Where(channel => channel.Pair.Enabled &&
                channel.TransferImpulseResponse != null)
            .ToList();
        if (participating.Count < 2)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        // The band read below is gate-independent (it windows each raw response
        // on its own front), but the proposal it writes is checked on the plot
        // and in the sum-loss read-out, both of which the gate builds — so a
        // misplaced window still has to be dealt with before the wizard runs.
        if (RefuseOnMisplacedGate("Auto crossover"))
        {
            return;
        }

        // Band/type detection reads the raw (unprocessed) responses with a fixed
        // 1/3-octave smoothing, independent of the display smoothing.
        var wizardOptions = new FrequencyResponseOptions { SmoothingInverseOctaves = 3 };
        var dialogChannels = new List<(string Name, Color Accent,
            IReadOnlyList<SignalPoint> MagnitudeDb, IReadOnlyList<double>? Coherence,
            IReadOnlyList<SignalPoint>? Distortion, DriverBandEstimate Band)>();
        try
        {
            foreach (VirtualCrossoverChannel channel in participating)
            {
                AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
                    new ImpulseMeasurementView(
                        channel.TransferImpulseResponse!,
                        channel.TransferPeakIndex,
                        channel.SampleRate),
                    wizardOptions,
                    Calibration);
                // When the source carried per-bin coherence, resample it onto the
                // magnitude curve's log grid so the band read discounts the
                // frequencies the measurement did not trust.
                IReadOnlyList<double>? coherence =
                    channel.TransferCoherence is { Length: > 1 } linear
                        ? CoherencePerPoint(linear, curve.Points, channel.SampleRate)
                        : null;
                // The distortion curve (computed at source resolve) bounds each
                // driver by its distortion-clean band; null when the source had no
                // sweep deconvolution.
                IReadOnlyList<SignalPoint>? distortion = channel.DistortionCurve;
                OxyColor accent = ChannelColors[channels.IndexOf(channel)];
                dialogChannels.Add((
                    $"{channel.Name} — {channel.Settings.DisplayName}",
                    Color.FromArgb(accent.R, accent.G, accent.B),
                    curve.Points,
                    coherence,
                    distortion,
                    CrossoverAutoSetup.EstimateBand(curve.Points, coherence, distortion)));
            }
        }
        catch (ArgumentException exception)
        {
            ShowError("A channel's response has no usable band.", exception.Message);
            return;
        }

        using var dialog = new VirtualCrossoverAutoSetupDialog();
        dialog.Init(
            participating[0].SampleRate,
            dialogChannels,
            participating.Select(channel => channel.TransferImpulseResponse!).ToList());
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } proposals)
        {
            return;
        }

        for (int i = 0; i < participating.Count; i++)
        {
            VirtualCrossoverChannel channel = participating[i];
            CrossoverProposal proposal = proposals[i];
            // A crossover is one electrical filter, so both sides of a stereo
            // pair get the SAME frequencies, families and slopes (and the same
            // wizard gain) — only delay and the scene-offset trim differ per
            // side. A mono pair has just its one side.
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
            }

            ApplySettingsToControl(channel);
        }

        ScheduleSave();
        RedrawAll();
    }

    // Averages a measurement's per-bin coherence (γ², a linear FFT grid over
    // [0, Nyquist], bin k → k · rate / (2·(len−1))) over each magnitude point's
    // 1/3-octave band, so the result lines up 1:1 with the wizard's magnitude
    // curve (which is itself 1/3-octave smoothed) for EstimateBand to consume.
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

    // ---------------------------------------------------------------- session

    // Exports the whole tool state (channels, chains, gate, view flags) to a
    // user-chosen file, so a tuning session can be shared or archived instead of
    // living only in the internal autosave.
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

    // Imports a session file, replacing the current state; the sources are
    // re-resolved from their stored history entries / file paths, and the result
    // immediately becomes the new internal autosave.
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

        VirtualCrossoverProjectFile imported;
        try
        {
            imported = VirtualCrossoverProjectFile.LoadFrom(dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowError("The session could not be loaded.", exception.Message);
            return;
        }

        await ApplyProjectAsync(imported, imported: true);
        ScheduleSave();
        await RelinkMissingSourcesAsync();
        ShowCalibrationNotice();
    }

    // Offers to relink the sources an imported session could not find. The stored
    // paths were written on the machine that measured, so a session that arrives
    // without its original tree — a different drive letter, a renamed folder, the
    // measurements filed apart from the session — leaves every such channel
    // unresolved. One folder answers for all of them: the same locator runs against
    // it, and it stays this session's extra search root.
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
            // Same order as a project load: leave the loading state before the
            // redraw, so the final frame is the real plot.
            SetProjectLoading(false);
            RedrawAll();
        }

        // The relinked paths belong in the autosave, not just on screen.
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

    // Every side that names a source FILE but has no measurement behind it — the
    // ones a folder can answer for. A side with no stored reference is simply
    // empty, not missing, and one referring only to a history entry of the machine
    // that measured is not something pointing at a folder could fix.
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

    // One sentence, once, about the calibration an imported session arrived with.
    // A calibration describes the microphone the MEASUREMENTS were taken with, so
    // a session travelling with its data brings the right correction along and
    // the selector starts on it; the user is told, and offered to keep it in their
    // own list. A session written before the curve travelled can only name an
    // entry, and an id is local to the machine that minted it — so a match by id
    // alone is reported as exactly that, and a miss keeps the selection the panel
    // already had rather than replacing a working choice with nothing.
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

    // The session's own curve is selected and its curves match the author's; the
    // one thing left to decide is whether it should live in this machine's list
    // too — which is right when these are the author's measurements (the
    // calibration is of the microphone that took them), and wrong for a
    // measurement taken here with a different microphone.
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

        // The host refreshed the consumers on adding, which hands the selection
        // over to the new entry (ReconcileCalibrationSelection); this is only for a
        // host that did not.
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
