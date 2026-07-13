using System.Numerics;
using System.Runtime.CompilerServices;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Resonalyze.History;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>
/// The Virtual DSP tool: up to three measured transfer IRs are run through
/// per-channel DSP chains (gain, delay, polarity, crossover, PEQ) and summed as
/// complex responses, predicting the combined output before touching the
/// hardware. The acoustic plot shows the raw/processed channels, their complex
/// sum and the sum loss; the DSP plot shows each chain's own magnitude and
/// phase. The whole state persists as a project file across restarts.
/// </summary>
public partial class VirtualCrossoverPanel : UserControl
{
    private const string CurveSeriesTag = "virtual-crossover:curve";
    private const string CurveTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00}";
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

    private readonly List<ChannelRuntime> channels = new();
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
    private PlotWatermarkAnnotation hintAnnotation = null!;
    private LinearAxis mainValueAxis = null!;
    // The main plot's two bottom axes: the shared log-frequency axis for the
    // magnitude/phase views and a linear ms axis for the impulse view. Only
    // one is in the model at a time (ConfigureMainBottomAxis swaps them), so
    // the untagged curve series always bind to the active one.
    private LogarithmicAxis mainFrequencyAxis = null!;
    private LinearAxis mainTimeAxis = null!;
    private PlotLabelsPanelController plotLabels = null!;
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

    // Bumped whenever the displayed side (or the whole project) changes: a
    // redraw pass that started before the bump computed the OLD side's IRs,
    // and drawing them against the new side's live settings would paint one
    // mixed frame before the pending repaint corrects it. Stale passes check
    // this after their await and skip the paint instead.
    private long displayRevision;

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

        InitializeMainPlot();
        InitializeDspPlot();
        mainPlotView.Paint += (_, _) => AppProfiler.FrameMark("vdsp-main");
        dspPlotView.Paint += (_, _) => AppProfiler.FrameMark("vdsp-dsp");
        InitializeSmoothingComboBox();
        WirePanelEvents();
        InitializeToolTips();

        buttonAutoDelay.Click += (_, _) => AutoAlignDelay();
        buttonAutoSetup.Click += (_, _) => OpenAutoSetupWizard();
        buttonCaptureOverlay.Click += (_, _) => CaptureSumToOverlay();
        buttonExport.Click += (_, _) => ExportTuningSheet();
        buttonPhaseGate.Click += (_, _) => OpenPhaseGateDialog();
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
            hintAnnotation.Text = LoadingHint;
            mainPlotView.InvalidatePlot(true);
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
        displayRevision++;
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
            ConfigureMainValueAxis();
            UpdateGateButtonAvailability();
            comboBoxSmoothing.SelectedItem =
                OverlaySmoothing.IsValid(project.SmoothingInverseOctaves)
                    ? project.SmoothingInverseOctaves
                    : 12;
            radioDspMagnitude.Checked = project.DspPlotMode == DspPlotMode.Magnitude;
            radioDspPhase.Checked = project.DspPlotMode == DspPlotMode.Phase;
            radioDspGroupDelay.Checked = project.DspPlotMode == DspPlotMode.GroupDelay;

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

        foreach (ChannelRuntime channel in channels)
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
        // Three-radio group: each fires on both the check and the uncheck, so act
        // only on the one that became checked to run the switch exactly once.
        radioDspMagnitude.CheckedChanged += (_, _) =>
        {
            if (radioDspMagnitude.Checked) OnDspPlotModeChanged();
        };
        radioDspPhase.CheckedChanged += (_, _) =>
        {
            if (radioDspPhase.Checked) OnDspPlotModeChanged();
        };
        radioDspGroupDelay.CheckedChanged += (_, _) =>
        {
            if (radioDspGroupDelay.Checked) OnDspPlotModeChanged();
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
        displayRevision++;
        suppressProjectEvents = true;
        try
        {
            foreach (ChannelRuntime channel in channels)
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
        List<ChannelRuntime> candidates = channels
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
                    ? channel.Control.ChannelName
                    : $"{channel.Control.ChannelName} — {source}";
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
            ChannelRuntime channel = candidates[index];
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

    // The "what the DSP does" part of one side, copied onto the other. PeqBand
    // is an immutable record, so a fresh list is a deep enough copy.
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
    private ChannelRuntime CreateChannelControl(int index)
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

        var runtime = new ChannelRuntime(control);
        control.SettingsChanged += (_, _) => OnChannelSettingsChanged(runtime);
        control.SourceClicked += (_, _) => ShowSourceMenu(runtime);
        control.PeqLoadClicked += (_, _) => LoadPeq(runtime);
        control.PeqClearClicked += (_, _) => ClearPeq(runtime);
        return runtime;
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
            ChannelRuntime removed = channels[^1];
            channels.RemoveAt(channels.Count - 1);
            channelListPanel.Controls.Remove(removed.Control);
            removed.Control.Dispose();
        }

        while (channels.Count < count)
        {
            ChannelRuntime added = CreateChannelControl(channels.Count);
            channels.Add(added);
            channelListPanel.Controls.Add(added.Control);
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
        ChannelRuntime added = channels[^1];
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

    private void OnChannelSettingsChanged(ChannelRuntime channel)
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
        bool monoNow = channel.Control.MonoCheckBox.Checked;
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
    private async void ReresolveRightSide(ChannelRuntime channel)
    {
        try
        {
            channel.PhysicalSideState(true).Clear();
            await ResolveSourceAsync(channel, rightSide: true, showErrors: false);
            if (IsDisposed)
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
        ConfigureMainValueAxis();
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
        project.SmoothingInverseOctaves = comboBoxSmoothing.SelectedItem is int value
            ? value
            : 12;
        ScheduleSave();
        RedrawAll();
    }

    // ------------------------------------------------------- settings mapping

    private void ApplySettingsToControl(ChannelRuntime channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        VirtualCrossoverChannelControl control = channel.Control;
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
            control.LowPassFamilyComboBox.SelectedItem = settings.LowPassEdge.Family;
            control.LowPassFrequencyInput.Value = Clamp(
                control.LowPassFrequencyInput, settings.LowPassEdge.FrequencyHz);
            control.LowPassSlopeComboBox.SelectedItem = settings.LowPassEdge.SlopeDbPerOctave;
            control.ShowRawCheckBox.Checked = settings.ShowRawCurve;
            control.ShowProcessedCheckBox.Checked = settings.ShowProcessedCurve;
            control.BypassCheckBox.Checked = settings.Bypass;
            control.MonoCheckBox.Checked = channel.Pair.Mono;
            control.Muted = !settings.Enabled;
        });

        UpdateSourceButton(channel);
        UpdatePeqLabel(channel);
    }

    private void ReadControlIntoSettings(ChannelRuntime channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        VirtualCrossoverChannelControl control = channel.Control;
        settings.GainDb = (double)control.GainInput.Value;
        settings.DelayMs = (double)control.DelayInput.Value;
        settings.InvertPolarity = control.InvertCheckBox.Checked;
        settings.CrossoverKind = control.SelectedCrossoverKind;
        settings.HighPassEdge = control.HighPassEdge;
        settings.LowPassEdge = control.LowPassEdge;
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

    private void ShowSourceMenu(ChannelRuntime channel)
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

        menu.Show(channel.Control.SourceButton, new Point(0, channel.Control.SourceButton.Height));
    }

    private void PopulateHistoryMenu(ToolStripMenuItem historyItem, ChannelRuntime channel)
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

    private async Task ChooseSourceFileAsync(ChannelRuntime channel)
    {
        // The CONCRETE slot and settings are captured NOW: the user can flip
        // the L/R selector, toggle Mono (which reroutes SideState) — or
        // import a whole different session — while the file loads below, and
        // the measurement must land in the slot whose Source button was
        // clicked, or nowhere at all. The revision (taken when the load
        // starts) guards the landing: any Clear() of the slot or a newer
        // pick into it refuses this one.
        bool rightSide = channel.ActiveRight;
        ChannelSideState targetState = channel.SideState(rightSide);
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
            if (!TryAcceptSource(
                channel,
                rightSide,
                targetState,
                targetSettings,
                revision,
                snapshot,
                Path.GetFileName(dialog.FileName),
                dialog.FileName,
                historyEntryId: null))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError("Failed to load the impulse response.", exception.Message);
        }
    }

    private async Task SelectHistoryEntryAsync(ChannelRuntime channel, Guid entryId)
    {
        // Same slot/settings/revision capture as ChooseSourceFileAsync: the
        // snapshot load is asynchronous and the L/R selector, the Mono
        // checkbox and session import all stay live meanwhile.
        bool rightSide = channel.ActiveRight;
        ChannelSideState targetState = channel.SideState(rightSide);
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

            TryAcceptSource(
                channel,
                rightSide,
                targetState,
                targetSettings,
                revision,
                snapshot,
                entry.FileNameOrDisplayName,
                entry.SourceFilePath,
                entryId);
        }
        catch (Exception exception)
        {
            ShowError("Failed to load the history entry.", exception.Message);
        }
    }

    // Validates and installs a resolved measurement as the channel's source. The
    // virtual sum only has physical meaning for loopback-referenced transfer IRs
    // sharing one sample rate, so both are enforced here with an explanation.
    // The target slot and settings are the caller's PRE-AWAIT captures — never
    // re-derived here, where a mid-load Mono toggle would reroute them — and
    // the revision refuses a landing the slot has moved past (cleared by a
    // project import or mono toggle, or superseded by a newer pick).
    private bool TryAcceptSource(
        ChannelRuntime channel,
        bool rightSide,
        ChannelSideState targetState,
        VirtualCrossoverChannelSettings targetSettings,
        int sourceRevision,
        MeasurementHistorySnapshot snapshot,
        string displayName,
        string? sourceFilePath,
        Guid? historyEntryId)
    {
        if (targetState.SourceRevision != sourceRevision)
        {
            return false;
        }

        if (snapshot.TransferImpulseResponse is not { Length: > 0 } transferIr)
        {
            ShowError(
                "This measurement has no loopback transfer IR.",
                "The virtual crossover sums loopback-referenced responses; " +
                "re-measure with a loopback channel configured.");
            return false;
        }

        // A mismatched sample rate is not a dead end: the user picks whether the
        // new measurement wins (clearing the incompatible sides) or loses. The
        // compatibility decision is shared with the silent reload path, and it
        // scans EVERY resolved side of every pair — the virtual sums of both
        // sides read one shared rate.
        VirtualCrossoverSourceRules.Decision decision = VirtualCrossoverSourceRules.Evaluate(
            hasTransferIr: true,
            candidateSampleRate: snapshot.SampleRate,
            otherResolvedSampleRates: ResolvedSidesExcept(targetState)
                .Select(item => item.State.SampleRate));
        if (decision == VirtualCrossoverSourceRules.Decision.NeedsConfirmClear)
        {
            var mismatched = ResolvedSidesExcept(targetState)
                .Where(item => item.State.SampleRate != snapshot.SampleRate)
                .ToList();
            string mismatchedList = string.Join(
                ", ",
                mismatched.Select(item =>
                    $"{SideLabel(item.Channel, item.RightSide)} " +
                    $"({item.State.SampleRate} Hz)"));
            DialogResult answer = MessageBox.Show(
                FindForm(),
                $"This measurement is {snapshot.SampleRate} Hz, but channel " +
                $"{mismatchedList} uses a different sample rate. All channels " +
                "must share one." +
                Environment.NewLine + Environment.NewLine +
                "Assign it anyway and clear the mismatched channel sources?",
                "Virtual DSP",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return false;
            }

            // The modal dialog pumped messages: a project-import continuation
            // may have wiped the slot underneath it while the user decided.
            if (targetState.SourceRevision != sourceRevision)
            {
                return false;
            }

            foreach ((ChannelRuntime other, bool otherRight, _) in mismatched)
            {
                ClearSourceCore(other, otherRight);
            }
        }

        targetState.TransferImpulseResponse = transferIr;
        targetState.ProcessedCache = null;
        targetState.TransferPeakIndex = Math.Clamp(
            snapshot.TransferPeakIndex ?? 0, 0, transferIr.Length - 1);
        targetState.SampleRate = snapshot.SampleRate;
        targetState.TransferCoherence = snapshot.TransferCoherence;
        targetState.DistortionCurve = ComputeDistortionCurve(snapshot);
        targetSettings.DisplayName = displayName;
        targetSettings.SourceFilePath = sourceFilePath;
        targetSettings.HistoryEntryId = historyEntryId;

        UpdateSourceButton(channel);
        UpdateSideRadioTexts();
        ScheduleSave();
        RedrawAll();
        return true;
    }

    // Every resolved (channel, side) except the given side state; mono pairs
    // expose only their single left-side slot.
    private IEnumerable<(ChannelRuntime Channel, bool RightSide, ChannelSideState State)>
        ResolvedSidesExcept(ChannelSideState? except)
    {
        foreach (ChannelRuntime channel in channels)
        {
            foreach (bool rightSide in new[] { false, true })
            {
                if (channel.Pair.Mono && rightSide)
                {
                    continue;
                }

                ChannelSideState state = channel.SideState(rightSide);
                if (state != except && state.TransferImpulseResponse != null)
                {
                    yield return (channel, rightSide, state);
                }
            }
        }
    }

    private static string SideLabel(ChannelRuntime channel, bool rightSide) =>
        channel.Pair.Mono
            ? $"{channel.Control.ChannelName} (mono)"
            : $"{channel.Control.ChannelName} {(rightSide ? "R" : "L")}";

    private void ClearSource(ChannelRuntime channel)
    {
        ClearSourceCore(channel, channel.ActiveRight);
        ScheduleSave();
        RedrawAll();
    }

    private void ClearSourceCore(ChannelRuntime channel, bool rightSide)
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
        ChannelRuntime channel, bool rightSide, bool showErrors)
    {
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        ChannelSideState state = channel.SideState(rightSide);
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
            MeasurementHistorySnapshot? snapshot = null;
            if (settings.HistoryEntryId is { } entryId && HistoryService != null)
            {
                snapshot = await HistoryService.GetSnapshotAsync(entryId);
            }
            if (snapshot == null &&
                !string.IsNullOrWhiteSpace(settings.SourceFilePath) &&
                File.Exists(settings.SourceFilePath))
            {
                ImpulseResponseFile file =
                    await ImpulseResponseFile.LoadAsync(settings.SourceFilePath);
                snapshot = MeasurementHistoryService.CreateSnapshot(file);
            }

            // The same compatibility decision as TryAcceptSource (the file behind a
            // stored path may have been replaced since the project was saved), but
            // here an incompatible source stays unresolved — the button shows the
            // warning glyph — instead of prompting: a silent reload cannot ask.
            VirtualCrossoverSourceRules.Decision decision = VirtualCrossoverSourceRules.Evaluate(
                hasTransferIr: snapshot?.TransferImpulseResponse is { Length: > 0 },
                candidateSampleRate: snapshot?.SampleRate ?? 0,
                otherResolvedSampleRates: ResolvedSidesExcept(state)
                    .Select(item => item.State.SampleRate));
            if (decision == VirtualCrossoverSourceRules.Decision.Accept &&
                snapshot?.TransferImpulseResponse is { Length: > 0 } transferIr &&
                state.SourceRevision == revision)
            {
                state.TransferImpulseResponse = transferIr;
                state.ProcessedCache = null;
                state.TransferPeakIndex = Math.Clamp(
                    snapshot.TransferPeakIndex ?? 0, 0, transferIr.Length - 1);
                state.SampleRate = snapshot.SampleRate;
                state.TransferCoherence = snapshot.TransferCoherence;
                state.DistortionCurve = ComputeDistortionCurve(snapshot);
            }
        }
        catch (Exception exception) when (!showErrors)
        {
            _ = exception;
        }
    }

    private void UpdateSourceButton(ChannelRuntime channel)
    {
        string? name = channel.Settings.DisplayName;
        bool resolved = channel.TransferImpulseResponse != null;
        channel.Control.SourceButton.Text = string.IsNullOrWhiteSpace(name)
            ? "Source..."
            : resolved ? name : $"⚠ {name}";
        // The as-measured driver polarity, read from the raw transfer IR (the
        // Invert switch is a separate, virtual stage on top of it).
        channel.Control.SetMeasuredPolarity(
            channel.TransferImpulseResponse is { } ir
                ? VirtualCrossoverAnalysis.EstimatePolarity(ir)
                : PolarityEstimate.Unknown);
        toolTip.SetToolTip(
            channel.Control.SourceButton,
            resolved
                ? channel.Settings.SourceFilePath ?? name
                : "Pick the channel's measurement: a saved impulse-response\r\n" +
                  "file or a history entry.\r\n" +
                  "Requires a loopback transfer IR.");
    }

    // -------------------------------------------------------------------- PEQ

    private void LoadPeq(ChannelRuntime channel)
    {
        IReadOnlyList<IEqProfileFormat> formats = EqProfileFormats.Importable;
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = EqFormatFileDialogs.BuildFilter(formats),
            Title = $"Load channel {channel.Control.ChannelName} PEQ"
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

    private void ClearPeq(ChannelRuntime channel)
    {
        channel.Settings.PeqBands = new List<PeqBand>();
        channel.Settings.PeqPreampDb = 0;
        channel.Settings.PeqSourceName = null;
        UpdatePeqLabel(channel);
        ScheduleSave();
        RedrawAll();
    }

    private void UpdatePeqLabel(ChannelRuntime channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        bool noPeq = settings.PeqBands.Count == 0 && settings.PeqPreampDb == 0;
        string text = noPeq
            ? "No PEQ"
            : $"{settings.PeqSourceName ?? "PEQ"}: {settings.PeqBands.Count} bands, " +
              $"preamp {settings.PeqPreampDb:0.0} dB";
        channel.Control.PeqInfoLabel.Text = text;
        // The label is narrow and clips the file name; the full text lives in the
        // tooltip. Nothing worth hovering when there is no PEQ.
        toolTip.SetToolTip(channel.Control.PeqInfoLabel, noPeq ? string.Empty : text);
    }

    // ------------------------------------------------------------------ plots

    private void InitializeMainPlot()
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        mainFrequencyAxis = (LogarithmicAxis)model.Axes[^1];
        // The impulse view runs on an absolute-time axis; its range follows
        // the gate window on every impulse redraw, so it is static like the
        // gate dialog's preview instead of pannable.
        mainTimeAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "ms",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            IsPanEnabled = false,
            IsZoomEnabled = false
        };
        // The absolute pan/zoom limits live in ConfigureMainValueAxis: they
        // differ between the magnitude (dB), phase (deg) and impulse
        // (normalized) views.
        mainValueAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        };
        model.Axes.Add(mainValueAxis);
        ConfigureMainValueAxis();

        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "Virtual DSP",
            TextColor = OxyColor.FromAColor(10, OxyColors.White),
            FontSize = 70,
            FontWeight = FontWeights.Bold
        });
        hintAnnotation = new PlotWatermarkAnnotation
        {
            Text = NoSourcesHint,
            VerticalPosition = 0.66,
            TextColor = OxyColor.FromRgb(230, 184, 0),
            FontSize = 15,
            FontWeight = FontWeights.Bold
        };
        model.Annotations.Add(hintAnnotation);

        mainPlotView.Model = model;
        PlotInteraction.EnableDoubleClickAxisReset(mainPlotView);
        plotLabels = new PlotLabelsPanelController(mainPlotView, () => Mode.VirtualCrossover);
    }

    // Magnitude and phase reuse one axis object so pan/zoom of the frequency
    // axis survives the toggle; only the value scale is re-armed. The impulse
    // view additionally swaps the bottom axis to the linear ms one.
    private void ConfigureMainValueAxis()
    {
        if (radioViewImpulse.Checked)
        {
            // Every trace is normalized to its own peak, exactly like the IR
            // Gate preview, so the scale is unitless.
            mainValueAxis.Title = string.Empty;
            mainValueAxis.AbsoluteMinimum = -1.05;
            mainValueAxis.AbsoluteMaximum = 1.05;
            mainValueAxis.Minimum = -1.05;
            mainValueAxis.Maximum = 1.05;
            mainValueAxis.MajorStep = 0.5;
        }
        else if (radioViewPhase.Checked)
        {
            mainValueAxis.Title = "deg";
            mainValueAxis.AbsoluteMinimum = -180;
            mainValueAxis.AbsoluteMaximum = 180;
            mainValueAxis.Minimum = -180;
            mainValueAxis.Maximum = 180;
            mainValueAxis.MajorStep = 45;
        }
        else
        {
            mainValueAxis.Title = "dB";
            mainValueAxis.AbsoluteMinimum = -90;
            mainValueAxis.AbsoluteMaximum = 20;
            mainValueAxis.Minimum = double.NaN;
            mainValueAxis.Maximum = double.NaN;
            mainValueAxis.MajorStep = double.NaN;
        }

        ConfigureMainBottomAxis();
        mainValueAxis.Reset();
        mainPlotView.InvalidatePlot(false);
    }

    // Keeps exactly one bottom axis in the model: the log-frequency axis for
    // the magnitude/phase views, the linear ms axis for the impulse view.
    // Swapping whole axis objects (instead of reconfiguring one) preserves
    // each view's own range across toggles.
    private void ConfigureMainBottomAxis()
    {
        if (mainPlotView.Model is not { } model)
        {
            return;
        }

        Axis wanted = radioViewImpulse.Checked ? mainTimeAxis : mainFrequencyAxis;
        Axis retired = radioViewImpulse.Checked ? mainFrequencyAxis : mainTimeAxis;
        if (!model.Axes.Contains(wanted))
        {
            model.Axes.Remove(retired);
            model.Axes.Add(wanted);
        }
    }

    private void InitializeDspPlot()
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        // One value axis, reconfigured per plot mode (magnitude / phase / group
        // delay). A single axis keeps each mode readable on its own scale.
        model.Axes.Add(new LinearAxis
        {
            Key = DspValueAxisKey,
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        });
        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "DSP chains",
            TextColor = OxyColor.FromAColor(10, OxyColors.White),
            FontSize = 40,
            FontWeight = FontWeights.Bold
        });

        ConfigureDspValueAxis((LinearAxis)model.Axes[^1], CurrentDspPlotMode());
        dspPlotView.Model = model;
        PlotInteraction.EnableDoubleClickAxisReset(dspPlotView);
    }

    private const string DspValueAxisKey = "dsp-value";

    // Tracks the mode the value axis range was last set for, so switching modes
    // resets the range to the new mode's default while an in-mode redraw (e.g.
    // editing a filter) preserves the user's zoom/pan.
    private DspPlotMode? dspValueAxisMode;

    private DspPlotMode CurrentDspPlotMode() =>
        radioDspPhase.Checked ? DspPlotMode.Phase
        : radioDspGroupDelay.Checked ? DspPlotMode.GroupDelay
        : DspPlotMode.Magnitude;

    // Titles the value axis and, only when the mode actually changed, resets its
    // range to that mode's sensible default.
    private void ConfigureDspValueAxis(LinearAxis axis, DspPlotMode mode)
    {
        axis.Title = mode switch
        {
            DspPlotMode.Phase => "deg",
            DspPlotMode.GroupDelay => "ms",
            _ => "dB"
        };

        if (dspValueAxisMode == mode)
        {
            return;
        }

        dspValueAxisMode = mode;
        switch (mode)
        {
            case DspPlotMode.Phase:
                axis.Minimum = -190;
                axis.Maximum = 190;
                axis.MajorStep = 90;
                break;
            case DspPlotMode.GroupDelay:
                // Group delay range varies widely with the filters, so let it
                // auto-scale to the drawn curves.
                axis.Minimum = double.NaN;
                axis.Maximum = double.NaN;
                axis.MajorStep = double.NaN;
                break;
            default:
                axis.Minimum = -60;
                axis.Maximum = 20;
                axis.MajorStep = double.NaN;
                break;
        }

        axis.Reset();
    }

    private void OnDspPlotModeChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        project.DspPlotMode = CurrentDspPlotMode();
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
            "Fractional-octave smoothing of the magnitude curves.");
        toolTip.SetToolTip(
            radioDspGroupDelay,
            "What the lower plot shows for each channel's DSP chain:\r\n" +
            "Magnitude, Phase, or filter Group delay (the crossover/PEQ\r\n" +
            "group delay in ms, excluding the channel's bulk delay).");
        toolTip.SetToolTip(
            comboBoxCalibration,
            "Microphone calibration applied to the magnitude curves.\r\n" +
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
            "RIGHT side for the channels you pick. Sources and delays\r\n" +
            "stay with their side; mono channels are not offered.");
        toolTip.SetToolTip(
            buttonCopyRightToLeft,
            "Copy the RIGHT side's gain, crossover and PEQ onto the\r\n" +
            "LEFT side for the channels you pick. Sources and delays\r\n" +
            "stay with their side; mono channels are not offered.");
        toolTip.SetToolTip(
            buttonAutoSetup,
            "Crossover wizard: detect each channel's driver type from\r\n" +
            "its response, confirm the types, and get a starting point —\r\n" +
            "LR24 splits where the responses intersect and cut-only\r\n" +
            "gains that level the channels.\r\n" +
            "Run Auto delay afterward to phase-align the result.");
        toolTip.SetToolTip(
            buttonPhaseGate,
            "Configure the phase-view gate: offset and Tukey fades,\r\n" +
            "with an IR preview — cut the window before the first\r\n" +
            "reflection for clean phase traces.");
        toolTip.SetToolTip(
            buttonSessionExport,
            "Save the whole session (sources, DSP chains, gate, view)\r\n" +
            "to a file to share or archive it.");
        toolTip.SetToolTip(
            buttonSessionImport,
            "Load a saved session file, replacing the current state.\r\n" +
            "Sources are re-resolved from history or their file paths.");
        foreach (ChannelRuntime channel in channels)
        {
            toolTip.SetToolTip(
                channel.Control.GainInput,
                "Channel gain (dB).\r\n" +
                "Relative levels are only honest when the measurements\r\n" +
                "were captured through the same playback chain;\r\n" +
                "compensate any difference here.");
            toolTip.SetToolTip(
                channel.Control.InvertCheckBox,
                "Invert the channel polarity — the DSP polarity switch.\r\n" +
                "Also the null test: with polarity flipped, the deepest\r\n" +
                "notch at the crossover frequency marks perfect alignment.");
            toolTip.SetToolTip(
                channel.Control.DelayInput,
                "Channel delay (ms) — the value you would dial into\r\n" +
                "this DSP channel.\r\n" +
                "The mm readout is the equivalent distance in air.");
            toolTip.SetToolTip(
                channel.Control.MuteButton,
                "Mute the channel: exclude it from the sum, the loss,\r\n" +
                "the metric, Auto delay and both plots — a quick\r\n" +
                "\"what changes without this driver\" check.");
            toolTip.SetToolTip(
                channel.Control.BypassCheckBox,
                "Bypass the DSP chain: feed the raw measured signal with\r\n" +
                "no gain, delay, polarity, crossover or PEQ — the driver's\r\n" +
                "natural band-pass, for an A/B against the processed result.");
            toolTip.SetToolTip(
                channel.Control.MonoCheckBox,
                "One physical driver serving both sides (typically the\r\n" +
                "subwoofer): a single set of settings participates in the\r\n" +
                "L and R views and calculations alike. The stereo Auto\r\n" +
                "delay tunes it with the left side and reports the right\r\n" +
                "junction it pins.");
            toolTip.SetToolTip(
                channel.Control.MeasuredPolarityLabel,
                "Acoustic polarity read from the measured IR\r\n" +
                "(the sign of its first significant excursion).\r\n" +
                "Normal — the driver pushes toward the mic first.\r\n" +
                "Inverted — it pulls first (wired in reverse).\r\n" +
                "Unknown — no source selected.\r\n" +
                "Independent of the Invert switch.");
        }
    }

    // ---------------------------------------------------------------- redraw

    private void RedrawAll()
    {
        using var _ = AppProfiler.Zone("VirtualDSP.RedrawAll");
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
        if (redrawTask is { IsCompleted: false })
        {
            redrawPending = true;
            return;
        }

        redrawTask = RunRedrawLoopAsync();
    }

    // The redraw loop. Only the ApplyChain FFTs inside ProcessChannelsAsync leave
    // the UI thread; the loop bookkeeping, the cache and the OxyPlot updates all
    // run here on the UI thread. It repeats while changes kept arriving during the
    // last pass, collapsing a burst of edits into a single trailing redraw.
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

    private sealed record ProcessedChannel(
        ChannelRuntime Channel,
        Complex[] ImpulseResponse,
        int PeakIndex,
        OxyColor Color);

    private sealed class ProcessedChannelCacheKey : IEquatable<ProcessedChannelCacheKey>
    {
        private readonly Complex[] source;
        private readonly int sampleRate;
        private readonly double gainDb;
        private readonly double delayMs;
        private readonly bool invertPolarity;
        private readonly CrossoverSpec? crossover;
        private readonly double peqPreampDb;
        private readonly PeqBand[] peqBands;

        public ProcessedChannelCacheKey(
            Complex[] source,
            int sampleRate,
            DspChannelChain chain)
        {
            this.source = source;
            this.sampleRate = sampleRate;
            gainDb = chain.GainDb;
            delayMs = chain.DelayMs;
            invertPolarity = chain.InvertPolarity;
            crossover = chain.Crossover;
            peqPreampDb = chain.Peq?.PreampDb ?? 0;
            peqBands = chain.Peq?.Bands.ToArray() ?? Array.Empty<PeqBand>();
        }

        public bool Equals(ProcessedChannelCacheKey? other) =>
            other != null &&
            ReferenceEquals(source, other.source) &&
            sampleRate == other.sampleRate &&
            gainDb == other.gainDb &&
            delayMs == other.delayMs &&
            invertPolarity == other.invertPolarity &&
            EqualityComparer<CrossoverSpec?>.Default.Equals(crossover, other.crossover) &&
            peqPreampDb == other.peqPreampDb &&
            peqBands.SequenceEqual(other.peqBands);

        public override bool Equals(object? obj) =>
            obj is ProcessedChannelCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(RuntimeHelpers.GetHashCode(source));
            hash.Add(sampleRate);
            hash.Add(gainDb);
            hash.Add(delayMs);
            hash.Add(invertPolarity);
            hash.Add(crossover);
            hash.Add(peqPreampDb);
            foreach (PeqBand band in peqBands)
            {
                hash.Add(band);
            }

            return hash.ToHashCode();
        }
    }

    private List<ProcessedChannel> ProcessChannels(
        IReadOnlyDictionary<ChannelRuntime, AlignmentOverride>? alignmentOverrides = null)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.ProcessChannels");
        var processed = new List<ProcessedChannel>();
        for (int i = 0; i < channels.Count; i++)
        {
            ChannelRuntime channel = channels[i];
            // A muted channel drops out of everything downstream: the plots, the
            // sum, the loss, the metric, Auto delay, and Capture to overlay.
            if (!channel.Settings.Enabled ||
                channel.TransferImpulseResponse is not { } ir)
            {
                continue;
            }

            DspChannelChain chain;
            using (AppProfiler.Zone("VirtualDSP.ProcessChannels.BuildChain"))
            {
                if (channel.Settings.Bypass)
                {
                    // Bypass feeds the raw measured signal: the identity chain (no
                    // gain, delay, polarity, crossover or PEQ), so even Auto delay's
                    // alignment overrides do not move it.
                    chain = DspChannelChain.Identity;
                }
                else
                {
                    chain = channel.Settings.ToChain();
                    if (alignmentOverrides != null)
                    {
                        AlignmentOverride alignment = alignmentOverrides.TryGetValue(
                            channel,
                            out AlignmentOverride value)
                            ? value
                            : new AlignmentOverride(0, false);
                        chain = chain with
                        {
                            DelayMs = alignment.DelayMs,
                            InvertPolarity = alignment.InvertPolarity
                        };
                    }
                }
            }

            // The shared per-channel cache serves interactive redraws (UI thread
            // only). The overrides path (Auto delay's pre-scan with zeroed
            // delays) does not cache: the actual search runs its own cropped,
            // thread-confined pipeline in ComputeAutoAlignment /
            // ComputeStereoAlignment.
            var cacheKey = new ProcessedChannelCacheKey(ir, channel.SampleRate, chain);
            ProcessedChannelCache? cached = alignmentOverrides == null
                ? channel.ProcessedCache
                : null;
            if (cached?.Key.Equals(cacheKey) == true)
            {
                using (AppProfiler.Zone("VirtualDSP.ProcessChannels.CacheHit"))
                {
                    processed.Add(new ProcessedChannel(
                        channel,
                        cached.ImpulseResponse,
                        cached.PeakIndex,
                        ChannelColors[i]));
                }

                continue;
            }

            Complex[] result;
            using (AppProfiler.Zone("VirtualDSP.ProcessChannels.ApplyChain"))
            {
                result = VirtualCrossoverAnalysis.ApplyChain(
                    ir, chain, channel.SampleRate);
            }

            int peakIndex;
            using (AppProfiler.Zone("VirtualDSP.ProcessChannels.FindPeak"))
            {
                peakIndex = VirtualCrossoverAnalysis.FindPeakIndex(result);
            }

            if (alignmentOverrides == null)
            {
                channel.ProcessedCache = new ProcessedChannelCache(
                    cacheKey, result, peakIndex);
            }

            using (AppProfiler.Zone("VirtualDSP.ProcessChannels.AddResult"))
            {
                processed.Add(new ProcessedChannel(
                    channel,
                    result,
                    peakIndex,
                    ChannelColors[i]));
            }
        }

        return processed;
    }

    // One channel whose processed IR the cache does not already hold, snapshotted
    // so the background task reads nothing but the immutable transfer IR and the
    // value-typed chain. The concrete side STATE is captured too: the cache
    // write happens after an await, and by then the active side may have
    // flipped — writing through the runtime's delegating property would then
    // stamp one side's FFT into the other side's cache slot.
    private sealed record PendingChannel(
        int Index,
        ChannelRuntime Channel,
        ChannelSideState State,
        Complex[] TransferIr,
        int SampleRate,
        DspChannelChain Chain,
        ProcessedChannelCacheKey Key,
        OxyColor Color);

    // The interactive-redraw variant of ProcessChannels: the cache is read and
    // written here on the UI thread, and only the FFT-heavy ApplyChain runs on a
    // background task. Results come back in channel order. There is no
    // alignment-override path — Auto delay keeps using the synchronous
    // ProcessChannels.
    private async Task<List<ProcessedChannel>> ProcessChannelsAsync()
    {
        using var _ = AppProfiler.Zone("VirtualDSP.ProcessChannelsAsync");
        var results = new ProcessedChannel?[channels.Count];
        var jobs = new List<PendingChannel>();
        for (int i = 0; i < channels.Count; i++)
        {
            ChannelRuntime channel = channels[i];
            ChannelSideState state = channel.SideState(channel.ActiveRight);
            if (!channel.Settings.Enabled ||
                state.TransferImpulseResponse is not { } ir)
            {
                continue;
            }

            DspChannelChain chain = channel.Settings.Bypass
                ? DspChannelChain.Identity
                : channel.Settings.ToChain();
            var key = new ProcessedChannelCacheKey(ir, state.SampleRate, chain);
            if (state.ProcessedCache?.Key.Equals(key) == true)
            {
                results[i] = new ProcessedChannel(
                    channel,
                    state.ProcessedCache.ImpulseResponse,
                    state.ProcessedCache.PeakIndex,
                    ChannelColors[i]);
                continue;
            }

            jobs.Add(new PendingChannel(
                i, channel, state, ir, state.SampleRate, chain, key,
                ChannelColors[i]));
        }

        if (jobs.Count > 0)
        {
            // One ApplyChain per channel — the full-length IR through the biquad
            // cascade and its FFT — is the tool's heaviest math. They are pure
            // and independent, so they run across cores; the cache write-back
            // below stays on the UI thread after the await, so nothing races.
            var computed = new (Complex[] Result, int Peak)[jobs.Count];
            await Task.Run(() => Parallel.For(0, jobs.Count, j =>
            {
                PendingChannel job = jobs[j];
                Complex[] result = VirtualCrossoverAnalysis.ApplyChain(
                    job.TransferIr, job.Chain, job.SampleRate);
                computed[j] = (result, VirtualCrossoverAnalysis.FindPeakIndex(result));
            }));

            for (int j = 0; j < jobs.Count; j++)
            {
                PendingChannel job = jobs[j];
                (Complex[] result, int peak) = computed[j];
                job.State.ProcessedCache = new ProcessedChannelCache(job.Key, result, peak);
                results[job.Index] = new ProcessedChannel(job.Channel, result, peak, job.Color);
            }
        }

        return results.Where(item => item != null).Select(item => item!).ToList();
    }

    private async Task RedrawMainPlotAsync()
    {
        using var _ = AppProfiler.Zone("VirtualDSP.RedrawMainPlot");
        PlotModel? model = mainPlotView.Model;
        if (model == null)
        {
            return;
        }

        // The heavy ApplyChain FFTs run off the UI thread; the existing curves stay
        // on screen until the new data is ready, so there is no clear-then-fill
        // flicker during the compute.
        long revision = displayRevision;
        List<ProcessedChannel> processed = await ProcessChannelsAsync();
        if (mainPlotView.IsDisposed)
        {
            return;
        }
        if (revision != displayRevision)
        {
            // The displayed side flipped mid-compute: these IRs belong to the
            // previous side, and the side switch has already queued a repaint.
            return;
        }

        // The stereo Δ block and the opposite-side sum read BOTH sides'
        // processed responses; their caches make an unchanged configuration
        // free. Same staleness rule as above.
        List<VirtualCrossoverMetric.StereoDelta> stereoDeltas =
            await ComputeStereoDeltasAsync();
        AnalysisCurve? oppositeSum =
            checkBoxShowSum.Checked && radioViewMagnitude.Checked
                ? await ComputeOppositeSumCurveAsync()
                : null;
        if (mainPlotView.IsDisposed || revision != displayRevision)
        {
            return;
        }

        RemoveCurveSeries(model);
        // While a session loads, interim redraws (the calibration combo refresh,
        // etc.) run before the sources resolve, so processed is empty then; keep
        // the loading note instead of flashing the "no sources" hint.
        hintAnnotation.Text = loadingProject
            ? LoadingHint
            : processed.Count == 0 ? NoSourcesHint : string.Empty;

        // The processed magnitudes and the complex sum feed both the drawn
        // curves and the sum-loss metric, so they are built once here.
        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sumCurve) =
            BuildMetricCurves(processed);

        UpdateMetric(processed, magnitudes, sumCurve, stereoDeltas);
        UpdateCrossoverWarning(processed);

        if (processed.Count > 0)
        {
            if (radioViewPhase.Checked)
            {
                DrawPhaseCurves(model, processed);
            }
            else if (radioViewImpulse.Checked)
            {
                DrawImpulseCurves(model, processed);
            }
            else
            {
                DrawMagnitudeCurves(model, processed, magnitudes, sumCurve, oppositeSum);
            }
        }

        plotLabels.Refresh();
        model.InvalidatePlot(true);
    }

    private void DrawMagnitudeCurves(
        PlotModel model,
        List<ProcessedChannel> processed,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve,
        AnalysisCurve? oppositeSumCurve = null)
    {
        for (int i = 0; i < processed.Count; i++)
        {
            ProcessedChannel item = processed[i];
            if (item.Channel.Settings.ShowRawCurve)
            {
                AnalysisCurve raw = BuildMagnitudeCurve(
                    item.Channel.TransferImpulseResponse!,
                    item.Channel.TransferPeakIndex,
                    item.Channel.SampleRate);
                AddCurve(
                    model,
                    $"{item.Channel.Control.ChannelName} raw",
                    raw.Points,
                    OxyColor.FromAColor(90, item.Color),
                    1.2,
                    LineStyle.Solid);
            }

            if (item.Channel.Settings.ShowProcessedCurve)
            {
                AnalysisCurve curve = magnitudes != null
                    ? magnitudes[i]
                    : BuildMagnitudeCurve(
                        item.ImpulseResponse, item.PeakIndex, item.Channel.SampleRate);
                AddCurve(
                    model,
                    item.Channel.Control.ChannelName,
                    curve.Points,
                    item.Color,
                    1.8,
                    LineStyle.Solid);
            }
        }

        if (magnitudes == null || sumCurve == null)
        {
            return;
        }

        if (checkBoxShowSum.Checked)
        {
            AddCurve(model, "Sum", sumCurve.Points, SumColor, 2.4, LineStyle.Solid);
            if (oppositeSumCurve != null)
            {
                // The other side's sum, dashed and translucent: the two tunes
                // compare at a glance without flipping the L/R selector.
                AddCurve(
                    model,
                    $"Sum {(project.ActiveSideRight ? "L" : "R")}",
                    oppositeSumCurve.Points,
                    OxyColor.FromAColor(110, SumColor),
                    1.8,
                    LineStyle.Dash);
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
            AddCurve(model, "Sum loss", points, LossColor, 1.8, LineStyle.Dash);
        }
    }

    // ------------------------------------------------- metric and auto delay

    // The frequency window the metric and Auto delay operate in: around the
    // corner frequencies the channels actually use (one octave to each side),
    // or a broad midband default when no crossover is configured yet.
    private static (double MinHz, double MaxHz) GetCrossoverWindow(
        List<ProcessedChannel> processed) =>
        VirtualCrossoverJunctions.GetCrossoverWindow(
            processed.Select(item => item.Channel.Settings));

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
        List<VirtualCrossoverMetric.Entry> entries = BuildMetricEntries(processed, magnitudes, sumCurve);
        string compact = FormatMetricCompact(entries);
        string detail = entries.Count > 0 ? FormatMetricDetail(entries) : string.Empty;
        if (stereoDeltas is { Count: > 0 })
        {
            compact += "\r\n\r\n" +
                VirtualCrossoverMetric.FormatStereoDeltasCompact(stereoDeltas);
            detail += (detail.Length > 0 ? "\r\n\r\n" : string.Empty) +
                VirtualCrossoverMetric.FormatStereoDeltasDetail(stereoDeltas);
        }

        MetricChanged?.Invoke(compact, detail);
    }

    // One channel side snapshotted on the UI thread for background processing
    // (the stereo Δ read-out and the opposite-side sum): the background pass
    // reads nothing mutable. Processed/Arrival start from the side's caches
    // and are filled on a miss.
    private sealed class SideProcessJob
    {
        public required ChannelSideState State { get; init; }
        public required Complex[] TransferIr { get; init; }
        public required int SampleRate { get; init; }
        public required DspChannelChain Chain { get; init; }
        public required ProcessedChannelCacheKey Key { get; init; }
        public Complex[]? ProcessedIr { get; set; }
        public int ProcessedPeak { get; set; }
        public bool ProcessedFromCache { get; set; }
        public TimeAlignmentAnalysisResult? Arrival { get; set; }
        public double? LevelDb { get; set; }
        public bool ArrivalFromCache { get; set; }
    }

    private sealed record StereoDeltaJob(
        string Channel,
        double LowHz,
        double HighHz,
        SideProcessJob Left,
        SideProcessJob Right,
        bool Mono = false)
    {
        // A mono job's Left and Right are the same instance; iterate the left
        // slot alone so the shared response is processed once.
        public IEnumerable<SideProcessJob> Sides =>
            Mono ? new[] { Left } : new[] { Left, Right };
    }

    /// <summary>
    /// The final per-pair L−R timing: both sides' fully processed responses
    /// (current delays included) get their band-limited envelope arrival read
    /// in the pair's shared band, and the difference (positive: right leads —
    /// the scene-offset convention) feeds the metric read-out. A mono channel
    /// (the shared sub) has one response, so it reports that single arrival in
    /// its own band with "—" for the right side and the delta; a stereo pair
    /// needs both sides present and unbypassed. Heavy work runs off the UI
    /// thread and both the processed IRs and the arrivals are cached per side,
    /// so an unchanged configuration costs nothing on redraw.
    /// </summary>
    private async Task<List<VirtualCrossoverMetric.StereoDelta>> ComputeStereoDeltasAsync()
    {
        var jobs = new List<StereoDeltaJob>();
        foreach (ChannelRuntime channel in channels)
        {
            // A mono channel (the shared sub) has one physical response and no
            // L/R timing to compare, but its own arrival is still worth showing:
            // it reads in its own band on the left slot and prints "—" for R and
            // the delta. A stereo pair needs both sides present and unbypassed.
            bool mono = channel.Pair.Mono;

            VirtualCrossoverChannelSettings leftSettings = channel.SideSettings(false);
            ChannelSideState leftState = channel.PhysicalSideState(false);
            if (!leftSettings.Enabled || leftSettings.Bypass ||
                leftState.TransferImpulseResponse is not { } leftIr)
            {
                continue;
            }

            VirtualCrossoverChannelSettings rightSettings = channel.SideSettings(true);
            ChannelSideState rightState = channel.PhysicalSideState(true);
            if (!mono &&
                (!rightSettings.Enabled || rightSettings.Bypass ||
                    rightState.TransferImpulseResponse is not { }))
            {
                continue;
            }

            (double leftLow, double leftHigh) =
                VirtualCrossoverJunctions.GetChannelBand(leftSettings);
            double lowHz, highHz;
            if (mono)
            {
                lowHz = leftLow;
                highHz = leftHigh;
            }
            else
            {
                (double rightLow, double rightHigh) =
                    VirtualCrossoverJunctions.GetChannelBand(rightSettings);
                lowHz = Math.Max(leftLow, rightLow);
                highHz = Math.Min(leftHigh, rightHigh);
            }
            if (highHz < lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
            {
                // The arrival analysis refuses a band this narrow (it is not
                // widened behind the caller's back), so the row would only
                // ever read "—" — leave it out entirely.
                continue;
            }

            SideProcessJob Snapshot(
                ChannelSideState state,
                VirtualCrossoverChannelSettings settings,
                Complex[] ir)
            {
                DspChannelChain chain = settings.ToChain();
                var key = new ProcessedChannelCacheKey(ir, state.SampleRate, chain);
                var side = new SideProcessJob
                {
                    State = state,
                    TransferIr = ir,
                    SampleRate = state.SampleRate,
                    Chain = chain,
                    Key = key
                };
                if (state.ProcessedCache?.Key.Equals(key) == true)
                {
                    side.ProcessedIr = state.ProcessedCache.ImpulseResponse;
                    side.ProcessedPeak = state.ProcessedCache.PeakIndex;
                    side.ProcessedFromCache = true;
                }
                if (side.ProcessedIr != null &&
                    state.ArrivalCache is { } arrival &&
                    ReferenceEquals(arrival.ProcessedIr, side.ProcessedIr) &&
                    arrival.LowHz == lowHz && arrival.HighHz == highHz)
                {
                    side.Arrival = arrival.Result;
                    side.LevelDb = arrival.LevelDb;
                    side.ArrivalFromCache = true;
                }

                return side;
            }

            SideProcessJob leftJob = Snapshot(leftState, leftSettings, leftIr);
            SideProcessJob rightJob = mono
                ? leftJob
                : Snapshot(
                    rightState,
                    rightSettings,
                    rightState.TransferImpulseResponse!);
            jobs.Add(new StereoDeltaJob(
                channel.Control.ChannelName,
                lowHz,
                highHz,
                leftJob,
                rightJob,
                mono));
        }

        bool anyWork = jobs.Any(job => job.Sides.Any(side => side.Arrival == null));
        if (anyWork)
        {
            await Task.Run(() =>
            {
                foreach (StereoDeltaJob job in jobs)
                {
                    foreach (SideProcessJob side in job.Sides)
                    {
                        if (side.ProcessedIr == null)
                        {
                            side.ProcessedIr = VirtualCrossoverAnalysis.ApplyChain(
                                side.TransferIr, side.Chain, side.SampleRate);
                            side.ProcessedPeak =
                                VirtualCrossoverAnalysis.FindPeakIndex(side.ProcessedIr);
                        }
                        if (side.Arrival == null)
                        {
                            side.Arrival =
                                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                                    side.ProcessedIr, side.SampleRate,
                                    job.LowHz, job.HighHz);
                            side.LevelDb = VirtualCrossoverAnalysis.MeasureBandLevelDb(
                                side.ProcessedIr, side.SampleRate,
                                job.LowHz, job.HighHz);
                        }
                    }
                }
            });

            // Cache write-back stays on the UI thread, like every other cache
            // in this panel.
            foreach (StereoDeltaJob job in jobs)
            {
                foreach (SideProcessJob side in job.Sides)
                {
                    if (!side.ProcessedFromCache)
                    {
                        side.State.ProcessedCache = new ProcessedChannelCache(
                            side.Key, side.ProcessedIr!, side.ProcessedPeak);
                    }
                    if (!side.ArrivalFromCache)
                    {
                        side.State.ArrivalCache =
                            (side.ProcessedIr!, job.LowHz, job.HighHz,
                                side.Arrival!.Value, side.LevelDb);
                    }
                }
            }
        }

        // The same reliability gate the engine's inter-side decisions apply,
        // per side: a formally valid arrival with a near-noise record would
        // print a precise-looking figure the user might chase with manual
        // delays — an honest "—" is the right read-out there. The Δ column
        // follows automatically (it needs both sides), and the level Δ is
        // gated the same way: a band that cannot place an arrival is reading
        // its noise floor, not a driver level.
        static bool Reliable(TimeAlignmentAnalysisResult arrival) =>
            arrival.IsValid &&
            arrival.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb;

        return jobs
            .Select(job =>
            {
                TimeAlignmentAnalysisResult left = job.Left.Arrival!.Value;
                bool leftReliable = Reliable(left);
                double? leftMs = leftReliable
                    ? left.FirstArrivalDelayMilliseconds
                    : null;
                if (job.Mono)
                {
                    // One physical response: show its arrival on the left slot;
                    // the right and the L−R delta have no meaning here ("—").
                    return new VirtualCrossoverMetric.StereoDelta(
                        job.Channel, leftMs, null, job.LowHz, job.HighHz, null);
                }

                TimeAlignmentAnalysisResult right = job.Right.Arrival!.Value;
                bool rightReliable = Reliable(right);
                return new VirtualCrossoverMetric.StereoDelta(
                    job.Channel,
                    leftMs,
                    rightReliable ? right.FirstArrivalDelayMilliseconds : null,
                    job.LowHz,
                    job.HighHz,
                    leftReliable && rightReliable &&
                    job.Left.LevelDb is { } leftLevel &&
                    job.Right.LevelDb is { } rightLevel
                        ? leftLevel - rightLevel
                        : null);
            })
            .ToList();
    }

    /// <summary>
    /// The complex-sum magnitude of the OPPOSITE side (dashed and translucent
    /// on the plot), so the two sides' tunes compare at a glance without
    /// flipping back and forth. Mono channels contribute their single response
    /// to both sides' sums, exactly as they do physically. Null when the
    /// opposite side has fewer than two participating channels — a "sum" of
    /// one driver is just that driver. Shares the per-side processed caches
    /// with everything else, so an unchanged opposite side costs nothing.
    /// </summary>
    private async Task<AnalysisCurve?> ComputeOppositeSumCurveAsync()
    {
        bool oppositeRight = !project.ActiveSideRight;
        var jobs = new List<SideProcessJob>();
        foreach (ChannelRuntime channel in channels)
        {
            VirtualCrossoverChannelSettings settings =
                channel.SideSettings(oppositeRight);
            ChannelSideState state = channel.SideState(oppositeRight);
            if (!settings.Enabled ||
                state.TransferImpulseResponse is not { } ir)
            {
                continue;
            }

            DspChannelChain chain = settings.Bypass
                ? DspChannelChain.Identity
                : settings.ToChain();
            var key = new ProcessedChannelCacheKey(ir, state.SampleRate, chain);
            var side = new SideProcessJob
            {
                State = state,
                TransferIr = ir,
                SampleRate = state.SampleRate,
                Chain = chain,
                Key = key
            };
            if (state.ProcessedCache?.Key.Equals(key) == true)
            {
                side.ProcessedIr = state.ProcessedCache.ImpulseResponse;
                side.ProcessedPeak = state.ProcessedCache.PeakIndex;
                side.ProcessedFromCache = true;
            }

            jobs.Add(side);
        }

        if (jobs.Count < 2)
        {
            return null;
        }

        if (jobs.Any(side => side.ProcessedIr == null))
        {
            await Task.Run(() =>
            {
                foreach (SideProcessJob side in jobs)
                {
                    if (side.ProcessedIr == null)
                    {
                        side.ProcessedIr = VirtualCrossoverAnalysis.ApplyChain(
                            side.TransferIr, side.Chain, side.SampleRate);
                        side.ProcessedPeak =
                            VirtualCrossoverAnalysis.FindPeakIndex(side.ProcessedIr);
                    }
                }
            });

            foreach (SideProcessJob side in jobs)
            {
                if (!side.ProcessedFromCache)
                {
                    side.State.ProcessedCache = new ProcessedChannelCache(
                        side.Key, side.ProcessedIr!, side.ProcessedPeak);
                }
            }
        }

        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses(
            jobs.Select(side => side.ProcessedIr!).ToList());
        int anchor = jobs.Min(side => side.ProcessedPeak);
        return BuildMagnitudeCurve(sum, anchor, jobs[0].SampleRate);
    }

    // The magnitude curves and complex sum the metric reads, built the same way
    // for the on-screen redraw and for a synchronous read (e.g. the Auto delay
    // log) so the two never disagree. Fewer than two channels yield no metric.
    private (List<AnalysisCurve>? Magnitudes, AnalysisCurve? Sum) BuildMetricCurves(
        List<ProcessedChannel> processed)
    {
        if (processed.Count < 2)
        {
            return (null, null);
        }

        // Every curve — the channels AND the sum — shares one window anchor (the
        // earliest arrival): with per-channel anchors the gates capture slightly
        // different room content and the loss can poke above its 0 dB ceiling.
        // The summed envelope peak can sit between the arrivals or vanish under
        // cancellation, so the anchor is the earliest arrival, not the sum peak.
        int anchor = processed.Min(item => item.PeakIndex);
        // One windowed FFT + resample per channel; GetPrimarySpectrum allocates
        // its own buffers and reads only the (redraw-stable) options and
        // calibration, so the channels' spectra compute across cores. AsOrdered
        // keeps the result aligned with the channel list.
        List<AnalysisCurve> magnitudes = processed
            .AsParallel()
            .AsOrdered()
            .Select(item => BuildMagnitudeCurve(
                item.ImpulseResponse, anchor, item.Channel.SampleRate))
            .ToList();
        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses(
            processed.Select(item => item.ImpulseResponse).ToList());
        AnalysisCurve sumCurve = BuildMagnitudeCurve(
            sum, anchor, processed[0].Channel.SampleRate);
        return (magnitudes, sumCurve);
    }

    // Builds the sum-loss read-outs for a processed set without touching any
    // control, so they can feed the label, its tooltip, and the Auto delay log
    // from one computation. Empty when there is no metric (fewer than two channels).
    private List<VirtualCrossoverMetric.Entry> BuildMetricEntries(
        List<ProcessedChannel> processed,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve)
    {
        var entries = new List<VirtualCrossoverMetric.Entry>();
        if (magnitudes == null || sumCurve == null)
        {
            return entries;
        }

        List<IReadOnlyList<SignalPoint>> channelPoints = magnitudes
            .Select(curve => (IReadOnlyList<SignalPoint>)curve.Points)
            .ToList();

        // Per-junction read-outs first, so an improvement at one crossover is
        // not averaged away by the other. Each junction reads the full sum
        // inside its own pair band; the out-of-pair channels are filtered so
        // far down there that their contribution is negligible.
        foreach (AdjacentPair pair in GetAdjacentPairs(OrderByBand(processed)))
        {
            double? pairLoss = VirtualCrossoverAnalysis.AverageSumLossDb(
                sumCurve.Points, channelPoints, pair.BandLowHz, pair.BandHighHz);
            double? pairDip = VirtualCrossoverAnalysis.MinimumSumLossDb(
                sumCurve.Points, channelPoints, pair.BandLowHz, pair.BandHighHz);
            if (pairLoss.HasValue)
            {
                entries.Add(new VirtualCrossoverMetric.Entry(
                    $"{pair.Lower.Channel.Control.ChannelName}/" +
                    $"{pair.Upper.Channel.Control.ChannelName}",
                    pairLoss.Value,
                    pairDip,
                    pair.BandLowHz,
                    pair.BandHighHz,
                    IsTotal: false));
            }
        }

        (double minHz, double maxHz) = GetCrossoverWindow(processed);
        double? loss = VirtualCrossoverAnalysis.AverageSumLossDb(
            sumCurve.Points, channelPoints, minHz, maxHz);
        double? dip = VirtualCrossoverAnalysis.MinimumSumLossDb(
            sumCurve.Points, channelPoints, minHz, maxHz);
        if (loss.HasValue)
        {
            entries.Add(new VirtualCrossoverMetric.Entry(
                "total", loss.Value, dip, minHz, maxHz, IsTotal: true));
        }

        return entries;
    }

    // The metric text renderings live in VirtualCrossoverMetric, where they are
    // unit-tested.
    private static string FormatMetricLabel(IReadOnlyList<VirtualCrossoverMetric.Entry> entries) =>
        VirtualCrossoverMetric.FormatLabel(entries);

    private static string FormatMetricCompact(IReadOnlyList<VirtualCrossoverMetric.Entry> entries) =>
        VirtualCrossoverMetric.FormatCompact(entries);

    private static string FormatMetricDetail(IReadOnlyList<VirtualCrossoverMetric.Entry> entries) =>
        VirtualCrossoverMetric.FormatDetail(entries);

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

        string name = latest.Channel.Control.ChannelName;
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
        (List<SideAlignmentChannel> leftSide, List<SideAlignmentChannel> rightSide) =
            CollectStereoSides();
        SideAlignmentChannel? bridgeRight = rightSide
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

        var alignment = new Dictionary<ChannelRuntime, AlignmentOverride>();
        List<ProcessedChannel> processed = ProcessChannels(alignment);
        if (processed.Count < 2)
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
        List<ProcessedChannel> bypassed = processed
            .Where(item => item.Channel.Settings.Bypass)
            .ToList();
        if (bypassed.Count > 0)
        {
            ShowError(
                "Auto delay cannot run with bypassed channels.",
                "Bypass feeds the raw measured signal, so the computed delays " +
                "and polarities would not apply to: " +
                string.Join(", ", bypassed.Select(
                    item => item.Channel.Control.ChannelName)) +
                ".\r\n\r\nDisable Bypass on every participating channel " +
                "(or mute the channel to exclude it) and run Auto delay again.");
            return;
        }

        // Without crossovers the search falls back to a broad midband window and
        // the result will shift once the filters are configured — the alignment
        // only matters (and is only well-defined) in the overlap region.
        bool anyCrossover = processed.Any(
            item => item.Channel.Settings.CrossoverKind != CrossoverKind.Off);
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

        (double minHz, double maxHz) = GetCrossoverWindow(processed);
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
            await Task.Run(() => ComputeAutoAlignment(processed, alignment, log));
            // The panel (or its form) may have been closed during the compute;
            // applying results would then touch disposed controls.
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            ApplyAlignmentResult(processed, alignment, log);
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
        foreach (ChannelRuntime channel in channels)
        {
            channel.Control.Enabled = !busy;
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
    private void ApplyAlignmentResult(
        List<ProcessedChannel> processed,
        Dictionary<ChannelRuntime, AlignmentOverride> alignment,
        System.Text.StringBuilder log)
    {
        foreach (ProcessedChannel item in processed)
        {
            AlignmentOverride result = alignment.GetValueOrDefault(item.Channel);
            item.Channel.Settings.DelayMs = Math.Round(result.DelayMs, 2);
            item.Channel.Settings.InvertPolarity = result.InvertPolarity;
            ApplySettingsToControl(item.Channel);
            log.AppendLine(
                $"Result {item.Channel.Control.ChannelName}: " +
                $"delay {item.Channel.Settings.DelayMs:0.00} ms, " +
                $"invert {(item.Channel.Settings.InvertPolarity ? "yes" : "no")}");
        }

        ScheduleSave();
        RedrawAll();
        // RedrawAll pushes the read-out asynchronously (the ApplyChain FFTs run off
        // the UI thread), so recompute the metric synchronously from the just-
        // applied settings so the log ends with this run's true outcome.
        List<ProcessedChannel> outcome = ProcessChannels();
        (List<AnalysisCurve>? outcomeMagnitudes, AnalysisCurve? outcomeSum) =
            BuildMetricCurves(outcome);
        log.AppendLine(FormatMetricDetail(
            BuildMetricEntries(outcome, outcomeMagnitudes, outcomeSum)));
        WriteAlignmentLog(log.ToString());
    }

    // Bridges the panel's channel model to the dsp AutoAlignmentEngine (where
    // the FFT-heavy alignment stages live, unit-tested): snapshots + junctions
    // in, an override map out. Runs on a background thread; the reprocess
    // delegate owns the thread-confined FFT cache — between consecutive
    // junction searches only one or two channels change their overrides, so
    // the other channels' processed IRs are reused instead of re-FFT'd, and
    // the shared UI-thread cache is never touched.
    private void ComputeAutoAlignment(
        List<ProcessedChannel> processed,
        Dictionary<ChannelRuntime, AlignmentOverride> alignment,
        System.Text.StringBuilder log)
    {
        List<ProcessedChannel> byBand = OrderByBand(processed);
        List<AdjacentPair> pairs = GetAdjacentPairs(byBand);

        // Same shared direct-sound crop + parallel cache-miss processing as
        // the stereo run: identical final delays at a fraction of the FFT
        // cost, because every search stage reads only the gated direct sound.
        List<ChannelRuntime> ordered = byBand.Select(item => item.Channel).ToList();
        Complex[][] croppedIrs = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            ordered.Select(channel => channel.TransferImpulseResponse!).ToList(),
            AutoDelaySearchCropLength,
            AutoDelaySearchCropPrePeakSamples);
        var searchIrs = new Dictionary<ChannelRuntime, Complex[]>();
        for (int i = 0; i < ordered.Count; i++)
        {
            searchIrs[ordered[i]] = croppedIrs[i];
        }

        var searchCache = new Dictionary<ChannelRuntime, ProcessedChannelCache>();
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            var results = new ProcessedChannelCache[ordered.Count];
            var keys = new ProcessedChannelCacheKey[ordered.Count];
            var chains = new DspChannelChain[ordered.Count];
            var missing = new List<int>();
            for (int i = 0; i < ordered.Count; i++)
            {
                ChannelRuntime channel = ordered[i];
                AlignmentOverride over = overrides.GetValueOrDefault(channel);
                chains[i] = channel.Settings.ToChain() with
                {
                    DelayMs = over.DelayMs,
                    InvertPolarity = over.InvertPolarity
                };
                keys[i] = new ProcessedChannelCacheKey(
                    searchIrs[channel], channel.SampleRate, chains[i]);
                ProcessedChannelCache? cached = searchCache.GetValueOrDefault(channel);
                if (cached?.Key.Equals(keys[i]) == true)
                {
                    results[i] = cached;
                }
                else
                {
                    missing.Add(i);
                }
            }

            Parallel.ForEach(missing, i =>
            {
                Complex[] result = VirtualCrossoverAnalysis.ApplyChain(
                    searchIrs[ordered[i]], chains[i], ordered[i].SampleRate);
                results[i] = new ProcessedChannelCache(
                    keys[i], result, VirtualCrossoverAnalysis.FindPeakIndex(result));
            });
            foreach (int i in missing)
            {
                searchCache[ordered[i]] = results[i];
            }

            return ordered
                .Select((channel, i) => new AlignmentSnapshot(
                    channel, results[i].ImpulseResponse, results[i].PeakIndex))
                .ToList();
        }

        IReadOnlyList<AlignmentSnapshot> initial = Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        var snapshots = ordered
            .Select((channel, i) => (channel, snapshot: initial[i]))
            .ToDictionary(item => item.channel, item => item.snapshot);
        List<AlignmentJunction> junctions = pairs
            .Select(pair => new AlignmentJunction(
                snapshots[pair.Lower.Channel],
                snapshots[pair.Upper.Channel],
                pair.CrossoverHz,
                pair.BandLowHz,
                pair.BandHighHz))
            .ToList();

        var engineAlignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            ordered.Select(channel => snapshots[channel]).ToList(),
            junctions,
            Reprocess,
            engineAlignment,
            log);

        foreach ((IAlignmentChannel channel, AlignmentOverride result) in engineAlignment)
        {
            alignment[(ChannelRuntime)channel] = result;
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
    private (List<SideAlignmentChannel> Left, List<SideAlignmentChannel> Right)
        CollectStereoSides()
    {
        var left = new List<SideAlignmentChannel>();
        var right = new List<SideAlignmentChannel>();
        foreach (ChannelRuntime channel in channels)
        {
            if (channel.SideSettings(false).Enabled &&
                channel.SideState(false).TransferImpulseResponse != null)
            {
                var side = new SideAlignmentChannel(channel, false);
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
                right.Add(new SideAlignmentChannel(channel, true));
            }
        }

        return (left, right);
    }

    // The stereo Auto delay: left side first, then the L/R bridge at the top
    // pair honoring the scene offset, then the right-side descent — the
    // cascade itself lives in AutoAlignmentEngine.ComputeStereo (dsp,
    // unit-tested on synthetic systems and real car measurements).
    private async Task AutoAlignStereoAsync(
        List<SideAlignmentChannel> leftSide,
        List<SideAlignmentChannel> rightSide,
        SideAlignmentChannel bridgeRight)
    {
        List<SideAlignmentChannel> union = leftSide.Concat(rightSide)
            .Distinct()
            .ToList();

        // Same reasoning as the single-side run: a bypassed side processes
        // through the identity chain, so the computed delay would silently not
        // apply — refuse instead of proposing a wrong alignment.
        List<SideAlignmentChannel> bypassed = union
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

        SideAlignmentChannel bridgeLeft = leftSide.First(
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

            ApplyStereoAlignmentResult(union, engineAlignment, log);
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
    // with the same thread-confined FFT cache idea as the single-side run.
    private void ComputeStereoAlignment(
        List<SideAlignmentChannel> leftSide,
        List<SideAlignmentChannel> rightSide,
        List<SideAlignmentChannel> union,
        SideAlignmentChannel bridgeLeft,
        SideAlignmentChannel bridgeRight,
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
        Complex[][] croppedIrs = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            union.Select(side => side.State.TransferImpulseResponse!).ToList(),
            AutoDelaySearchCropLength,
            AutoDelaySearchCropPrePeakSamples);
        var searchIrs = new Dictionary<SideAlignmentChannel, Complex[]>();
        for (int i = 0; i < union.Count; i++)
        {
            searchIrs[union[i]] = croppedIrs[i];
        }

        var searchCache = new Dictionary<SideAlignmentChannel, ProcessedChannelCache>();
        ProcessedChannelCache ComputeFresh(
            SideAlignmentChannel side,
            ProcessedChannelCacheKey key,
            DspChannelChain chain)
        {
            Complex[] result = VirtualCrossoverAnalysis.ApplyChain(
                searchIrs[side], chain, side.State.SampleRate);
            return new ProcessedChannelCache(
                key, result, VirtualCrossoverAnalysis.FindPeakIndex(result));
        }

        // Cache misses (all channels on the first call, usually one channel
        // per cascade step afterwards) run in parallel: ApplyChain is pure
        // FFT work on independent inputs, and the cache is written back on
        // this (engine) thread only.
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            var results = new ProcessedChannelCache[union.Count];
            var keys = new ProcessedChannelCacheKey[union.Count];
            var chains = new DspChannelChain[union.Count];
            var missing = new List<int>();
            for (int i = 0; i < union.Count; i++)
            {
                SideAlignmentChannel side = union[i];
                AlignmentOverride over = overrides.GetValueOrDefault(side);
                chains[i] = side.Settings.ToChain() with
                {
                    DelayMs = over.DelayMs,
                    InvertPolarity = over.InvertPolarity
                };
                keys[i] = new ProcessedChannelCacheKey(
                    searchIrs[side], side.State.SampleRate, chains[i]);
                ProcessedChannelCache? cached = searchCache.GetValueOrDefault(side);
                if (cached?.Key.Equals(keys[i]) == true)
                {
                    results[i] = cached;
                }
                else
                {
                    missing.Add(i);
                }
            }

            Parallel.ForEach(missing, i =>
            {
                results[i] = ComputeFresh(union[i], keys[i], chains[i]);
            });
            foreach (int i in missing)
            {
                searchCache[union[i]] = results[i];
            }

            return union
                .Select((side, i) => new AlignmentSnapshot(
                    side, results[i].ImpulseResponse, results[i].PeakIndex))
                .ToList();
        }

        IReadOnlyList<AlignmentSnapshot> initialSnapshots = Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        Dictionary<SideAlignmentChannel, AlignmentSnapshot> initial = union
            .Select((side, i) => (side, snapshot: initialSnapshots[i]))
            .ToDictionary(item => item.side, item => item.snapshot);
        List<AlignmentSnapshot> ByBand(List<SideAlignmentChannel> sides) => sides
            .OrderBy(side => VirtualCrossoverJunctions.BandCenterHz(side.Settings))
            .Select(side => initial[side])
            .ToList();
        List<AlignmentJunction> Pairs(List<AlignmentSnapshot> byBand)
        {
            var pairs = new List<AlignmentJunction>();
            for (int i = 0; i < byBand.Count - 1; i++)
            {
                double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                    ((SideAlignmentChannel)byBand[i].Channel).Settings,
                    ((SideAlignmentChannel)byBand[i + 1].Channel).Settings);
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
        foreach (SideAlignmentChannel right in rightSide.Where(side => side.RightSide))
        {
            SideAlignmentChannel? left = leftSide.FirstOrDefault(
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
            Reprocess,
            alignment,
            log);
    }

    // Applies the stereo proposal to BOTH sides' settings, rebinds the visible
    // controls and closes the log with the active side's metric.
    private void ApplyStereoAlignmentResult(
        List<SideAlignmentChannel> union,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        System.Text.StringBuilder log)
    {
        foreach (SideAlignmentChannel side in union)
        {
            AlignmentOverride result = alignment.GetValueOrDefault(side);
            side.Settings.DelayMs = Math.Round(result.DelayMs, 2);
            side.Settings.InvertPolarity = result.InvertPolarity;
            log.AppendLine(
                $"Result {side.Name}: delay {side.Settings.DelayMs:0.00} ms, " +
                $"invert {(side.Settings.InvertPolarity ? "yes" : "no")}");
        }

        foreach (ChannelRuntime channel in union
            .Select(side => side.Runtime)
            .Distinct())
        {
            ApplySettingsToControl(channel);
        }

        ScheduleSave();
        RedrawAll();
        // The read-out follows the active side; the log records which one.
        List<ProcessedChannel> outcome = ProcessChannels();
        (List<AnalysisCurve>? outcomeMagnitudes, AnalysisCurve? outcomeSum) =
            BuildMetricCurves(outcome);
        log.AppendLine($"Metric ({(project.ActiveSideRight ? "R" : "L")} side):");
        log.AppendLine(FormatMetricDetail(
            BuildMetricEntries(outcome, outcomeMagnitudes, outcomeSum)));
        WriteAlignmentLog(log.ToString());
    }

    // A diagnostic trace of the last Auto delay run (pair bands, arrivals,
    // deltas, fine results), for sharing when an alignment looks wrong. Best
    // effort: a failed write must never break the alignment itself.
    private static void WriteAlignmentLog(string text)
    {
        try
        {
            string path = Path.Combine(
                AppContext.BaseDirectory, "tools", "virtual-dsp-align.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch
        {
            // Diagnostics only.
        }
    }

    private static double GetPairCrossoverHz(
        VirtualCrossoverChannelSettings lower,
        VirtualCrossoverChannelSettings upper) =>
        VirtualCrossoverJunctions.GetPairCrossoverHz(lower, upper);

    // Adjacent channels along the spectrum with their shared junction: the pair
    // crossover frequency and the band (an octave to each side) where the two
    // drivers genuinely overlap. This band is where coarse arrivals are
    // compared, where the fine delay search correlates, and where the per-pair
    // sum-loss metric is read.
    private sealed record AdjacentPair(
        ProcessedChannel Lower,
        ProcessedChannel Upper,
        double CrossoverHz,
        double BandLowHz,
        double BandHighHz);

    private static List<ProcessedChannel> OrderByBand(List<ProcessedChannel> processed) =>
        processed
            .OrderBy(item => VirtualCrossoverJunctions.BandCenterHz(item.Channel.Settings))
            .ToList();

    private static List<AdjacentPair> GetAdjacentPairs(List<ProcessedChannel> byBand)
    {
        var pairs = new List<AdjacentPair>();
        for (int i = 0; i < byBand.Count - 1; i++)
        {
            double pairHz = GetPairCrossoverHz(
                byBand[i].Channel.Settings, byBand[i + 1].Channel.Settings);
            (double bandLowHz, double bandHighHz) = VirtualCrossoverJunctions.OverlapBand(pairHz);
            pairs.Add(new AdjacentPair(
                byBand[i],
                byBand[i + 1],
                pairHz,
                bandLowHz,
                bandHighHz));
        }

        return pairs;
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

    private void DrawPhaseCurves(PlotModel model, List<ProcessedChannel> processed)
    {
        // One shared absolute reference (the earliest arrival) keeps the curves'
        // relative phase intact — that relative alignment through the crossover
        // region is exactly what this view is for.
        int reference = processed.Min(item => item.PeakIndex);
        int sampleRate = processed[0].Channel.SampleRate;
        double gateOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(reference, sampleRate);
        double detrendMs = ResolveCommonDetrendMs(processed, gateOffsetMs, sampleRate);

        foreach (ProcessedChannel item in processed)
        {
            if (!item.Channel.Settings.ShowProcessedCurve)
            {
                continue;
            }

            AddCurve(
                model,
                item.Channel.Control.ChannelName,
                BuildPhasePoints(item.ImpulseResponse, sampleRate, gateOffsetMs, detrendMs),
                item.Color,
                1.8,
                LineStyle.Solid);
        }

        if (processed.Count >= 2 && checkBoxShowSum.Checked)
        {
            Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses(
                processed.Select(item => item.ImpulseResponse).ToList());
            AddCurve(
                model,
                "Sum",
                BuildPhasePoints(sum, sampleRate, gateOffsetMs, detrendMs),
                SumColor,
                2.4,
                LineStyle.Solid);
        }
    }

    // The impulse view is the gate dialog's IR preview promoted to the main
    // plot: every processed channel IR (crossover/PEQ/gain/delay/polarity
    // applied) on the shared absolute timeline, each normalized to its own
    // in-window peak, with the phase-gate Tukey window drawn where it sits.
    // Well-aligned drivers visibly start together.
    private void DrawImpulseCurves(PlotModel model, List<ProcessedChannel> processed)
    {
        // Only the shown traces set the gate offset and the ms-axis window, so
        // an auto gate never centers on a channel whose curve is hidden.
        List<ProcessedChannel> shown = processed
            .Where(item => item.Channel.Settings.ShowProcessedCurve)
            .ToList();
        if (shown.Count == 0)
        {
            return;
        }

        int reference = shown.Min(item => item.PeakIndex);
        int sampleRate = shown[0].Channel.SampleRate;
        double gateOffsetMs = gatePreview?.OffsetMs
            ?? ResolveGateOffsetMs(reference, sampleRate);

        var traces = shown
            .Select(item => new IrPreviewTrace(
                item.ImpulseResponse,
                item.Channel.Control.ChannelName,
                item.Color))
            .ToList();

        (double StartMs, double EndMs)? window =
            ImpulseWindowPreview.AddGatedTraceSeries(
                model,
                traces,
                sampleRate,
                gateOffsetMs,
                gatePreview?.LeftMs ?? project.PhaseGateLeftMs,
                gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs,
                gatePreview?.RightMs ?? project.PhaseGateRightMs,
                CurveSeriesTag);
        if (window is not { } bounds)
        {
            return;
        }

        // The axis is static (no pan/zoom), so simply re-arm it to the display
        // window the series were built for.
        mainTimeAxis.AbsoluteMinimum = bounds.StartMs;
        mainTimeAxis.AbsoluteMaximum = bounds.EndMs;
        mainTimeAxis.Minimum = bounds.StartMs;
        mainTimeAxis.Maximum = bounds.EndMs;
        mainTimeAxis.Reset();
    }

    // A stored gate offset is used as-is; an unconfigured project follows the
    // earliest processed arrival, so the gate tracks source and delay changes
    // until the user pins it in the gate dialog.
    private double ResolveGateOffsetMs(int referenceSample, int sampleRate) =>
        project.PhaseGateOffsetMs ?? referenceSample * 1_000.0 / sampleRate;

    // The τ detrend follows the same pattern: unconfigured projects reference
    // the earliest arrival. One τ serves every curve, so their relative phase —
    // the whole point of this view — survives the detrend.
    private double ResolveDetrendMs(int referenceSample, int sampleRate) =>
        project.PhaseDetrendMs ?? referenceSample * 1_000.0 / sampleRate;

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

    private List<SignalPoint> BuildPhasePoints(
        Complex[] impulseResponse,
        int sampleRate,
        double gateOffsetMs,
        double detrendMs)
    {
        // The gate construction is shared with the Phase mode (DataHelper's
        // gated extraction): a Tukey window of left + plateau + right whose
        // left shoulder ends at the gate offset, zero-padded to the fixed FFT
        // length so the frequency grid is constant. The τ detrend is the
        // fractional-sample phase reference; every curve built with the same τ
        // is directly comparable regardless of where its gate sits.
        var view = new ImpulseMeasurementView(impulseResponse, 0, sampleRate);
        PhaseAnalysisSettings settings = CreateVirtualPhaseSettings(
            gateOffsetMs,
            PhaseDetrendMode.Manual,
            detrendMs);
        List<SignalPoint> phase = DataHelper.GetGatedPhaseData(view, settings);

        var points = new List<SignalPoint>(phase.Count);
        foreach (SignalPoint point in phase)
        {
            if (point.X is < 20 or > 20_000)
            {
                continue;
            }

            points.Add(new SignalPoint(point.X, point.Y / Math.PI * 180.0));
        }

        return points;
    }

    // Opens the manual phase-gate dialog: the gate offset and Tukey shoulders
    // with a live preview of every processed channel IR, so reflections can be
    // cut out of the phase view visually.
    private void OpenPhaseGateDialog()
    {
        List<ProcessedChannel> processed = ProcessChannels();
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
                item.Channel.Control.ChannelName,
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
                project.PhaseGateOffsetMs = dialog.GateOffsetMs;
                project.PhaseGateLeftMs = dialog.LeftMs;
                project.PhaseGatePlateauMs = dialog.PlateauMs;
                project.PhaseGateRightMs = dialog.RightMs;
                project.PhaseDetrendMs = dialog.DetrendMs;
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

    private void RedrawDspPlot()
    {
        using var _ = AppProfiler.Zone("VirtualDSP.RedrawDspPlot");
        PlotModel? model = dspPlotView.Model;
        if (model == null)
        {
            return;
        }

        RemoveCurveSeries(model);

        DspPlotMode mode = CurrentDspPlotMode();
        if (model.Axes.FirstOrDefault(axis => axis.Key == DspValueAxisKey)
            is LinearAxis valueAxis)
        {
            ConfigureDspValueAxis(valueAxis, mode);
        }

        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 512);
        for (int i = 0; i < channels.Count; i++)
        {
            ChannelRuntime channel = channels[i];
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
            PreparedDspResponse preparedResponse = PreparedDspResponse.Create(
                chain,
                channel.SampleRate);

            var points = new List<DataPoint>(grid.Count);
            foreach (double frequency in grid)
            {
                points.Add(new DataPoint(
                    frequency,
                    DspPlotValue(preparedResponse, frequency, mode)));
            }

            AddDspSeries(
                model, $"{channel.Control.ChannelName} filter", points,
                ChannelColors[i], 1.8, LineStyle.Solid, DspValueAxisKey);
        }

        model.InvalidatePlot(true);
    }

    private static double DspPlotValue(
        PreparedDspResponse response,
        double frequency,
        DspPlotMode mode) => mode switch
        {
            DspPlotMode.Phase => response.Response(frequency).Phase / Math.PI * 180.0,
            DspPlotMode.GroupDelay => response.GroupDelayMs(frequency),
            _ => DataHelper.AmplitudeToDecibels(response.Response(frequency).Magnitude)
        };

    // ------------------------------------------------------- capture / export

    // Saves the current complex sum as a Captured overlay in Frequency Response,
    // closing the loop: virtual alignment -> comparison against real measurements
    // and target curves -> EQ Wizard.
    private void CaptureSumToOverlay()
    {
        List<ProcessedChannel> processed = ProcessChannels();
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
            processed.Select(item => item.Channel.Control.ChannelName));
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
    private void ExportTuningSheet()
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
        List<ProcessedChannel> metricChannels = ProcessChannels();
        (List<AnalysisCurve>? metricMagnitudes, AnalysisCurve? metricSum) =
            BuildMetricCurves(metricChannels);
        string metricLine = FormatMetricLabel(
            BuildMetricEntries(metricChannels, metricMagnitudes, metricSum));
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
            foreach (ChannelRuntime channel in participating)
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
                    $"{channel.Control.ChannelName} — {channel.Settings.DisplayName}",
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
            ChannelRuntime channel = participating[i];
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

    // Computes the channel's harmonic distortion (THD, dB vs the fundamental) from
    // a source's sweep deconvolution, for the crossover wizard's distortion-clean
    // band read. Returns null when the source carried no sweep deconvolution (only a
    // loopback transfer) or the sweep metadata is missing — the wizard then falls
    // back to the class-based sensible range.
    private static IReadOnlyList<SignalPoint>? ComputeDistortionCurve(
        MeasurementHistorySnapshot snapshot)
    {
        if (snapshot.SweepDeconvolutionImpulseResponse is not { Length: > 0 } ir ||
            snapshot.Octaves <= 0 ||
            snapshot.SampleRate <= 0 ||
            !double.IsFinite(snapshot.SweepDurationSeconds) ||
            snapshot.SweepDurationSeconds <= 0)
        {
            return null;
        }

        try
        {
            int sweepSamples = (int)Math.Round(snapshot.SweepDurationSeconds * snapshot.SampleRate);
            var sweep = EssSweepMetadata.FromExponentialSweep(
                snapshot.SampleRate, snapshot.Octaves, sweepSamples, snapshot.SweepDeconvolutionPeakIndex);

            double[] real = new double[ir.Length];
            for (int i = 0; i < ir.Length; i++)
            {
                real[i] = ir[i].Real;
            }

            EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
                real, sweep, new HarmonicAnalysisOptions(MaxHarmonic: 5));
            DistortionSpectrum spectrum = EssDistortion.ComputeDistortion(
                decomposition, calibration: null, new DistortionOptions(MaxHarmonic: 5));

            var points = new List<SignalPoint>(spectrum.Frequencies.Length);
            for (int i = 0; i < spectrum.Frequencies.Length; i++)
            {
                double thd = spectrum.ThdRatio[i];
                points.Add(new SignalPoint(
                    spectrum.Frequencies[i],
                    double.IsFinite(thd) && thd > 0.0 ? 20.0 * Math.Log10(thd) : double.NaN));
            }

            return points;
        }
        catch (ArgumentException)
        {
            return null;
        }
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

    // ------------------------------------------------------------ series glue

    private static void RemoveCurveSeries(PlotModel model)
    {
        for (int index = model.Series.Count - 1; index >= 0; index--)
        {
            if (Equals(model.Series[index].Tag, CurveSeriesTag))
            {
                model.Series.RemoveAt(index);
            }
        }

        // The impulse view marks its gate-offset annotation with the same tag,
        // so a redraw sweeps it together with the curves.
        for (int index = model.Annotations.Count - 1; index >= 0; index--)
        {
            if (Equals(model.Annotations[index].Tag, CurveSeriesTag))
            {
                model.Annotations.RemoveAt(index);
            }
        }
    }

    private static void AddCurve(
        PlotModel model,
        string title,
        IReadOnlyList<SignalPoint> points,
        OxyColor color,
        double thickness,
        LineStyle lineStyle)
    {
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = thickness,
            LineStyle = lineStyle,
            Title = title,
            Tag = CurveSeriesTag,
            TrackerFormatString = CurveTrackerFormat
        };
        foreach (SignalPoint point in points)
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
        }

        model.Series.Add(series);
    }

    private static void AddDspSeries(
        PlotModel model,
        string title,
        List<DataPoint> points,
        OxyColor color,
        double thickness,
        LineStyle lineStyle,
        string yAxisKey)
    {
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = thickness,
            LineStyle = lineStyle,
            Title = title,
            Tag = CurveSeriesTag,
            TrackerFormatString = CurveTrackerFormat,
            YAxisKey = yAxisKey
        };
        series.Points.AddRange(points);
        model.Series.Add(series);
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

    // The resolved source measurement of one SIDE of a channel pair (null while
    // unresolved), plus that side's interactive processed-IR cache.
    private sealed class ChannelSideState
    {
        public Complex[]? TransferImpulseResponse { get; set; }
        public int TransferPeakIndex { get; set; }
        public int SampleRate { get; set; }

        // The measurement's per-bin coherence (γ²) on the linear FFT grid, when
        // the source carried it. Only the auto-crossover wizard reads it, to
        // discount frequencies the measurement did not trust when reading each
        // driver's usable band; null when the source had none.
        public double[]? TransferCoherence { get; set; }

        // The channel's harmonic distortion (THD, dB vs the fundamental) computed
        // from the source's sweep deconvolution, when it carried one. Only the
        // auto-crossover wizard reads it, to bound each driver by its
        // distortion-clean band (a tweeter's low handover follows its measured
        // distortion knee); null when the source had no sweep deconvolution.
        public IReadOnlyList<SignalPoint>? DistortionCurve { get; set; }

        public ProcessedChannelCache? ProcessedCache { get; set; }

        // The band-limited envelope arrival and gated band level of this
        // side's PROCESSED response, keyed by the processed array's identity
        // and the measured band — the L/R/Δ read-out re-runs on every redraw,
        // and the Hilbert analysis of a full-length IR is far too heavy to
        // repeat when nothing changed. The level rides in the same cache
        // entry: it is measured over the same band from the same response.
        public (Complex[] ProcessedIr, double LowHz, double HighHz,
            TimeAlignmentAnalysisResult Result, double? LevelDb)?
            ArrivalCache
        { get; set; }

        // Invalidation counter for in-flight asynchronous source loads: a
        // load captures the revision when it starts (BeginSourceLoad, which
        // also invalidates any OLDER in-flight load into this slot, so the
        // user's latest pick wins regardless of completion order) and may
        // write back only while the revision still matches. Clear() bumps it
        // too: a project import or mono toggle mid-load kills the landing
        // instead of hiding a stale measurement in a slot that was wiped.
        public int SourceRevision { get; private set; }

        public int BeginSourceLoad() => ++SourceRevision;

        public void Clear()
        {
            TransferImpulseResponse = null;
            TransferPeakIndex = 0;
            SampleRate = 0;
            TransferCoherence = null;
            DistortionCurve = null;
            ProcessedCache = null;
            ArrivalCache = null;
            SourceRevision++;
        }
    }

    // Runtime state of one channel block — since the stereo rework, one L/R
    // PAIR. The block's controls and every interactive computation read the
    // ACTIVE side through the delegating members below, so the rest of the
    // panel works unchanged; the side toggle just flips ActiveRight and
    // rebinds. A mono pair (shared subwoofer) routes both sides to the left
    // settings and state.
    private sealed class ChannelRuntime : IAlignmentChannel
    {
        private readonly ChannelSideState leftState = new();
        private readonly ChannelSideState rightState = new();

        public ChannelRuntime(VirtualCrossoverChannelControl control)
        {
            Control = control;
        }

        public VirtualCrossoverChannelControl Control { get; }
        // The alignment engine's log identity; ChannelName is a plain string
        // property, safe to read off the UI thread.
        public string Name => Control.ChannelName;

        public VirtualCrossoverChannelPairSettings Pair { get; set; } = new();
        public bool ActiveRight { get; set; }

        // The EFFECTIVE side slot: what the views and calculations read — a
        // mono pair routes both sides to its single left slot.
        public ChannelSideState SideState(bool rightSide) =>
            Pair.Mono || !rightSide ? leftState : rightState;

        // The PHYSICAL side slot, mono routing ignored. Lifetime management
        // (project load, mono toggling) must use this one: through the
        // effective accessor a mono pair's real right slot is unreachable, so
        // a stale measurement could hide there and resurface the moment the
        // pair stops being mono.
        public ChannelSideState PhysicalSideState(bool rightSide) =>
            rightSide ? rightState : leftState;
        public VirtualCrossoverChannelSettings SideSettings(bool rightSide) =>
            Pair.SideFor(rightSide);
        private ChannelSideState Active => SideState(ActiveRight);

        public VirtualCrossoverChannelSettings Settings => Pair.SideFor(ActiveRight);
        public Complex[]? TransferImpulseResponse
        {
            get => Active.TransferImpulseResponse;
            set => Active.TransferImpulseResponse = value;
        }
        public int TransferPeakIndex
        {
            get => Active.TransferPeakIndex;
            set => Active.TransferPeakIndex = value;
        }
        public double[]? TransferCoherence
        {
            get => Active.TransferCoherence;
            set => Active.TransferCoherence = value;
        }
        public IReadOnlyList<SignalPoint>? DistortionCurve
        {
            get => Active.DistortionCurve;
            set => Active.DistortionCurve = value;
        }
        public int SampleRate
        {
            get => Active.SampleRate;
            set => Active.SampleRate = value;
        }
        public ProcessedChannelCache? ProcessedCache
        {
            get => Active.ProcessedCache;
            set => Active.ProcessedCache = value;
        }
    }

    // One side of a channel pair as the STEREO alignment engine sees it: the
    // engine needs distinct identities for the left and right drivers of one
    // block, while the interactive paths keep using ChannelRuntime itself. A
    // mono pair contributes a single instance (rightSide: false) to both
    // sides' channel lists.
    private sealed class SideAlignmentChannel : IAlignmentChannel
    {
        public SideAlignmentChannel(ChannelRuntime runtime, bool rightSide)
        {
            Runtime = runtime;
            RightSide = rightSide;
        }

        public ChannelRuntime Runtime { get; }
        public bool RightSide { get; }
        public VirtualCrossoverChannelSettings Settings =>
            Runtime.SideSettings(RightSide);
        public ChannelSideState State => Runtime.SideState(RightSide);
        public string Name => Runtime.Pair.Mono
            ? $"{Runtime.Name} (mono)"
            : $"{Runtime.Name} {(RightSide ? "R" : "L")}";
        public int SampleRate => State.SampleRate;
    }

    private sealed record ProcessedChannelCache(
        ProcessedChannelCacheKey Key,
        Complex[] ImpulseResponse,
        int PeakIndex);
}
