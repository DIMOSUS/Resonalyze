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
        OxyColor.FromRgb(96, 210, 120)    // C: green
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
        double DetrendMs)? gatePreview;
    private PlotWatermarkAnnotation hintAnnotation = null!;
    private LinearAxis mainValueAxis = null!;
    private PlotLabelsPanelController plotLabels = null!;
    private bool initialized;
    private bool suppressProjectEvents;
    private bool savePending;

    public VirtualCrossoverPanel()
    {
        InitializeComponent();
        channels.Add(new ChannelRuntime(virtualCrossoverChannelControl1));
        channels.Add(new ChannelRuntime(virtualCrossoverChannelControl2));
        channels.Add(new ChannelRuntime(virtualCrossoverChannelControl3));
        for (int i = 0; i < channels.Count; i++)
        {
            // The block header and curve checkboxes carry the channel's plot
            // color, so a curve is traceable to its block at a glance.
            OxyColor color = ChannelColors[i];
            channels[i].Control.SetAccentColor(
                Color.FromArgb(color.R, color.G, color.B));
        }

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

    /// <summary>Microphone calibration applied to the magnitude curves. Wired by the host form.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal CalibrationFile? Calibration { get; set; }

    /// <summary>
    /// Saves the given curve as a Captured Frequency Response overlay and returns
    /// the slot it landed in (null when all slots are taken). Wired by the host form.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<string, OverlayPoint[], int?>? OverlayCaptureRequested { get; set; }

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
        _ = LoadProjectAsync();
    }

    // ---------------------------------------------------------------- project

    private Task LoadProjectAsync() =>
        ApplyProjectAsync(VirtualCrossoverProjectFile.LoadOrDefault());

    // Binds a project (the internal autosave or an imported session) to the UI:
    // controls, view flags, and freshly re-resolved sources.
    private async Task ApplyProjectAsync(VirtualCrossoverProjectFile newProject)
    {
        project = newProject;
        // Older files may carry two channels; the UI always shows three.
        while (project.Channels.Count < channels.Count)
        {
            project.Channels.Add(new VirtualCrossoverChannelSettings());
        }

        suppressProjectEvents = true;
        try
        {
            checkBoxShowSum.Checked = project.ShowSumCurve;
            checkBoxShowLoss.Checked = project.ShowLossCurve;
            radioViewPhase.Checked = project.ShowPhaseView;
            radioViewMagnitude.Checked = !project.ShowPhaseView;
            ConfigureMainValueAxis();
            UpdateGateButtonAvailability();
            comboBoxSmoothing.SelectedItem =
                OverlaySmoothing.IsValid(project.SmoothingInverseOctaves)
                    ? project.SmoothingInverseOctaves
                    : 12;

            for (int i = 0; i < channels.Count; i++)
            {
                channels[i].Settings = project.Channels[i];
                ApplySettingsToControl(channels[i]);
            }
        }
        finally
        {
            suppressProjectEvents = false;
        }

        foreach (ChannelRuntime channel in channels)
        {
            // Discard the previous project's resolved measurement first, so an
            // imported channel without a source does not keep stale audio.
            channel.TransferImpulseResponse = null;
            channel.ProcessedCache = null;
            channel.SampleRate = 0;
            await ResolveSourceAsync(channel, showErrors: false);
        }

        RedrawAll();
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
        catch
        {
            // The project file is a convenience; failing to save it must never
            // break the tool (e.g. a read-only install directory).
        }
    }

    // ----------------------------------------------------------------- wiring

    private void WirePanelEvents()
    {
        foreach (ChannelRuntime channel in channels)
        {
            ChannelRuntime captured = channel;
            channel.Control.SettingsChanged += (_, _) => OnChannelSettingsChanged(captured);
            channel.Control.SourceClicked += (_, _) => ShowSourceMenu(captured);
            channel.Control.PeqLoadClicked += (_, _) => LoadPeq(captured);
            channel.Control.PeqClearClicked += (_, _) => ClearPeq(captured);
        }

        checkBoxShowSum.CheckedChanged += (_, _) => OnViewChanged();
        checkBoxShowLoss.CheckedChanged += (_, _) => OnViewChanged();
        // In a two-radio group every toggle flips radioViewPhase, so listening
        // to it alone reacts exactly once per mode switch.
        radioViewPhase.CheckedChanged += (_, _) => OnViewModeChanged();
        comboBoxSmoothing.SelectedIndexChanged += (_, _) => OnViewChanged();
    }

    private void OnChannelSettingsChanged(ChannelRuntime channel)
    {
        if (suppressProjectEvents)
        {
            return;
        }

        ReadControlIntoSettings(channel);
        ScheduleSave();
        RedrawAll();
    }

    private void OnViewModeChanged()
    {
        ConfigureMainValueAxis();
        UpdateGateButtonAvailability();
        OnViewChanged();
    }

    // The gate only shapes the phase view; grey the button out elsewhere so it
    // does not suggest an effect on the magnitude curves.
    private void UpdateGateButtonAvailability() =>
        buttonPhaseGate.Enabled = radioViewPhase.Checked;

    private void OnViewChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        project.ShowSumCurve = checkBoxShowSum.Checked;
        project.ShowLossCurve = checkBoxShowLoss.Checked;
        project.ShowPhaseView = radioViewPhase.Checked;
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
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            RestoreDirectory = true,
            Title = $"Choose channel {channel.Control.ChannelName} impulse response"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(dialog.FileName);
            MeasurementHistorySnapshot snapshot = MeasurementHistoryService.CreateSnapshot(file);
            if (!TryAcceptSource(
                channel,
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
        try
        {
            MeasurementHistoryEntry? entry = HistoryService?.FindById(entryId);
            MeasurementHistorySnapshot? snapshot = HistoryService == null
                ? null
                : await HistoryService.GetSnapshotAsync(entryId);
            if (entry == null || snapshot == null)
            {
                return;
            }

            TryAcceptSource(
                channel,
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
    private bool TryAcceptSource(
        ChannelRuntime channel,
        MeasurementHistorySnapshot snapshot,
        string displayName,
        string? sourceFilePath,
        Guid? historyEntryId)
    {
        if (snapshot.TransferImpulseResponse is not { Length: > 0 } transferIr)
        {
            ShowError(
                "This measurement has no loopback transfer IR.",
                "The virtual crossover sums loopback-referenced responses; " +
                "re-measure with a loopback channel configured.");
            return false;
        }

        // A mismatched sample rate is not a dead end: the user picks whether the
        // new measurement wins (clearing the incompatible channels) or loses.
        List<ChannelRuntime> mismatched = channels
            .Where(other => other != channel &&
                other.TransferImpulseResponse != null &&
                other.SampleRate != snapshot.SampleRate)
            .ToList();
        if (mismatched.Count > 0)
        {
            string mismatchedList = string.Join(
                ", ",
                mismatched.Select(other =>
                    $"{other.Control.ChannelName} ({other.SampleRate} Hz)"));
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

            foreach (ChannelRuntime other in mismatched)
            {
                ClearSourceCore(other);
            }
        }

        channel.TransferImpulseResponse = transferIr;
        channel.ProcessedCache = null;
        channel.TransferPeakIndex = Math.Clamp(
            snapshot.TransferPeakIndex ?? 0, 0, transferIr.Length - 1);
        channel.SampleRate = snapshot.SampleRate;
        channel.Settings.DisplayName = displayName;
        channel.Settings.SourceFilePath = sourceFilePath;
        channel.Settings.HistoryEntryId = historyEntryId;

        UpdateSourceButton(channel);
        ScheduleSave();
        RedrawAll();
        return true;
    }

    private void ClearSource(ChannelRuntime channel)
    {
        ClearSourceCore(channel);
        ScheduleSave();
        RedrawAll();
    }

    private void ClearSourceCore(ChannelRuntime channel)
    {
        channel.TransferImpulseResponse = null;
        channel.ProcessedCache = null;
        channel.SampleRate = 0;
        channel.Settings.DisplayName = string.Empty;
        channel.Settings.SourceFilePath = null;
        channel.Settings.HistoryEntryId = null;
        UpdateSourceButton(channel);
    }

    // Re-resolves a persisted source reference: the history entry first (it
    // survives file moves), then the file path. A source that no longer exists
    // degrades to an unresolved channel instead of failing the project load.
    private async Task ResolveSourceAsync(ChannelRuntime channel, bool showErrors)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        if (!settings.HasSource)
        {
            UpdateSourceButton(channel);
            return;
        }

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

            // The same compatibility rules as TryAcceptSource: the file behind a
            // stored path may have been replaced since the project was saved. An
            // incompatible source stays unresolved (the button shows the warning
            // glyph) instead of silently producing a physically wrong sum.
            bool compatible = channels.All(other =>
                other == channel ||
                other.TransferImpulseResponse == null ||
                other.SampleRate == snapshot?.SampleRate);
            if (compatible &&
                snapshot?.TransferImpulseResponse is { Length: > 0 } transferIr)
            {
                channel.TransferImpulseResponse = transferIr;
                channel.ProcessedCache = null;
                channel.TransferPeakIndex = Math.Clamp(
                    snapshot.TransferPeakIndex ?? 0, 0, transferIr.Length - 1);
                channel.SampleRate = snapshot.SampleRate;
            }
        }
        catch (Exception exception) when (!showErrors)
        {
            _ = exception;
        }

        UpdateSourceButton(channel);
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
            Filter = string.Join(
                "|",
                formats.Select(format => $"{format.Name} (*.{format.Extension})|*.{format.Extension}")),
            Title = $"Load channel {channel.Control.ChannelName} PEQ"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        IEqProfileFormat chosen = formats[Math.Clamp(dialog.FilterIndex - 1, 0, formats.Count - 1)];
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
        channel.Control.PeqInfoLabel.Text = settings.PeqBands.Count == 0 && settings.PeqPreampDb == 0
            ? "No PEQ"
            : $"{settings.PeqSourceName ?? "PEQ"}: {settings.PeqBands.Count} bands, " +
              $"preamp {settings.PeqPreampDb:0.0} dB";
    }

    // ------------------------------------------------------------------ plots

    private void InitializeMainPlot()
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        // The absolute pan/zoom limits live in ConfigureMainValueAxis: they
        // differ between the magnitude (dB) and phase (deg) views.
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
    // axis survives the toggle; only the value scale is re-armed.
    private void ConfigureMainValueAxis()
    {
        if (radioViewPhase.Checked)
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

        mainValueAxis.Reset();
        mainPlotView.InvalidatePlot(false);
    }

    private void InitializeDspPlot()
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        model.Axes.Add(new LinearAxis
        {
            Key = "dsp-magnitude",
            Position = AxisPosition.Left,
            Title = "dB",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Minimum = -60,
            Maximum = 20
        });
        model.Axes.Add(new LinearAxis
        {
            Key = "dsp-phase",
            Position = AxisPosition.Right,
            Title = "deg",
            Minimum = -190,
            Maximum = 190,
            MajorStep = 90
        });
        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "DSP chains",
            TextColor = OxyColor.FromAColor(10, OxyColors.White),
            FontSize = 40,
            FontWeight = FontWeights.Bold
        });

        dspPlotView.Model = model;
        PlotInteraction.EnableDoubleClickAxisReset(dspPlotView);
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
            comboBoxSmoothing,
            "Fractional-octave smoothing of the magnitude curves.");
        toolTip.SetToolTip(
            buttonAutoDelay,
            "Align the channels in two stages: band-limited first\r\n" +
            "arrivals set the coarse delays, then a phase search\r\n" +
            "(±half a crossover period) fine-tunes them and flips\r\n" +
            "polarity when the channels sum better inverted.\r\n" +
            "Set the crossover filters first — the search targets\r\n" +
            "the overlap region around their corner frequencies.");
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
        toolTip.SetToolTip(
            labelMetric,
            "Average summation loss inside the crossover window.\r\n" +
            "0 dB — perfectly coherent summation;\r\n" +
            "more negative — the channels partially cancel.");
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

        RedrawMainPlot();
        RedrawDspPlot();
    }

    private sealed record ProcessedChannel(
        ChannelRuntime Channel,
        Complex[] ImpulseResponse,
        int PeakIndex,
        OxyColor Color);

    private readonly record struct AlignmentOverride(
        double DelayMs,
        bool InvertPolarity);

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
        bool useCache = alignmentOverrides == null;
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

            ProcessedChannelCacheKey? cacheKey = useCache
                ? new ProcessedChannelCacheKey(ir, channel.SampleRate, chain)
                : null;
            if (cacheKey != null &&
                channel.ProcessedCache?.Key.Equals(cacheKey) == true)
            {
                using (AppProfiler.Zone("VirtualDSP.ProcessChannels.CacheHit"))
                {
                    processed.Add(new ProcessedChannel(
                        channel,
                        channel.ProcessedCache.ImpulseResponse,
                        channel.ProcessedCache.PeakIndex,
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

            if (cacheKey != null)
            {
                channel.ProcessedCache = new ProcessedChannelCache(
                    cacheKey,
                    result,
                    peakIndex);
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

    private void RedrawMainPlot()
    {
        using var _ = AppProfiler.Zone("VirtualDSP.RedrawMainPlot");
        PlotModel? model = mainPlotView.Model;
        if (model == null)
        {
            return;
        }

        RemoveCurveSeries(model);
        List<ProcessedChannel> processed = ProcessChannels();
        hintAnnotation.Text = processed.Count == 0 ? NoSourcesHint : string.Empty;

        // The processed magnitudes and the complex sum feed both the drawn
        // curves and the sum-loss metric, so they are built once here. Every
        // curve — the channels AND the sum — shares one window anchor (the
        // earliest arrival): with per-channel anchors the gates capture slightly
        // different room content and the loss can poke above its 0 dB ceiling.
        List<AnalysisCurve>? magnitudes = null;
        AnalysisCurve? sumCurve = null;
        if (processed.Count >= 2)
        {
            int anchor = processed.Min(item => item.PeakIndex);
            magnitudes = processed
                .Select(item => BuildMagnitudeCurve(
                    item.ImpulseResponse, anchor, item.Channel.SampleRate))
                .ToList();
            // The window anchors at the earliest arrival: the summed envelope peak
            // can sit between the arrivals or vanish entirely under cancellation.
            Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses(
                processed.Select(item => item.ImpulseResponse).ToList());
            sumCurve = BuildMagnitudeCurve(
                sum, anchor, processed[0].Channel.SampleRate);
        }

        UpdateMetric(processed, magnitudes, sumCurve);

        if (processed.Count > 0)
        {
            if (radioViewPhase.Checked)
            {
                DrawPhaseCurves(model, processed);
            }
            else
            {
                DrawMagnitudeCurves(model, processed, magnitudes, sumCurve);
            }
        }

        plotLabels.Refresh();
        model.InvalidatePlot(true);
    }

    private void DrawMagnitudeCurves(
        PlotModel model,
        List<ProcessedChannel> processed,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve)
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
        }

        if (checkBoxShowLoss.Checked)
        {
            // The signed dB gap between the complex sum and the phase-blind
            // magnitude sum of the processed channels (<= 0 by the triangle
            // inequality); all curves share the same fixed log grid.
            int count = magnitudes.Min(curve => curve.Points.Count);
            count = Math.Min(count, sumCurve.Points.Count);
            var points = new List<SignalPoint>(count);
            for (int i = 0; i < count; i++)
            {
                double magnitudeSum = magnitudes.Sum(
                    curve => DataHelper.DecibelsToAmplitude(curve.Points[i].Y));
                points.Add(new SignalPoint(
                    sumCurve.Points[i].X,
                    sumCurve.Points[i].Y - DataHelper.AmplitudeToDecibels(magnitudeSum)));
            }

            AddCurve(model, "Sum loss", points, LossColor, 1.8, LineStyle.Dash);
        }
    }

    // ------------------------------------------------- metric and auto delay

    // The frequency window the metric and Auto delay operate in: around the
    // corner frequencies the channels actually use (one octave to each side),
    // or a broad midband default when no crossover is configured yet.
    private (double MinHz, double MaxHz) GetCrossoverWindow(
        List<ProcessedChannel> processed)
    {
        var corners = new List<double>();
        foreach (ProcessedChannel item in processed)
        {
            VirtualCrossoverChannelSettings settings = item.Channel.Settings;
            if (settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass)
            {
                corners.Add(settings.LowPassEdge.FrequencyHz);
            }
            if (settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass)
            {
                corners.Add(settings.HighPassEdge.FrequencyHz);
            }
        }

        if (corners.Count == 0)
        {
            return (100, 10_000);
        }

        return (
            Math.Max(20, corners.Min() / 2),
            Math.Min(20_000, corners.Max() * 2));
    }

    private void UpdateMetric(
        List<ProcessedChannel> processed,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve)
    {
        if (magnitudes == null || sumCurve == null)
        {
            labelMetric.Text = "Sum loss avg: —";
            return;
        }

        List<IReadOnlyList<SignalPoint>> channelPoints = magnitudes
            .Select(curve => (IReadOnlyList<SignalPoint>)curve.Points)
            .ToList();
        var parts = new List<string>();

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
                parts.Add(
                    $"{pair.Lower.Channel.Control.ChannelName}/" +
                    $"{pair.Upper.Channel.Control.ChannelName} " +
                    $"{pairLoss.Value:0.0} dB" +
                    (pairDip.HasValue ? $", dip {pairDip.Value:0.0} dB" : "") +
                    $" ({FormatHz(pair.BandLowHz)} – {FormatHz(pair.BandHighHz)})");
            }
        }

        (double minHz, double maxHz) = GetCrossoverWindow(processed);
        double? loss = VirtualCrossoverAnalysis.AverageSumLossDb(
            sumCurve.Points, channelPoints, minHz, maxHz);
        double? dip = VirtualCrossoverAnalysis.MinimumSumLossDb(
            sumCurve.Points, channelPoints, minHz, maxHz);
        if (loss.HasValue)
        {
            string total = $"{loss.Value:0.0} dB" +
                (dip.HasValue ? $", dip {dip.Value:0.0} dB" : "") +
                $" ({FormatHz(minHz)} – {FormatHz(maxHz)})";
            parts.Add(parts.Count > 0 ? "total " + total : total);
        }

        labelMetric.Text = parts.Count > 0
            ? "Sum loss avg: " + string.Join("   ", parts)
            : "Sum loss avg: —";
    }

    private static string FormatHz(double frequencyHz) =>
        frequencyHz >= 1_000
            ? $"{frequencyHz / 1_000:0.#} kHz"
            : $"{frequencyHz:0} Hz";

    // Bounds of the stage-2 fine-search span. The span scales with the
    // crossover frequency (half its period) because the coarse arrival error
    // grows with the period — but it never drops below half a millisecond:
    // arrival estimates carry a floor of error (filter group-delay asymmetry,
    // driver rise time) that does not shrink with the junction period, so at a
    // high split half a period would regularly miss the true optimum. The
    // extra lobes a wide window admits are handled by the candidate list, the
    // arrival prior, and the physical tie-break in SelectCandidate.
    private const double MinFineAlignmentRangeMs = 0.5;
    private const double MaxFineAlignmentRangeMs = 2.5;

    // Tie-break preferences among near-equal alignment candidates. An inverted
    // winner must beat the best non-inverted candidate by a real margin: room
    // reflections routinely hand a (flip + half-period shift) impostor a few
    // hundredths of a dB inside the pair band, while a genuinely flipped
    // driver wins by the full arrival-prior penalty of its non-inverted
    // impostors (>= ~0.3 dB, see PriorPenaltyDbAtSigma). Among equals of one
    // polarity, the candidate closest to the measured arrivals wins — the
    // physically minimal correction.
    private const double InvertPreferenceMarginDb = 0.25;
    private const double DelayTieMarginDb = 0.1;

    private static AlignmentCandidate SelectCandidate(
        IReadOnlyList<AlignmentCandidate> candidates,
        double baseDeltaMs)
    {
        AlignmentCandidate best = candidates[0];
        if (best.InvertPolarity)
        {
            AlignmentCandidate? bestNormal = candidates
                .Where(item => !item.InvertPolarity)
                .OrderByDescending(item => item.ScoreDb)
                .FirstOrDefault();
            if (bestNormal != null &&
                bestNormal.ScoreDb >= best.ScoreDb - InvertPreferenceMarginDb)
            {
                best = bestNormal;
            }
        }

        return candidates
            .Where(item => item.InvertPolarity == best.InvertPolarity &&
                item.ScoreDb >= best.ScoreDb - DelayTieMarginDb)
            .OrderBy(item => Math.Abs(item.DelayMs - baseDeltaMs))
            .First();
    }

    // Two-stage alignment. Stage 1: Time-Alignment-style band-limited first
    // arrivals give a coarse delay per channel (robust, no phase ambiguity).
    // Stage 2: walking pair by pair outward from the reference, a phase
    // correlation fine-tunes each channel against its settled neighbor inside
    // their shared pair band, also deciding whether its polarity should flip.
    // The latest-arriving channel is the fixed reference, so the proposed
    // delays stay non-negative by construction. Previous Auto/manual delay and
    // polarity settings are ignored: the command recomputes an absolute proposal
    // from the current sources, crossover filters, gains and PEQ every time.
    private void AutoAlignDelay()
    {
        var alignment = new Dictionary<ChannelRuntime, AlignmentOverride>();
        List<ProcessedChannel> processed = ProcessChannels(alignment);
        if (processed.Count < 2)
        {
            System.Media.SystemSounds.Beep.Play();
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

        // Stage 1: coarse offsets from band-limited first arrivals. Arrivals of
        // different drivers are only comparable inside a SHARED band — a woofer's
        // envelope in its own low band rises milliseconds later than a tweeter's
        // in its high band. So each adjacent pair is measured around its own
        // crossover frequency, and the pairwise differences chain into one
        // relative timeline.
        List<ProcessedChannel> byBand = OrderByBand(processed);
        List<AdjacentPair> pairs = GetAdjacentPairs(byBand);

        var timeline = new Dictionary<ChannelRuntime, double> { [byBand[0].Channel] = 0 };
        foreach (AdjacentPair pair in pairs)
        {
            double lowerArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                pair.Lower.ImpulseResponse,
                pair.Lower.Channel.SampleRate,
                pair.BandLowHz,
                pair.BandHighHz);
            double upperArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                pair.Upper.ImpulseResponse,
                pair.Upper.Channel.SampleRate,
                pair.BandLowHz,
                pair.BandHighHz);
            timeline[pair.Upper.Channel] =
                timeline[pair.Lower.Channel] + (upperArrival - lowerArrival);

            log.AppendLine(
                $"Pair {pair.Lower.Channel.Control.ChannelName}/" +
                $"{pair.Upper.Channel.Control.ChannelName}: " +
                $"fc {pair.CrossoverHz:0} Hz, " +
                $"band {pair.BandLowHz:0}-{pair.BandHighHz:0} Hz, " +
                $"arrivals {lowerArrival:0.000} / {upperArrival:0.000} ms, " +
                $"diff {upperArrival - lowerArrival:+0.000;-0.000} ms");
        }

        // The relatively latest channel is the fixed reference; everyone else is
        // delayed toward it, so the coarse deltas are non-negative.
        double latest = timeline.Values.Max();
        ChannelRuntime reference = timeline.First(pair => pair.Value == latest).Key;
        log.AppendLine($"Reference: {reference.Control.ChannelName}");

        // Stage 2: sequential pairwise fine alignment, walking outward from the
        // reference along the band order. Each channel is phase-correlated
        // against its already-settled neighbor only, inside their shared pair
        // band, so the search window is sized by THAT junction — a mid channel
        // must not have its low-junction window squeezed to the period of its
        // high junction. An arrival error at a low junction then propagates
        // through the chain and moves the whole upper group together, which a
        // per-channel search against all fixed channels at once cannot do.
        int referenceIndex = byBand.FindIndex(item => item.Channel == reference);
        var searchOrder =
            new List<(ProcessedChannel Target, ProcessedChannel Neighbor, AdjacentPair Pair)>();
        for (int i = referenceIndex - 1; i >= 0; i--)
        {
            searchOrder.Add((byBand[i], byBand[i + 1], pairs[i]));
        }
        for (int i = referenceIndex + 1; i < byBand.Count; i++)
        {
            searchOrder.Add((byBand[i], byBand[i - 1], pairs[i - 1]));
        }

        // One junction search: candidates of the prior-penalized loss score in
        // a window around the arrival-based delta. The window is half the
        // period of THIS junction's crossover — wide enough to absorb the
        // coarse arrival error (which grows with the period), narrow enough
        // not to span two same-polarity lobes. The base doubles as a soft
        // prior: a quadratic dB penalty that deters far lobes.
        (IReadOnlyList<AlignmentCandidate> Candidates, double BaseDelta, double HalfPeriodMs)
            SearchJunction(
                ChannelRuntime channel,
                ChannelRuntime neighborChannel,
                AdjacentPair junction,
                double? windowOverrideMs = null)
        {
            // Reprocess so the settled neighbors participate with their new
            // delays and polarities. The searched channel is dropped from the
            // override map so its response is the raw, undelayed IR — the search
            // provides the delay, and chosen.DelayMs is then the absolute delay
            // to assign. Without this reset, a uniform shift applied earlier to
            // a not-yet-searched channel (the negative-delay branch below) would
            // bake a stray offset into variableIr that the reported delay does
            // not account for, mis-aligning that channel by the shift.
            var searchAlignment = new Dictionary<ChannelRuntime, AlignmentOverride>(alignment);
            searchAlignment.Remove(channel);
            List<ProcessedChannel> current = ProcessChannels(searchAlignment);
            Complex[] variableIr = current
                .First(item => item.Channel == channel).ImpulseResponse;
            var neighborIrs = new List<Complex[]>
            {
                current.First(item => item.Channel == neighborChannel).ImpulseResponse
            };

            double halfPeriodMs = 500.0 / junction.CrossoverHz;
            double rangeMs = windowOverrideMs ?? Math.Clamp(
                halfPeriodMs, MinFineAlignmentRangeMs, MaxFineAlignmentRangeMs);
            double baseDelta = alignment.GetValueOrDefault(neighborChannel).DelayMs
                + timeline[neighborChannel] - timeline[channel];
            IReadOnlyList<AlignmentCandidate> candidates =
                VirtualCrossoverAnalysis.FindAlignmentCandidates(
                    variableIr,
                    neighborIrs,
                    channel.SampleRate,
                    junction.BandLowHz,
                    junction.BandHighHz,
                    baseDelta - rangeMs,
                    baseDelta + rangeMs,
                    priorDelayMs: baseDelta,
                    priorSigmaMs: rangeMs / 2);
            return (candidates, baseDelta, halfPeriodMs);
        }

        foreach ((ProcessedChannel target, ProcessedChannel neighbor, AdjacentPair pair)
            in searchOrder)
        {
            ChannelRuntime channel = target.Channel;
            (IReadOnlyList<AlignmentCandidate> candidates, double baseDelta,
                double halfPeriodMs) = SearchJunction(channel, neighbor.Channel, pair);
            log.AppendLine(
                $"Channel {channel.Control.ChannelName}: " +
                $"vs {neighbor.Channel.Control.ChannelName} " +
                $"in {pair.BandLowHz:0}-{pair.BandHighHz:0} Hz, " +
                $"base {baseDelta:0.000} ms, candidates " +
                string.Join("; ", candidates.Select(item =>
                    $"{item.DelayMs:0.000} ms" +
                    $"{(item.InvertPolarity ? " inv" : "")} " +
                    $"(score {item.ScoreDb:0.00}, avg {item.LossDb:0.00}, " +
                    $"dip {item.DipDb:0.0} dB)")));

            AlignmentCandidate chosen = candidates.Count > 0
                ? SelectCandidate(candidates, baseDelta)
                : new AlignmentCandidate(baseDelta, false, 0);
            if (candidates.Count > 0 && chosen != candidates[0])
            {
                log.AppendLine(
                    $"  preferred {chosen.DelayMs:0.000} ms" +
                    $"{(chosen.InvertPolarity ? " inv" : "")} over " +
                    $"{candidates[0].DelayMs:0.000} ms" +
                    $"{(candidates[0].InvertPolarity ? " inv" : "")} " +
                    $"(margin {candidates[0].ScoreDb - chosen.ScoreDb:0.00} dB)");
            }

            double fineRangeMs = Math.Clamp(
                halfPeriodMs, MinFineAlignmentRangeMs, MaxFineAlignmentRangeMs);

            // A result pinned to the window edge means the optimum lies beyond
            // the coarse estimate's reach — retry once, widened but still short
            // of a full period so the search cannot land on the next lobe. The
            // edge hit means the base itself is suspect, so the retry relaxes
            // the prior along with the window.
            double retryRangeMs = Math.Min(1.8 * halfPeriodMs, 3.0);
            if (retryRangeMs > fineRangeMs &&
                Math.Abs(chosen.DelayMs - baseDelta) >= fineRangeMs - 0.02)
            {
                (IReadOnlyList<AlignmentCandidate> retried, _, _) = SearchJunction(
                    channel, neighbor.Channel, pair, windowOverrideMs: retryRangeMs);
                if (retried.Count > 0)
                {
                    chosen = retried[0];
                }

                log.AppendLine(
                    $"  WARNING: fine result at the search edge; widened to " +
                    $"±{retryRangeMs:0.000} ms -> {chosen.DelayMs:0.000} ms, " +
                    $"invert {(chosen.InvertPolarity ? "yes" : "no")}");
            }

            double newDelay = chosen.DelayMs;
            if (newDelay < 0)
            {
                // A physically impossible negative delay: push every channel by
                // the deficit instead — a uniform shift preserves the alignment.
                double shift = -newDelay;
                foreach (ProcessedChannel item in processed)
                {
                    if (item.Channel != channel)
                    {
                        AlignmentOverride currentAlignment =
                            alignment.GetValueOrDefault(item.Channel);
                        alignment[item.Channel] = currentAlignment with
                        {
                            DelayMs = Math.Min(100, currentAlignment.DelayMs + shift)
                        };
                    }
                }
                newDelay = 0;
            }

            alignment[channel] = new AlignmentOverride(
                Math.Clamp(Math.Round(newDelay, 2), 0, 100),
                chosen.InvertPolarity);
        }

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
        // RedrawAll refreshed the metric, so the log ends with the outcome.
        log.AppendLine(labelMetric.Text);
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

    // The crossover frequency between two adjacent channels: the lower one's
    // low-pass corner when set, the upper one's high-pass corner otherwise, and
    // the geometric mean of their band centers as the filterless fallback.
    private static double GetPairCrossoverHz(
        VirtualCrossoverChannelSettings lower,
        VirtualCrossoverChannelSettings upper)
    {
        if (lower.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass)
        {
            return lower.LowPassEdge.FrequencyHz;
        }
        if (upper.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass)
        {
            return upper.HighPassEdge.FrequencyHz;
        }

        (double lowerLow, double lowerHigh) = GetChannelBand(lower);
        (double upperLow, double upperHigh) = GetChannelBand(upper);
        return Math.Sqrt(
            Math.Sqrt(lowerLow * lowerHigh) * Math.Sqrt(upperLow * upperHigh));
    }

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
            .OrderBy(item =>
            {
                (double lowHz, double highHz) = GetChannelBand(item.Channel.Settings);
                return Math.Sqrt(lowHz * highHz);
            })
            .ToList();

    private static List<AdjacentPair> GetAdjacentPairs(List<ProcessedChannel> byBand)
    {
        var pairs = new List<AdjacentPair>();
        for (int i = 0; i < byBand.Count - 1; i++)
        {
            double pairHz = GetPairCrossoverHz(
                byBand[i].Channel.Settings, byBand[i + 1].Channel.Settings);
            pairs.Add(new AdjacentPair(
                byBand[i],
                byBand[i + 1],
                pairHz,
                Math.Max(20, pairHz / 2),
                Math.Min(20_000, pairHz * 2)));
        }

        return pairs;
    }

    // The band a channel actually plays in: its crossover corners when set, the
    // full range otherwise. Used to order the channels along the spectrum.
    private static (double LowHz, double HighHz) GetChannelBand(
        VirtualCrossoverChannelSettings settings)
    {
        double lowHz =
            settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass
                ? settings.HighPassEdge.FrequencyHz
                : 20;
        double highHz =
            settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass
                ? settings.LowPassEdge.FrequencyHz
                : 20_000;
        return highHz > lowHz ? (lowHz, highHz) : (20, 20_000);
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
        double detrendMs = gatePreview?.DetrendMs
            ?? ResolveDetrendMs(reference, sampleRate);

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

    private List<SignalPoint> BuildPhasePoints(
        Complex[] impulseResponse,
        int sampleRate,
        double gateOffsetMs,
        double detrendMs)
    {
        // The same gate construction as the Phase mode: a Tukey window of
        // left + plateau + right whose left shoulder ends at the gate offset,
        // zero-padded to the fixed FFT length so the frequency grid is constant.
        int length = DataHelper.GatedFftLength;
        int gateOffset = (int)Math.Round(gateOffsetMs / 1_000.0 * sampleRate);
        int left = MillisecondsToSamples(
            gatePreview?.LeftMs ?? project.PhaseGateLeftMs, sampleRate);
        int plateau = MillisecondsToSamples(
            gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs, sampleRate);
        int right = MillisecondsToSamples(
            gatePreview?.RightMs ?? project.PhaseGateRightMs, sampleRate);

        int gate = Math.Clamp(left + plateau + right, 1, length);
        left = Math.Min(left, gate);
        right = Math.Min(right, gate - left);

        double[] tukey = Windowing.TukeyWindow(
            gate,
            (double)left / gate * 2.0,
            (double)right / gate * 2.0);
        double[] window = new double[length];
        Array.Copy(tukey, window, gate);

        int gateStart = gateOffset - left;
        // The τ detrend is the phase reference. GetPhaseData re-references to a
        // whole sample (the view's PeakIndex); the fractional remainder is
        // applied per point below, so τ resolution is not limited to samples.
        double detrendSamples = detrendMs / 1_000.0 * sampleRate;
        int referenceSample = Math.Clamp(
            (int)Math.Round(detrendSamples), 0, impulseResponse.Length - 1);
        double residualSeconds = (detrendSamples - referenceSample) / sampleRate;

        var view = new ImpulseMeasurementView(impulseResponse, referenceSample, sampleRate);
        // GetPhaseData extracts at PeakIndex + offset and re-references the phase
        // to PeakIndex, so every curve built with the same τ is directly
        // comparable regardless of where the gate sits.
        List<SignalPoint> phase = DataHelper.GetPhaseData(
            view, gateStart - referenceSample, length, window, unwrap: false);

        var points = new List<SignalPoint>(phase.Count);
        foreach (SignalPoint point in phase)
        {
            if (point.X is < 20 or > 20_000)
            {
                continue;
            }

            double corrected = point.Y + Math.Tau * point.X * residualSeconds;
            corrected = Math.Atan2(Math.Sin(corrected), Math.Cos(corrected));
            points.Add(new SignalPoint(point.X, corrected / Math.PI * 180.0));
        }

        return points;
    }

    private static int MillisecondsToSamples(double milliseconds, int sampleRate) =>
        (int)Math.Round(Math.Max(0.0, milliseconds) * sampleRate / 1_000.0);

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
            fitOffsetMs);
        // The callback is wired after Init so seeding the controls does not
        // trigger a redundant redraw; from here every dialog change repaints the
        // phase plot immediately.
        dialog.PreviewChanged = (offsetMs, leftMs, plateauMs, rightMs, detrendMs) =>
        {
            gatePreview = (offsetMs, leftMs, plateauMs, rightMs, detrendMs);
            RedrawMainPlot();
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
                ScheduleSave();
            }
        }
        finally
        {
            // Save committed the candidate values, Cancel discards them; either
            // way the plot re-renders from the project state.
            gatePreview = null;
            RedrawMainPlot();
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

        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 512);
        for (int i = 0; i < channels.Count; i++)
        {
            ChannelRuntime channel = channels[i];
            if (!channel.Settings.Enabled || channel.TransferImpulseResponse == null)
            {
                continue;
            }

            // The chain is drawn without its delay term: the filters' phase shape
            // is the readable part, while a delay would wrap the trace into an
            // unreadable sawtooth (its effect is visible on the acoustic plot).
            DspChannelChain chain = channel.Settings.ToChain() with { DelayMs = 0 };
            PreparedDspResponse preparedResponse = PreparedDspResponse.Create(
                chain,
                channel.SampleRate);

            var magnitudePoints = new List<DataPoint>(grid.Count);
            var phasePoints = new List<DataPoint>(grid.Count);
            foreach (double frequency in grid)
            {
                Complex response = preparedResponse.Response(frequency);
                magnitudePoints.Add(new DataPoint(
                    frequency, DataHelper.AmplitudeToDecibels(response.Magnitude)));
                phasePoints.Add(new DataPoint(
                    frequency, response.Phase / Math.PI * 180.0));
            }

            string name = channel.Control.ChannelName;
            AddDspSeries(
                model, $"{name} filter", magnitudePoints,
                ChannelColors[i], 1.8, LineStyle.Solid, "dsp-magnitude");
            AddDspSeries(
                model, $"{name} phase", phasePoints,
                OxyColor.FromAColor(140, ChannelColors[i]), 1.2, LineStyle.Dash, "dsp-phase");
        }

        model.InvalidatePlot(true);
    }

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

        string metricLine = labelMetric.Text;
        int sampleRate = channels
            .Where(channel => channel.TransferImpulseResponse != null)
            .Select(channel => channel.SampleRate)
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
            IReadOnlyList<SignalPoint> MagnitudeDb, DriverBandEstimate Band)>();
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
                OxyColor accent = ChannelColors[channels.IndexOf(channel)];
                dialogChannels.Add((
                    $"{channel.Control.ChannelName} — {channel.Settings.DisplayName}",
                    Color.FromArgb(accent.R, accent.G, accent.B),
                    curve.Points,
                    CrossoverAutoSetup.EstimateBand(curve.Points)));
            }
        }
        catch (ArgumentException exception)
        {
            ShowError("A channel's response has no usable band.", exception.Message);
            return;
        }

        using var dialog = new VirtualCrossoverAutoSetupDialog();
        dialog.Init(dialogChannels);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } proposals)
        {
            return;
        }

        for (int i = 0; i < participating.Count; i++)
        {
            VirtualCrossoverChannelSettings settings = participating[i].Settings;
            CrossoverProposal proposal = proposals[i];
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
            ApplySettingsToControl(participating[i]);
        }

        ScheduleSave();
        RedrawAll();
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

    // Runtime state of one channel block: the bound project settings plus the
    // resolved source measurement (null while unresolved).
    private sealed class ChannelRuntime
    {
        public ChannelRuntime(VirtualCrossoverChannelControl control)
        {
            Control = control;
        }

        public VirtualCrossoverChannelControl Control { get; }
        public VirtualCrossoverChannelSettings Settings { get; set; } = new();
        public Complex[]? TransferImpulseResponse { get; set; }
        public int TransferPeakIndex { get; set; }
        public int SampleRate { get; set; }
        public ProcessedChannelCache? ProcessedCache { get; set; }
    }

    private sealed record ProcessedChannelCache(
        ProcessedChannelCacheKey Key,
        Complex[] ImpulseResponse,
        int PeakIndex);
}
