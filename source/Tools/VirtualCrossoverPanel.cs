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

    // The tool renders through the standard FR pipeline (window around the peak,
    // log grid, smoothing) with its own options instance, so the FR mode's
    // settings stay untouched.
    private readonly FrequencyResponseOptions frequencyResponseOptions = new();

    private readonly List<VirtualCrossoverChannel> channels = new();

    // The model-to-control binding. VirtualCrossoverChannel is UI-free, so the
    // panel owns the mapping to each block's control; only the binding methods
    // (ApplySettingsToControl, UpdateSourceButton, tooltips…) look it up, and
    // the algorithmic paths read the model directly.
    private readonly Dictionary<VirtualCrossoverChannel, VirtualCrossoverChannelControl>
        channelControls = new();
    private readonly VirtualCrossoverProcessingCoordinator processingCoordinator = new();
    private readonly VirtualCrossoverMetrics metrics;
    private readonly ToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 12_000,
        ShowAlways = true
    };

    private VirtualCrossoverProjectFile project = new();

    // Candidate gate values while the gate dialog is open, so the phase plot
    // tracks the dialog live; null once it closes (Save committed them to the
    // project, Cancel reverts by simply dropping them).
    private (double OffsetMs, double LeftMs, double PlateauMs, double RightMs,
        PhaseWindowMode WindowMode, int FdwCycles, PhaseDetrendMode DetrendMode,
        double DetrendMs)? gatePreview;
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
    private bool autoDelayBusy;
    private bool loadingProject;

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
        buttonSessionImport.Click += async (_, _) => await ImportSessionAsync();
        buttonSessionExport.Click += (_, _) => ExportSession();
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
    /// Microphone calibration applied to the magnitude curves, resolved from the
    /// panel's own <see cref="comboBoxCalibration"/> selection (Off / 0° / 90°).
    /// Null when calibration is off or unavailable.
    /// </summary>
    private CalibrationFile? Calibration { get; set; }

    // Resolves a calibration file for a given mode; supplied by the host form,
    // which owns the configured 0°/90° paths. Null until the host wires it.
    private Func<MicrophoneCalibrationMode, CalibrationFile?>? calibrationResolver;
    private bool hasZeroDegreeCalibration;
    private bool hasNinetyDegreeCalibration;

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
            await ApplyProjectAsync(loaded);
            NotifyIfProjectBackedUp(loaded.BackupNoticePath);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Virtual DSP project load failed: {exception}");
        }
    }

    // Tell the user, once, when their unreadable session file was moved aside so
    // they know a .backup exists to recover from (previously a silent rename).
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
    // controls, view flags, and freshly re-resolved sources.
    private async Task ApplyProjectAsync(VirtualCrossoverProjectFile newProject)
    {
        SetProjectLoading(true);
        try
        {
            await BindProjectAsync(newProject);
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

    private async Task BindProjectAsync(VirtualCrossoverProjectFile newProject)
    {
        project = newProject;
        // Match the block list to the project's channel count (validated into the
        // supported range on load), so an imported 2- or 6-channel session shows
        // exactly its channels.
        SetChannelCount(project.Pairs.Count);

        suppressProjectEvents = true;
        try
        {
            checkBoxShowSum.Checked = project.ShowSumCurve;
            checkBoxShowLoss.Checked = project.ShowLossCurve;
            radioViewImpulse.Checked = project.ShowImpulseView;
            radioViewPhase.Checked =
                !project.ShowImpulseView && project.ShowPhaseView;
            radioViewMagnitude.Checked =
                !project.ShowImpulseView && !project.ShowPhaseView;
            radioSideRight.Checked = project.ActiveSideRight;
            radioSideLeft.Checked = !project.ActiveSideRight;
            numericSceneOffset.Value = Clamp(
                numericSceneOffset, project.StereoSceneOffsetMs);
            acousticPlot.ConfigureForView(CurrentAcousticView());
            UpdateGateButtonAvailability();
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

        RefreshCalibrationCombo();

        foreach (VirtualCrossoverChannel channel in channels)
        {
            // BOTH physical slots are wiped first — through the effective
            // accessor a mono pair's real right slot is unreachable, and a
            // stale measurement from the previous project would otherwise
            // resurface the moment the pair stops being mono. Then both sides
            // resolve up front (the stereo Auto delay needs them together); a
            // mono pair resolves its single slot once.
            channel.PhysicalSideState(false).Clear();
            channel.PhysicalSideState(true).Clear();
            foreach (bool rightSide in new[] { false, true })
            {
                if (channel.Pair.Mono && rightSide)
                {
                    continue;
                }

                await ResolveSourceAsync(channel, rightSide, showErrors: false);
            }

            UpdateSourceButton(channel);
        }

        UpdateSideRadioTexts();
        // The final redraw is issued by ApplyProjectAsync after the loading
        // state clears, so it draws the real plot instead of the loading note.
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
        }
        catch (Exception exception)
        {
            // The project file is a convenience; failing to save it must never
            // break the tool (e.g. a read-only install directory).
            System.Diagnostics.Debug.WriteLine(
                $"Virtual DSP project save failed: {exception}");
        }
    }

    // ------------------------------------------------------------ calibration

    /// <summary>
    /// Wires the microphone calibration source. The host owns the configured
    /// 0°/90° files, so it supplies both a resolver and which profiles exist; the
    /// panel offers the available ones in its own Off / 0° / 90° selector. Called
    /// again whenever the configured files change, refreshing the selector.
    /// </summary>
    internal void ConfigureCalibration(
        Func<MicrophoneCalibrationMode, CalibrationFile?> resolver,
        bool hasZeroDegree,
        bool hasNinetyDegree)
    {
        calibrationResolver = resolver;
        hasZeroDegreeCalibration = hasZeroDegree;
        hasNinetyDegreeCalibration = hasNinetyDegree;
        RefreshCalibrationCombo();
    }

    // Rebuilds the selector's items from the available profiles and the persisted
    // mode, then resolves the calibration the curves use. A profile that is no
    // longer configured falls back to Off (the helper's default selection).
    private void RefreshCalibrationCombo()
    {
        suppressProjectEvents = true;
        try
        {
            MicrophoneCalibrationComboHelper.Configure(
                comboBoxCalibration,
                project.CalibrationMode,
                hasZeroDegreeCalibration,
                hasNinetyDegreeCalibration);
        }
        finally
        {
            suppressProjectEvents = false;
        }

        ResolveCalibration();
        RedrawAll();
    }

    // Sets the calibration file from the selector's current mode. Off (or an
    // absent resolver) yields no calibration, matching the loopback-referenced
    // default.
    private void ResolveCalibration()
    {
        MicrophoneCalibrationMode mode =
            MicrophoneCalibrationComboHelper.GetSelectedMode(comboBoxCalibration);
        Calibration = mode == MicrophoneCalibrationMode.Off
            ? null
            : calibrationResolver?.Invoke(mode);
    }

    private void OnCalibrationChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        project.CalibrationMode =
            MicrophoneCalibrationComboHelper.GetSelectedMode(comboBoxCalibration);
        ResolveCalibration();
        ScheduleSave();
        RedrawAll();
    }

    // ----------------------------------------------------------------- wiring

    private void WirePanelEvents()
    {
        checkBoxShowSum.CheckedChanged += (_, _) => OnViewChanged();
        checkBoxShowLoss.CheckedChanged += (_, _) => OnViewChanged();
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
        numericSceneOffset.ValueChanged += (_, _) => OnSceneOffsetChanged();
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

    private void OnSceneOffsetChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        project.StereoSceneOffsetMs = (double)numericSceneOffset.Value;
        ScheduleSave();
    }

    // The "L→R" / "R→L" commands: copy the DSP-chain part of one side's
    // settings (gain, crossover, PEQ) onto the other for the channels the user
    // picks. Sources and delays stay side-specific — each side has its own
    // measurement and its own arrival — and polarity stays with the alignment.
    // Mono pairs have one settings set and are not offered.
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
            dialog.SelectedIndices.Count == 0)
        {
            return;
        }

        bool targetSideShown = project.ActiveSideRight == !fromRight;
        foreach (int index in dialog.SelectedIndices)
        {
            VirtualCrossoverChannel channel = candidates[index];
            CopyChainSettings(
                channel.SideSettings(fromRight),
                channel.SideSettings(!fromRight));
            if (targetSideShown)
            {
                ApplySettingsToControl(channel);
            }
        }

        ScheduleSave();
        RedrawAll();
    }

    // The POSITION-INDEPENDENT part of one side, copied onto the other: the settings
    // that describe the driver rather than where it sits. Delay, polarity and the
    // all-pass are deliberately not among them — each aligns a driver against its own
    // side's geometry, and a left tweeter's phase correction is not a right tweeter's.
    // PeqBand is an immutable record, so a fresh list is a deep enough copy.
    private static void CopyChainSettings(
        VirtualCrossoverChannelSettings from,
        VirtualCrossoverChannelSettings to)
    {
        to.GainDb = from.GainDb;
        to.CrossoverKind = from.CrossoverKind;
        to.HighPassEdge = from.HighPassEdge;
        to.LowPassEdge = from.LowPassEdge;
        to.PeqPreampDb = from.PeqPreampDb;
        to.PeqBands = from.PeqBands.ToList();
        to.PeqSourceName = from.PeqSourceName;
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
        control.PeqLoadClicked += (_, _) => LoadPeq(channel);
        control.PeqClearClicked += (_, _) => ClearPeq(channel);
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
        buttonAddChannel.Enabled =
            !autoDelayBusy && channels.Count < MaxChannelCount;
        buttonRemoveChannel.Enabled =
            !autoDelayBusy && channels.Count > MinChannelCount;
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
        UpdateGateButtonAvailability();
        // Fractional-octave smoothing shapes only the frequency-domain curves;
        // grey it out in the impulse view where it has no effect.
        comboBoxSmoothing.Enabled = !radioViewImpulse.Checked;
        OnViewChanged();
    }

    // The gate shapes the phase and impulse views; grey the button out on the
    // magnitude view so it does not suggest an effect on those curves.
    private void UpdateGateButtonAvailability() =>
        buttonPhaseGate.Enabled = !radioViewMagnitude.Checked;

    private void OnViewChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        project.ShowSumCurve = checkBoxShowSum.Checked;
        project.ShowLossCurve = checkBoxShowLoss.Checked;
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
            control.GainInput.Value = Clamp(control.GainInput, settings.GainDb);
            control.DelayInput.Value = Clamp(control.DelayInput, settings.DelayMs);
            control.InvertCheckBox.Checked = settings.InvertPolarity;
            control.CrossoverKindComboBox.SelectedItem = settings.CrossoverKind;
            // Family first: selecting it repopulates the slope list the slope
            // selection then lands in.
            control.HighPassFamilyComboBox.SelectedItem = settings.HighPassEdge.Family;
            control.HighPassFrequencyInput.Value = Clamp(
                control.HighPassFrequencyInput, settings.HighPassEdge.FrequencyHz);
            control.HighPassSlopeComboBox.SelectedItem = settings.HighPassEdge.SlopeDbPerOctave;
            control.HighPassRippleInput.Value = Clamp(
                control.HighPassRippleInput, settings.HighPassEdge.RippleDb);
            control.LowPassFamilyComboBox.SelectedItem = settings.LowPassEdge.Family;
            control.LowPassFrequencyInput.Value = Clamp(
                control.LowPassFrequencyInput, settings.LowPassEdge.FrequencyHz);
            control.LowPassSlopeComboBox.SelectedItem = settings.LowPassEdge.SlopeDbPerOctave;
            control.LowPassRippleInput.Value = Clamp(
                control.LowPassRippleInput, settings.LowPassEdge.RippleDb);
            control.AllPassTypeComboBox.SelectedItem = settings.AllPassType;
            control.AllPassFrequencyInput.Value = Clamp(
                control.AllPassFrequencyInput, settings.AllPassFrequencyHz);
            control.AllPassQInput.Value = Clamp(control.AllPassQInput, settings.AllPassQ);
            control.ShowRawCheckBox.Checked = settings.ShowRawCurve;
            control.ShowProcessedCheckBox.Checked = settings.ShowProcessedCurve;
            control.BypassCheckBox.Checked = settings.Bypass;
            control.MonoCheckBox.Checked = channel.Pair.Mono;
            control.Muted = !settings.Enabled;
        });

        UpdateSourceButton(channel);
        UpdatePeqLabel(channel);
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
        settings.ShowRawCurve = control.ShowRawCheckBox.Checked;
        settings.ShowProcessedCurve = control.ShowProcessedCheckBox.Checked;
        settings.Enabled = !control.Muted;
        settings.Bypass = control.BypassCheckBox.Checked;
        channel.Pair.Mono = control.MonoCheckBox.Checked;
    }

    private static decimal Clamp(DarkNumericUpDown control, double value)
    {
        decimal rounded = (decimal)Math.Round(value, control.DecimalPlaces);
        return Math.Clamp(rounded, control.Minimum, control.Maximum);
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

        ToolStripMenuItem clearItem = new("Clear");
        clearItem.Enabled = channel.Settings.HasSource;
        clearItem.Click += (_, _) => ClearSource(channel);
        menu.Items.Add(clearItem);

        Button sourceButton = ControlFor(channel).SourceButton;
        menu.Show(sourceButton, new Point(0, sourceButton.Height));
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
                    "This measurement has no loopback transfer IR.",
                    "The virtual crossover sums loopback-referenced responses; " +
                    "re-measure with a loopback channel configured.");
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
    // first (it survives file moves), then the file path. A source that no
    // longer exists degrades to an unresolved side instead of failing the
    // project load.
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
        try
        {
            MeasurementHistorySnapshot? snapshot =
                await LoadSnapshotFromReferenceAsync(settings);
            // The same assignment core as the interactive pickers (the file
            // behind a stored path may have been replaced since the project was
            // saved), but under RejectSilently: an incompatible source — no
            // transfer IR, or a rate that clashes with the other sides — stays
            // unresolved (the button shows the warning glyph) instead of
            // prompting, because a silent reload cannot ask.
            if (snapshot != null)
            {
                TryAssignSource(
                    state, revision, snapshot, SourceConflictPolicy.RejectSilently);
            }
        }
        catch (Exception exception) when (!showErrors)
        {
            _ = exception;
        }
    }

    // Loads the measurement behind a persisted source reference: the history
    // entry first (it survives file moves), then the file path. Null when
    // neither resolves — the side stays unresolved instead of failing the load.
    private async Task<MeasurementHistorySnapshot?> LoadSnapshotFromReferenceAsync(
        VirtualCrossoverChannelSettings settings)
    {
        if (settings.HistoryEntryId is { } entryId && HistoryService != null)
        {
            MeasurementHistorySnapshot? snapshot =
                await HistoryService.GetSnapshotAsync(entryId);
            if (snapshot != null)
            {
                return snapshot;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.SourceFilePath) &&
            File.Exists(settings.SourceFilePath))
        {
            ImpulseResponseFile file =
                await ImpulseResponseFile.LoadAsync(settings.SourceFilePath);
            return MeasurementHistoryService.CreateSnapshot(file);
        }

        return null;
    }

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
            curve = chosen.Import(File.ReadAllText(dialog.FileName));
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
        UpdatePeqLabel(channel);
        ScheduleSave();
        RedrawAll();
    }

    private void ClearPeq(VirtualCrossoverChannel channel)
    {
        channel.Settings.PeqBands = new List<PeqBand>();
        channel.Settings.PeqPreampDb = 0;
        channel.Settings.PeqSourceName = null;
        UpdatePeqLabel(channel);
        ScheduleSave();
        RedrawAll();
    }

    private void UpdatePeqLabel(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        bool noPeq = settings.PeqBands.Count == 0 && settings.PeqPreampDb == 0;
        string text = noPeq
            ? "No PEQ"
            : $"{settings.PeqSourceName ?? "PEQ"}: {settings.PeqBands.Count} bands, " +
              $"preamp {settings.PeqPreampDb:0.0} dB";
        Label peqInfoLabel = ControlFor(channel).PeqInfoLabel;
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
            comboBoxCalibration,
            "Microphone calibration applied to the magnitude curves —\r\n" +
            "and to the curves the Sum loss read-out is measured from,\r\n" +
            "so it still moves those numbers in the Phase and Impulse\r\n" +
            "views, where no calibrated trace is drawn.\r\n" +
            "The measurement is loopback-referenced, so this is\r\n" +
            "optional; 0° / 90° appear when their files are configured\r\n" +
            "in Record Settings.");
        toolTip.SetToolTip(
            buttonAutoDelay,
            "Align the channels in two stages: band-limited first\r\n" +
            "arrivals set the coarse delays, then a phase search\r\n" +
            "(±half a crossover period) fine-tunes them and flips\r\n" +
            "polarity when the channels sum better inverted.\r\n" +
            "With both sides loaded the run is STEREO: the left side\r\n" +
            "aligns first, the right top driver is timed to the left\r\n" +
            "one (honoring the L/R offset), and the right side\r\n" +
            "descends from it — so the stereo image stays put.\r\n" +
            "Set the crossover filters first — the search targets\r\n" +
            "the overlap region around their corner frequencies.");
        toolTip.SetToolTip(
            numericSceneOffset,
            "Stereo scene offset for Auto delay (ms).\r\n" +
            "Positive: the RIGHT side arrives earlier by this much,\r\n" +
            "pulling the image from the driver's axis toward the\r\n" +
            "dash center on a left-hand-drive car. Typical: 0.2–0.3 ms.\r\n" +
            "Right-hand drive: enter a negative value.\r\n" +
            "0 = image centered on the measurement position.");
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
            "Copy the LEFT side's gain, crossover and PEQ onto the\r\n" +
            "RIGHT side for the channels you pick. Sources, delay,\r\n" +
            "polarity and all-pass stay with their side; mono channels\r\n" +
            "are not offered.");
        toolTip.SetToolTip(
            buttonCopyRightToLeft,
            "Copy the RIGHT side's gain, crossover and PEQ onto the\r\n" +
            "LEFT side for the channels you pick. Sources, delay,\r\n" +
            "polarity and all-pass stay with their side; mono channels\r\n" +
            "are not offered.");
        toolTip.SetToolTip(
            buttonAutoSetup,
            "Crossover wizard: detect each channel's driver type from\r\n" +
            "its response, confirm the types, and get a starting point —\r\n" +
            "LR24 splits where the responses intersect and cut-only\r\n" +
            "gains that level the channels.\r\n" +
            "Run Auto delay afterward to phase-align the result.");
        toolTip.SetToolTip(
            buttonPhaseGate,
            "Configure the gate for the phase and impulse views:\r\n" +
            "offset and Tukey fades, with an IR preview — cut the\r\n" +
            "window before the first reflection for clean traces.\r\n" +
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

        frequencyResponseOptions.SmoothingInverseOctaves =
            comboBoxSmoothing.SelectedItem is int smoothing ? smoothing : 12;

        RequestRedraw();
    }

    // Starts the redraw loop, or — if one is already running — marks its current
    // pass stale so it repeats once more with the latest settings. Called only on
    // the UI thread, so the flag and the task handle need no synchronization.
    private void RequestRedraw()
    {
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
                if (!channel.Settings.Enabled ||
                    state.ProcessingSource is not { } source)
                {
                    continue;
                }

                DspChannelChain chain = channel.Settings.Bypass
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
                color));
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
        AnalysisCurve? oppositeSum =
            checkBoxShowSum.Checked && radioViewMagnitude.Checked
                ? await metrics.ComputeOppositeSumCurveAsync(
                    channels, !project.ActiveSideRight, revision)
                : null;
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
        using (AppProfiler.Zone("VirtualDSP.BuildCurves"))
        {
            (magnitudes, sumCurve) = metrics.BuildCurves(processed);
        }

        using (AppProfiler.Zone("VirtualDSP.UpdateMetric"))
        {
            UpdateMetric(processed, magnitudes, sumCurve, stereoDeltas);
        }

        using (AppProfiler.Zone("VirtualDSP.UpdateCrossoverWarning"))
        {
            UpdateCrossoverWarning(processed);
        }

        // Split from the draw on purpose: building the curves (the phase view's
        // gated FFTs) and handing them to OxyPlot are different suspects, and as
        // one expression they were indistinguishable.
        AcousticRender acousticRender;
        using (AppProfiler.Zone("VirtualDSP.BuildAcousticRender"))
        {
            acousticRender = BuildAcousticRender(processed, magnitudes, sumCurve, oppositeSum);
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
            hint, BuildMagnitudeCurves(processed, magnitudes, sumCurve, oppositeSum), null);
    }

    private List<AcousticCurve> BuildMagnitudeCurves(
        List<ProcessedChannel> processed,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve,
        AnalysisCurve? oppositeSumCurve)
    {
        // The processed curves arrive prebuilt from BuildCurves, but a shown RAW
        // curve is spectrum-built right here, one channel after another.
        using var _ = AppProfiler.Zone("VirtualDSP.BuildMagnitudeCurves");
        var curves = new List<AcousticCurve>();
        for (int i = 0; i < processed.Count; i++)
        {
            ProcessedChannel item = processed[i];
            if (item.Channel.Settings.ShowRawCurve)
            {
                AnalysisCurve raw = BuildMagnitudeCurve(
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

            if (item.Channel.Settings.ShowProcessedCurve)
            {
                AnalysisCurve curve = magnitudes != null
                    ? magnitudes[i]
                    : BuildMagnitudeCurve(
                        item.ImpulseResponse, item.PeakIndex, item.Channel.SampleRate);
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

        if (checkBoxShowLoss.Checked)
        {
            // The signed dB gap between the complex sum and the phase-blind
            // magnitude sum of the processed channels (<= 0 by the triangle
            // inequality); shares the one SumLossCurve definition with the metric,
            // so the drawn curve and the measured loss cannot drift apart.
            List<SignalPoint> points = VirtualCrossoverAnalysis.SumLossCurve(
                sumCurve.Points,
                magnitudes.Select(curve => curve.Points).ToList());
            curves.Add(new AcousticCurve(
                "Sum loss", points, LossColor, 1.8, LineStyle.Dash));
        }

        return curves;
    }

    // ------------------------------------------------- metric and auto delay

    private void UpdateMetric(
        List<ProcessedChannel> processed,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve,
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
            entries = metrics.BuildEntries(processed, magnitudes, sumCurve);
        }

        string compact = VirtualCrossoverMetric.FormatCompact(entries);
        string detail = entries.Count > 0 ? VirtualCrossoverMetric.FormatDetail(entries) : string.Empty;
        if (stereoDeltas is { Count: > 0 })
        {
            compact += "\r\n\r\n" +
                VirtualCrossoverMetric.FormatStereoDeltasCompact(stereoDeltas);
            detail += (detail.Length > 0 ? "\r\n\r\n" : string.Empty) +
                VirtualCrossoverMetric.FormatStereoDeltasDetail(stereoDeltas);
        }

        MetricChanged?.Invoke(compact, detail);
    }

    // The spread of alignment delays, above which the setup is flagged. A driver
    // whose crossover has pathological group delay (a narrow or steep low-
    // frequency band-pass) arrives so late that Auto delay must push every other
    // driver out by this much to match it — a spread this large is the symptom.
    private const double CrossoverGroupDelayWarningMs = 10.0;

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
            .Where(item => !item.Channel.Settings.Bypass)
            .ToList();
        if (active.Count < 2)
        {
            labelCrossoverWarning.Visible = false;
            return;
        }

        // The latest driver holds the smallest delay (everyone else is delayed
        // toward it); the spread is how far ahead the earliest driver sits.
        ProcessedChannel latest = active.MinBy(item => item.Channel.Settings.DelayMs)!;
        double earliestDelay = active.Max(item => item.Channel.Settings.DelayMs);
        double spread = earliestDelay - latest.Channel.Settings.DelayMs;
        if (spread <= CrossoverGroupDelayWarningMs)
        {
            labelCrossoverWarning.Visible = false;
            toolTip.SetToolTip(labelCrossoverWarning, string.Empty);
            return;
        }

        string name = latest.Channel.Name;
        labelCrossoverWarning.Text =
            $"⚠ {name} lags the others by ~{spread:0} ms — check its crossover.";
        toolTip.SetToolTip(
            labelCrossoverWarning,
            $"{name} arrives ~{spread:0} ms after the other drivers, so Auto delay pushes " +
            "them out by that much to match it.\r\n\r\n" +
            "This is usually excessive crossover group delay — a narrow or steep low-frequency " +
            "band-pass. Reduce its slope or widen its band to bring the alignment delays down.");
        labelCrossoverWarning.Visible = true;
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

        var alignment = new Dictionary<VirtualCrossoverChannel, AlignmentOverride>();
        // Cheap participant snapshot: enabled channels with a resolved
        // measurement. No DSP runs here — the shared crop and every ApplyChain
        // happen later, off the UI thread, inside ComputeAutoAlignment's
        // AlignmentReprocessor.
        List<VirtualCrossoverChannel> participants = channels
            .Where(channel =>
                channel.Settings.Enabled && channel.TransferImpulseResponse != null)
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
            .Where(channel => channel.Settings.Bypass)
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
        var log = new System.Text.StringBuilder();
        log.AppendLine($"Auto delay {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        log.AppendLine($"Crossover window: {minHz:0} - {maxHz:0} Hz");
        log.AppendLine("Previous delay / polarity settings ignored for this run.");

        // The alignment stages are FFT-heavy (~seconds). Run them off the UI
        // thread so the window stays responsive and shows the busy state instead
        // of hanging. Every input that could mutate the channel list or its
        // settings (channel controls, add/remove, session import, the auto
        // commands) is locked for the duration, so the background compute reads
        // a stable configuration.
        SetAutoDelayBusy(true);
        try
        {
            await Task.Run(() => ComputeAutoAlignment(participants, alignment, log));
            // The panel (or its form) may have been closed during the compute;
            // applying results would then touch disposed controls.
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            await ApplyAlignmentResultAsync(participants, alignment, log);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Auto delay failed: {exception}");
            if (!IsDisposed && IsHandleCreated)
            {
                ShowError("Auto delay failed.", exception.Message);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                SetAutoDelayBusy(false);
            }
        }
    }

    // Marks the panel busy while Auto delay runs: the heavy stages moved off the
    // UI thread, so without this the window would look idle while a click does
    // nothing for seconds. Disables the inputs the compute reads (so it sees a
    // stable snapshot) and shows a wait state.
    private void SetAutoDelayBusy(bool busy)
    {
        autoDelayBusy = busy;
        buttonAutoDelay.Enabled = !busy;
        buttonAutoDelay.Text = busy ? "Aligning…" : "Auto delay";
        buttonAutoSetup.Enabled = !busy;
        // The background compute iterates the live channel list and its live
        // settings, so everything that can mutate them must be locked out too:
        // add/remove change the list (and dispose controls), a session import
        // replaces the whole configuration mid-search. The side radios flip
        // the mutable ActiveRight the single-side run reads through — flipping
        // mid-run would hand the worker the other side's measurements and land
        // the results on the wrong side's settings.
        buttonSessionImport.Enabled = !busy;
        radioSideLeft.Enabled = !busy;
        radioSideRight.Enabled = !busy;
        numericSceneOffset.Enabled = !busy;
        buttonCopyLeftToRight.Enabled = !busy;
        buttonCopyRightToLeft.Enabled = !busy;
        UpdateChannelButtons();
        foreach (VirtualCrossoverChannel channel in channels)
        {
            ControlFor(channel).Enabled = !busy;
        }

        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (busy)
        {
            MetricChanged?.Invoke("Auto delay\r\naligning…", string.Empty);
        }
    }

    // Applies the computed delays/polarities to the channels and their controls,
    // redraws, and closes the log with this run's outcome. UI-thread work: it
    // touches controls and the plots, so it stays out of the background compute.
    private async Task ApplyAlignmentResultAsync(
        List<VirtualCrossoverChannel> participants,
        Dictionary<VirtualCrossoverChannel, AlignmentOverride> alignment,
        System.Text.StringBuilder log)
    {
        foreach (VirtualCrossoverChannel channel in participants)
        {
            AlignmentOverride result = alignment.GetValueOrDefault(channel);
            channel.Settings.DelayMs = Math.Round(result.DelayMs, 2);
            channel.Settings.InvertPolarity = result.InvertPolarity;
            ApplySettingsToControl(channel);
            log.AppendLine(
                $"Result {channel.Name}: " +
                $"delay {channel.Settings.DelayMs:0.00} ms, " +
                $"invert {(channel.Settings.InvertPolarity ? "yes" : "no")}");
        }

        ScheduleSave();
        RedrawAll();
        // RedrawAll pushes the read-out asynchronously (the ApplyChain FFTs run off
        // the UI thread), so recompute the metric synchronously from the just-
        // applied settings so the log ends with this run's true outcome.
        ProcessedRender? render = await ProcessChannelsAsync();
        List<ProcessedChannel> outcome = render?.Channels ?? [];
        (List<AnalysisCurve>? outcomeMagnitudes, AnalysisCurve? outcomeSum) =
            metrics.BuildCurves(outcome);
        log.AppendLine(VirtualCrossoverMetric.FormatDetail(
            metrics.BuildEntries(outcome, outcomeMagnitudes, outcomeSum)));
        WriteAlignmentLog(log.ToString());
    }

    // Bridges the panel's channel model to the dsp AutoAlignmentEngine (where
    // the FFT-heavy alignment stages live, unit-tested): snapshots + junctions
    // in, an override map out. Runs on a background thread; the AlignmentReprocessor
    // owns the run-scoped FFT cache, so between consecutive junction searches only
    // the one or two channels that changed their overrides are re-FFT'd, and the
    // shared UI-thread coordinator cache is never touched.
    private void ComputeAutoAlignment(
        List<VirtualCrossoverChannel> participants,
        Dictionary<VirtualCrossoverChannel, AlignmentOverride> alignment,
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
            ordered.Select(channel => new AlignmentReprocessInput(
                channel,
                channel.TransferImpulseResponse!,
                channel.SampleRate,
                channel.Settings.ToChain())).ToList(),
            AutoDelaySearchCropLength,
            AutoDelaySearchCropPrePeakSamples);

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

        var engineAlignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            ordered.Select(channel => snapshots[channel]).ToList(),
            junctions,
            reprocessor.Reprocess,
            engineAlignment,
            log);

        foreach ((IAlignmentChannel channel, AlignmentOverride result) in engineAlignment)
        {
            alignment[(VirtualCrossoverChannel)channel] = result;
        }
    }

    // The Auto delay search reads only the gated direct sound, so it runs on
    // a shared crop of the measured IRs (one offset for every channel keeps
    // the inter-channel timing intact). 64k samples keep well over a second
    // of decay at 44.1 kHz — conservative headroom over the 4096-sample
    // evaluation gate and the low-band arrival envelopes.
    private const int AutoDelaySearchCropLength = 65_536;
    private const int AutoDelaySearchCropPrePeakSamples = 8_192;

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
            if (channel.SideSettings(false).Enabled &&
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
                channel.SideSettings(true).Enabled &&
                channel.SideState(true).TransferImpulseResponse != null)
            {
                right.Add(new VirtualCrossoverSideAlignmentChannel(channel, true));
            }
        }

        return (left, right);
    }

    // The stereo Auto delay: left side first, then the L/R bridge at the top
    // pair honoring the scene offset, then the right-side descent — the
    // cascade itself lives in AutoAlignmentEngine.ComputeStereo (dsp,
    // unit-tested on synthetic systems and real car measurements).
    private async Task AutoAlignStereoAsync(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        VirtualCrossoverSideAlignmentChannel bridgeRight)
    {
        List<VirtualCrossoverSideAlignmentChannel> union = leftSide.Concat(rightSide)
            .Distinct()
            .ToList();

        // Same reasoning as the single-side run: a bypassed side processes
        // through the identity chain, so the computed delay would silently not
        // apply — refuse instead of proposing a wrong alignment.
        List<VirtualCrossoverSideAlignmentChannel> bypassed = union
            .Where(item => item.Settings.Bypass)
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

        double sceneOffsetMs = project.StereoSceneOffsetMs;

        var log = new System.Text.StringBuilder();
        log.AppendLine($"Auto delay (stereo) {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        log.AppendLine(
            $"Scene offset {sceneOffsetMs:+0.00;-0.00} ms (positive: right side leads); " +
            $"bridge {bridgeLeft.Name} -> {bridgeRight.Name} " +
            $"in {bridgeBandLowHz:0}-{bridgeBandHighHz:0} Hz");
        log.AppendLine("Previous delay / polarity settings ignored for this run.");

        var engineAlignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        SetAutoDelayBusy(true);
        try
        {
            await Task.Run(() => ComputeStereoAlignment(
                leftSide, rightSide, union, bridgeLeft, bridgeRight,
                bridgeBandLowHz, bridgeBandHighHz, sceneOffsetMs,
                engineAlignment, log));
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            await ApplyStereoAlignmentResultAsync(union, engineAlignment, log);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Auto delay failed: {exception}");
            if (!IsDisposed && IsHandleCreated)
            {
                ShowError("Auto delay failed.", exception.Message);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                SetAutoDelayBusy(false);
            }
        }
    }

    // Bridges the pair/side model to the stereo engine on a background thread,
    // sharing the same AlignmentReprocessor (run-scoped FFT cache) as the
    // single-side run.
    private void ComputeStereoAlignment(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        List<VirtualCrossoverSideAlignmentChannel> union,
        VirtualCrossoverSideAlignmentChannel bridgeLeft,
        VirtualCrossoverSideAlignmentChannel bridgeRight,
        double bridgeBandLowHz,
        double bridgeBandHighHz,
        double sceneOffsetMs,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        System.Text.StringBuilder log)
    {
        // The whole search runs on a shared direct-sound crop of the measured
        // IRs: the engine only reads the gated direct sound and band-limited
        // arrivals, so the final delays are identical to a full-length run
        // (validated on real measurements) while every FFT in the cascade
        // shrinks from the capture length to the crop.
        var reprocessor = new AlignmentReprocessor(
            union.Select(side => new AlignmentReprocessInput(
                side,
                side.State.TransferImpulseResponse!,
                side.State.SampleRate,
                side.Settings.ToChain())).ToList(),
            AutoDelaySearchCropLength,
            AutoDelaySearchCropPrePeakSamples);

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

        // The L/R pair links (the shared playing band of each stereo pair)
        // aim the descent's gentle prior at the cross-side-consistent delay —
        // the same Δ the metric panel verifies afterwards.
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
                pairLinks.Add(new StereoPairLink(left, right, lowHz, highHz));
            }
        }

        List<AlignmentSnapshot> leftByBand = ByBand(leftSide);
        List<AlignmentSnapshot> rightByBand = ByBand(rightSide);
        AutoAlignmentEngine.ComputeStereo(
            new StereoAlignmentPlan(
                leftByBand,
                Pairs(leftByBand),
                rightByBand,
                Pairs(rightByBand),
                union.Where(side => side.Runtime.Pair.Mono)
                    .Cast<IAlignmentChannel>()
                    .ToList(),
                bridgeLeft,
                bridgeRight,
                bridgeBandLowHz,
                bridgeBandHighHz,
                sceneOffsetMs,
                pairLinks),
            reprocessor.Reprocess,
            alignment,
            log);
    }

    // Applies the stereo proposal to BOTH sides' settings, rebinds the visible
    // controls and closes the log with the active side's metric.
    private async Task ApplyStereoAlignmentResultAsync(
        List<VirtualCrossoverSideAlignmentChannel> union,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        System.Text.StringBuilder log)
    {
        foreach (VirtualCrossoverSideAlignmentChannel side in union)
        {
            AlignmentOverride result = alignment.GetValueOrDefault(side);
            side.Settings.DelayMs = Math.Round(result.DelayMs, 2);
            side.Settings.InvertPolarity = result.InvertPolarity;
            log.AppendLine(
                $"Result {side.Name}: delay {side.Settings.DelayMs:0.00} ms, " +
                $"invert {(side.Settings.InvertPolarity ? "yes" : "no")}");
        }

        foreach (VirtualCrossoverChannel channel in union
            .Select(side => side.Runtime)
            .Distinct())
        {
            ApplySettingsToControl(channel);
        }

        ScheduleSave();
        RedrawAll();
        // The read-out follows the active side; the log records which one.
        ProcessedRender? render = await ProcessChannelsAsync();
        List<ProcessedChannel> outcome = render?.Channels ?? [];
        (List<AnalysisCurve>? outcomeMagnitudes, AnalysisCurve? outcomeSum) =
            metrics.BuildCurves(outcome);
        log.AppendLine($"Metric ({(project.ActiveSideRight ? "R" : "L")} side):");
        log.AppendLine(VirtualCrossoverMetric.FormatDetail(
            metrics.BuildEntries(outcome, outcomeMagnitudes, outcomeSum)));
        WriteAlignmentLog(log.ToString());
    }

    // A diagnostic trace of the last Auto delay run (pair bands, arrivals,
    // deltas, fine results), for sharing when an alignment looks wrong. Best
    // effort: a failed write must never break the alignment itself.
    private static void WriteAlignmentLog(string text)
    {
        try
        {
            string path = ApplicationDataPaths.Current.VirtualDspAlignmentLogFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch
        {
            // Diagnostics only.
        }
    }

    private AnalysisCurve BuildMagnitudeCurve(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate)
    {
        return DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(impulseResponse, peakIndex, sampleRate),
            frequencyResponseOptions,
            Calibration);
    }

    private List<AcousticCurve> BuildPhaseCurves(List<ProcessedChannel> processed)
    {
        // The gate's own path: every shown channel is gated and FFT'd here, one
        // after another, plus one more for the sum.
        using var _ = AppProfiler.Zone("VirtualDSP.BuildPhaseCurves");
        // One shared absolute reference (the earliest arrival) keeps the curves'
        // relative phase intact — that relative alignment through the crossover
        // region is exactly what this view is for.
        int reference = processed.Min(item => item.PeakIndex);
        int sampleRate = processed[0].Channel.SampleRate;
        double gateOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(reference, sampleRate);
        double detrendMs = ResolveCommonDetrendMs(processed, gateOffsetMs, sampleRate);

        // Read the gate and project state ONCE, here on the UI thread: every curve gets
        // the identical settings anyway, and the workers below must not reach back into
        // gatePreview or project.
        PhaseAnalysisSettings settings = CreateVirtualPhaseSettings(
            gateOffsetMs,
            PhaseDetrendMode.Manual,
            detrendMs);

        var jobs = new List<(string Title, Complex[] Ir, OxyColor Color, double Thickness)>();
        foreach (ProcessedChannel item in processed)
        {
            if (item.Channel.Settings.ShowProcessedCurve)
            {
                jobs.Add((item.Channel.Name, item.ImpulseResponse, item.Color, 1.8));
            }
        }

        if (processed.Count >= 2 && checkBoxShowSum.Checked)
        {
            jobs.Add((
                "Sum",
                VirtualCrossoverAnalysis.SumImpulseResponses(
                    processed.Select(item => item.ImpulseResponse).ToList()),
                SumColor,
                2.4));
        }

        // One gated FFT per curve, and they are independent: GetGatedPhaseData allocates
        // its own buffers and reads only the settings captured above, so the curves build
        // across cores. AsOrdered keeps the plot order stable. Same shape as the
        // magnitude curves in VirtualCrossoverMetrics.BuildCurves.
        return jobs
            .AsParallel()
            .AsOrdered()
            .Select(job => new AcousticCurve(
                job.Title,
                BuildPhasePoints(job.Ir, sampleRate, settings),
                job.Color,
                job.Thickness,
                LineStyle.Solid))
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
            .Where(item => item.Channel.Settings.ShowProcessedCurve)
            .ToList();
        if (shown.Count == 0)
        {
            return null;
        }

        int reference = shown.Min(item => item.PeakIndex);
        int sampleRate = shown[0].Channel.SampleRate;
        double gateOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(reference, sampleRate);

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

    // A stored gate offset is used as-is; an unconfigured side follows the
    // earliest processed arrival, so the gate tracks source and delay changes
    // until the user pins it in the gate dialog.
    private double ResolveGateOffsetMs(int referenceSample, int sampleRate) =>
        ActiveGate.OffsetMs ?? referenceSample * 1_000.0 / sampleRate;

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
                processed.Min(item => item.PeakIndex), sampleRate);
        }

        // Estimate once from the existing common anchor (earliest processed
        // arrival), then apply that exact value to every driver and the sum.
        ProcessedChannel anchor = processed.MinBy(item => item.PeakIndex)!;
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

    // Static on purpose: the caller reads the gate and project state once on the UI
    // thread and hands the settings down, so this runs on a pool thread without the
    // compiler letting it reach back into a control.
    private static List<SignalPoint> BuildPhasePoints(
        Complex[] impulseResponse,
        int sampleRate,
        PhaseAnalysisSettings settings)
    {
        // The gate construction is shared with the Phase mode (DataHelper's
        // gated extraction): a Tukey window of left + plateau + right whose
        // left shoulder ends at the gate offset, zero-padded to the fixed FFT
        // length so the frequency grid is constant. The τ detrend is the
        // fractional-sample phase reference; every curve built with the same τ
        // is directly comparable regardless of where its gate sits.
        var view = new ImpulseMeasurementView(impulseResponse, 0, sampleRate);
        List<SignalPoint> phase = DataHelper.GetGatedPhaseData(view, settings);

        // Wrapped phase jumps from +180° to −180° between adjacent bins; a NaN
        // break keeps the plot from drawing that wrap as a vertical line that
        // reads like a real phase transition.
        var points = new List<SignalPoint>(phase.Count);
        double previous = double.NaN;
        foreach (SignalPoint point in phase)
        {
            if (point.X is < 20 or > 20_000)
            {
                continue;
            }

            double degrees = point.Y / Math.PI * 180.0;
            if (!double.IsNaN(previous) && !double.IsNaN(degrees) &&
                Math.Abs(degrees - previous) > 180.0)
            {
                points.Add(new SignalPoint(point.X, double.NaN));
            }

            points.Add(new SignalPoint(point.X, degrees));
            previous = degrees;
        }

        return points;
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
        int reference = processed.Min(item => item.PeakIndex);
        double fitOffsetMs = reference * 1_000.0 / sampleRate;

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
            ResolveGateOffsetMs(reference, sampleRate),
            project.PhaseGateLeftMs,
            project.PhaseGatePlateauMs,
            project.PhaseGateRightMs,
            ResolveDetrendMs(reference, sampleRate),
            project.PhaseWindowMode,
            project.PhaseFdwCycles,
            project.PhaseDetrendMode,
            fitOffsetMs);
        // The callback is wired after Init so seeding the controls does not
        // trigger a redundant redraw; from here every dialog change repaints the
        // phase plot immediately.
        dialog.PreviewChanged = (offsetMs, leftMs, plateauMs, rightMs, windowMode,
            fdwCycles, detrendMode, detrendMs) =>
        {
            gatePreview = (offsetMs, leftMs, plateauMs, rightMs, windowMode,
                fdwCycles, detrendMode, detrendMs);
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
                gate.OffsetMs = dialog.GateOffsetMs;
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
            if (!channel.Settings.Enabled || channel.TransferImpulseResponse == null)
            {
                continue;
            }

            // The chain is drawn without its delay term: the filters' own shape is
            // the readable part, while a bulk delay would wrap the phase into an
            // unreadable sawtooth and swamp the filter group delay (its effect is
            // visible on the acoustic plot). A bypassed channel draws its flat
            // identity chain.
            DspChannelChain chain = channel.Settings.Bypass
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
            data = await Task.Run(() => BuildCorrelationView(pair));
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
    // UPPER channel. The pair is cropped to the alignment engine's own
    // direct-sound window first: the correlation and the honest loss sweep
    // then read the same basis Auto delay searches, and the sweep's per-point
    // inverse FFTs shrink from the capture length to the crop.
    private static JunctionCorrelationView BuildCorrelationView(AdjacentPair pair)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildCorrelationView");
        int sampleRate = pair.Lower.Channel.SampleRate;
        Complex[][] cropped = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            new List<Complex[]>
            {
                pair.Lower.ImpulseResponse,
                pair.Upper.ImpulseResponse
            },
            AutoDelaySearchCropLength,
            AutoDelaySearchCropPrePeakSamples);
        Complex[] lower = cropped[0];
        Complex[] upper = cropped[1];

        // The window spans 1.5 crossover periods to each side (floored at the
        // fixed diagnostic span), so the neighboring comb lobes both ways are
        // in view even at an 80 Hz junction.
        double windowMs = Math.Max(3.0, 1.5 * 1000.0 / pair.CrossoverHz);
        double passOctaves = Math.Log2(pair.BandHighHz / pair.BandLowHz);
        List<SignalPoint> correlation =
            VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                lower, upper, sampleRate, pair.CrossoverHz, passOctaves,
                windowMs, centerLagMs: 0, phaseTransform: false);
        List<SignalPoint> whitened =
            VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                lower, upper, sampleRate, pair.CrossoverHz, passOctaves,
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
                -windowMs, windowMs, stepMs, invert)
            .Select(point => new SignalPoint(
                point.DelayMs,
                point.LossDb +
                    VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
                    (point.DipDb - point.LossDb)))
            .ToList();

        double arrivalLagMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                lower, sampleRate, pair.BandLowHz, pair.BandHighHz)
            - VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                upper, sampleRate, pair.BandLowHz, pair.BandHighHz);

        return new JunctionCorrelationView(
            $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}",
            pair.Upper.Channel.Name,
            pair.CrossoverHz,
            pair.BandLowHz,
            pair.BandHighHz,
            correlation,
            whitened,
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
            processed[0].Channel.SampleRate);

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
        (List<AnalysisCurve>? metricMagnitudes, AnalysisCurve? metricSum) =
            metrics.BuildCurves(metricChannels);
        string metricLine = VirtualCrossoverMetric.FormatLabel(
            metrics.BuildEntries(metricChannels, metricMagnitudes, metricSum));
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
                    dialog.FileName, project, metricLine, sampleRate);
            }
            else
            {
                File.WriteAllText(
                    dialog.FileName,
                    VirtualCrossoverSheet.FormatText(project, metricLine));
            }
        }
        catch (Exception exception)
        {
            ShowError("The tuning sheet could not be exported.", exception.Message);
        }
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
            .Where(channel => channel.Settings.Enabled &&
                channel.TransferImpulseResponse != null)
            .ToList();
        if (participating.Count < 2)
        {
            System.Media.SystemSounds.Beep.Play();
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

        await ApplyProjectAsync(imported);
        ScheduleSave();
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
