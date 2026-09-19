using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze
{
    public partial class Form1 : Form
    {
        private const int MeasurementSettingsSaveDelayMilliseconds = 10_000;
        private const int RecordButtonLongPressMilliseconds = 650;

        public Mode CurrentMode => analyzerPlot.Mode;

        // The main plot: draws the open measurement in the active mode, with its overlays and zoom.
        private readonly AnalyzerPlot analyzerPlot;
        // Composition root: the one place audio backends are wired.
        private readonly IAudioSessionFactory audioSessionFactory =
            new AudioSessionFactory(AudioBackendRegistry.CreateDefault());
        private readonly ExpSweepMeasurement expSweepMeasurement;
        // The open measurement every analysis mode reads; the engine only produces the next one.
        private readonly AnalyzerDocument analyzerDocument = new();
        private readonly NoiseMeasurement noiseMeasurement;
        private readonly MicrophoneCalibrationService microphoneCalibration;
        // Every mode's options; the panels edit them in place and the plot builds read them.
        private readonly AnalyzerViewSettings viewSettings = new();
        private readonly PlotModelFactory plotModelFactory;
        private readonly ModeController modeController;
        private readonly LiveSpectrumController liveSpectrumController;
        private readonly TimeAlignmentPanelController timeAlignmentController;
        private readonly MainCommandController commandController;
        private readonly MeasurementSettingsFile measurementSettings;
        private readonly MeasurementHistoryService measurementHistoryService = new();
        private readonly InputLevelMeterController inputLevelMeterController;
        private readonly DockedModeSettingsHost dockedModeSettingsHost;
        private readonly DockedModeSettingsHost dockedMeasurementSettingsHost;
        private readonly DockedModeSettingsHost dockedHistoryHost;
        private readonly MeasurementSessionTracker sessionTracker;
        private bool closingPrepared;
        private bool closingInProgress;
        private bool resourcesDisposed;
        // DisposeAppResources skips blocking device teardown during OS shutdown.
        private bool shutdownFastClose;
        private bool updateCheckStarted;
        // Edits arriving during an apply are coalesced into one re-run.
        private bool applyingSweepSettings;
        private bool sweepSettingsApplyPending;
        private readonly DebouncedSaver measurementSettingsSaver;
        private readonly StartupAudioWarmup startupAudioWarmup;
        private readonly ButtonLongPressBehavior recordButtonLongPress;
        private readonly CompareSelection compareSelection = new();
        private ContextMenuStrip? compareMenuStrip;

        public Form1()
        {
            InitializeComponent();
            expSweepMeasurement = new ExpSweepMeasurement(audioSessionFactory);
            noiseMeasurement = new NoiseMeasurement(audioSessionFactory);
            ConfigureToolTips();
            // PlotView paints only BackColor; axis colours come from the model (PlotModelStyle.ApplyChrome).
            plotView1.BackColor = UiPalette.GraphSurface;
            PlotInteraction.Enable(plotView1);
            plotView1.Paint += (_, _) => AppProfiler.FrameMark("main-plot");
            measurementSettings = MeasurementSettingsFile.LoadOrDefault();
            measurementSettingsSaver = new DebouncedSaver(
                MeasurementSettingsSaveDelayMilliseconds,
                measurementSettings.Save);
            startupAudioWarmup = new StartupAudioWarmup(WarmUpStartupAudioAsync);
            recordButtonLongPress = new ButtonLongPressBehavior(
                buttonRecord,
                RecordButtonLongPressMilliseconds,
                CanLongPressCancelMeasurementSeries,
                async () =>
                {
                    buttonRecord.Text = "Aborting...";
                    await expSweepMeasurement.AbortAsync();
                });
            microphoneCalibration = new MicrophoneCalibrationService(
                () => measurementSettings.Measurement.MicrophoneCalibration0DegreesPath,
                () => measurementSettings.Measurement.AdditionalMicrophoneCalibrations,
                ShowCalibrationProblem);
            sessionTracker = new MeasurementSessionTracker(
                measurementHistoryService,
                analyzerDocument,
                CaptureCurrentSessionSnapshot);
            Form1ControllerDependencies dependencies = CreateControllerDependencies();
            plotModelFactory = dependencies.PlotModelFactory;
            analyzerPlot = dependencies.AnalyzerPlot;
            liveSpectrumController = dependencies.LiveSpectrumController;
            modeController = dependencies.ModeController;
            commandController = dependencies.CommandController;
            timeAlignmentController = dependencies.TimeAlignmentController;
            inputLevelMeterController = dependencies.InputLevelMeterController;
            dockedModeSettingsHost = dependencies.DockedModeSettingsHost;
            dockedMeasurementSettingsHost = dependencies.DockedMeasurementSettingsHost;
            dockedHistoryHost = dependencies.DockedHistoryHost;
            eqWizardPanel.ResultsChanged = eqResultsPanel.SetResults;
            eqWizardPanel.HistoryService = measurementHistoryService;
            eqWizardPanel.ApplyPersistedSettings(measurementSettings.EqWizard);
            // Q convention belongs to the DSP being tuned, so it is top-level; VDSP reads it off its processor profile instead.
            eqWizardPanel.TargetDspQConvention = measurementSettings.TargetDspQConvention;
            eqWizardPanel.SettingsChanged += () =>
            {
                measurementSettings.EqWizard = eqWizardPanel.CaptureSettings();
                measurementSettings.TargetDspQConvention = eqWizardPanel.ManualQConvention;
                virtualCrossoverPanel.SetTargetCurve(eqWizardPanel.TargetCurve);
                ScheduleMeasurementSettingsSave();
            };
            signalGeneratorPanel.PlaybackSettingsProvider = CreateSignalGeneratorPlaybackSettings;
            signalGeneratorPanel.AudioSessionFactory = audioSessionFactory;
            virtualCrossoverPanel.HistoryService = measurementHistoryService;
            RefreshCalibrationConsumers();
            virtualCrossoverPanel.OverlayCaptureRequested = analyzerPlot.SaveFrequencyResponseOverlay;
            // The wizard owns the EQ target; it ignores an equal value, so the write-back cannot loop.
            virtualCrossoverPanel.SetTargetCurve(eqWizardPanel.TargetCurve);
            virtualCrossoverPanel.TargetCurveChanged = eqWizardPanel.ApplyTargetCurve;
            // If the channel no longer exists the wizard stays open so the tune is not lost.
            virtualCrossoverPanel.EditPeqInWizardRequested = request =>
            {
                eqWizardPanel.BeginVirtualDspHandoff(request);
                _ = modeController.SelectAsync(ModeTab.ToolsEqWizard);
            };
            // AI import fits with the wizard's current Auto Tune settings, matching the button.
            virtualCrossoverPanel.AutoTunePolicyProvider = () => eqWizardPanel.CurrentAutoTunePolicy;
            virtualCrossoverPanel.OpenSourceInAnalyzersRequested =
                (entryId, filePath) =>
                    _ = OpenVirtualDspSourceInAnalyzersAsync(entryId, filePath);
            eqWizardPanel.BackToVirtualDspRequested = () =>
                _ = modeController.SelectAsync(ModeTab.ToolsVirtualCrossover);
            // A refused return leaves the constructor open with its design.
            virtualCrossoverPanel.EditFirInConstructorRequested = request =>
            {
                firConstructorPanel.BeginVirtualDspHandoff(request);
                _ = modeController.SelectAsync(ModeTab.ToolsFirConstructor);
            };
            firConstructorPanel.BackToVirtualDspRequested = () =>
                _ = modeController.SelectAsync(ModeTab.ToolsVirtualCrossover);
            firConstructorPanel.ReturnFirRequested = (token, kernel, design) =>
            {
                if (virtualCrossoverPanel.TryApplyFirFromConstructor(token, kernel, design))
                {
                    // The token names the previous kernel, so a second return would be refused.
                    firConstructorPanel.EndVirtualDspHandoff();
                    _ = modeController.SelectAsync(ModeTab.ToolsVirtualCrossover);
                    return;
                }

                MessageBox.Show(
                    this,
                    "This FIR filter cannot be returned: the channel side it was opened for " +
                    "has changed since. The channel may have been removed or replaced by " +
                    "another project, switched between stereo and mono, had its FIR filter " +
                    "imported, cleared or copied over, or the DSP processor may have changed " +
                    "its rate or stopped taking FIR filters." +
                    Environment.NewLine + Environment.NewLine +
                    "The design stays here: export it, or open the channel's FIR menu again " +
                    "to design against what it holds now.",
                    "FIR Constructor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            };
            eqWizardPanel.ReturnPeqRequested = (token, curve, targetLevelDb) =>
            {
                if (virtualCrossoverPanel.TryApplyPeqFromWizard(
                        token, curve, targetLevelDb))
                {
                    _ = modeController.SelectAsync(ModeTab.ToolsVirtualCrossover);
                    return;
                }

                MessageBox.Show(
                    this,
                    "This PEQ cannot be returned: what it was tuned against has " +
                    "changed since. The channel may have been removed or replaced " +
                    "by another project, given a different measurement, had its " +
                    "DSP chain edited, its PEQ replaced or cleared, its gate moved, " +
                    "switched between stereo and mono, or the microphone calibration " +
                    "or target level may have changed — a bank fitted against one of " +
                    "those does not belong to the other." +
                    Environment.NewLine + Environment.NewLine +
                    "The filters stay here: export them, or start a fresh edit from " +
                    "the channel's PEQ menu to tune against what it shows now.",
                    "EQ Wizard",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            };
            virtualCrossoverPanel.MetricChanged = (text, detail) =>
            {
                virtualDspMetricLabel.Text = text;
                virtualDspMetricDetail = detail;
            };
            // Warning in the free right column; WinForms tooltips never wrap prose, so wrap here.
            virtualCrossoverPanel.WarningChanged = (text, detail, color) =>
            {
                virtualDspWarningLabel.ForeColor = color;
                virtualDspWarningLabel.Text = text;
                virtualDspWarningDetail = ToolTipTextWrapper.Wrap(detail);
                virtualDspWarningLabel.Visible =
                    text.Length > 0 && virtualCrossoverPanel.Visible;
            };
            WirePersistentTooltip(virtualDspMetricLabel, () => virtualDspMetricDetail);
            WirePersistentTooltip(virtualDspWarningLabel, () => virtualDspWarningDetail);
            ApplyPersistedSettings();
            WireControllerEvents();
            InitializeStartupState();
            WireFormEvents();
        }

        private string virtualDspMetricDetail = string.Empty;
        private string virtualDspWarningDetail = string.Empty;

        // Shown manually: auto ToolTips pop after at most ~32 s. Placed left of the label, since a tip under the cursor flickers.
        private void WirePersistentTooltip(Control control, Func<string> detail)
        {
            control.MouseEnter += (_, _) =>
            {
                string text = detail();
                if (text.Length == 0)
                {
                    return;
                }

                Size tipSize = TextRenderer.MeasureText(
                    text, SystemFonts.StatusFont ?? Font);
                toolTip1.Show(text, control, -tipSize.Width - 16, 0);
            };
            control.MouseLeave += (_, _) => toolTip1.Hide(control);
        }

        // BeginInvoke can throw if the handle dies after the guard (events come from audio threads during close).
        private bool TryBeginInvokeOnUiThread(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return false;
            }

            try
            {
                BeginInvoke(action);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void ShowMeasurementError(string summary, Exception? error)
        {
            if (error == null || closingInProgress)
            {
                return;
            }

            string? logPath = TryWriteMeasurementErrorLog(error);
            string logNotice = logPath == null
                ? string.Empty
                : $"\r\n\r\nFull details: {logPath}";
            MessageBox.Show(
                this,
                $"{summary}\r\n\r\n{error.Message}{logNotice}",
                "Measurement",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static string? TryWriteMeasurementErrorLog(Exception error)
        {
            try
            {
                string path = ApplicationDataPaths.Current.MeasurementErrorLogFile;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(
                    path,
                    $"[{DateTimeOffset.Now:O}]\r\n{error}\r\n\r\n");
                return path;
            }
            catch
            {
                return null;
            }
        }

        // Called from Task.Run plot builds (deduplicated per path), so queued via BeginInvoke rather than a modal dialog mid-build.
        private void ShowCalibrationProblem(string path, string? reason)
        {
            if (closingInProgress)
            {
                return;
            }

            TryBeginInvokeOnUiThread(() =>
            {
                if (closingInProgress)
                {
                    return;
                }

                MessageBox.Show(
                    this,
                    "The selected microphone calibration could not be loaded; " +
                    $"curves are shown uncalibrated.\r\n\r\n{reason ?? path}",
                    "Microphone calibration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            });
        }

        private SignalGeneratorPlaybackSettings CreateSignalGeneratorPlaybackSettings() =>
            new(
                expSweepMeasurement.AudioBackend,
                expSweepMeasurement.SampleRate,
                expSweepMeasurement.Bits,
                expSweepMeasurement.PlaybackChannel,
                expSweepMeasurement.OutputDeviceNumber,
                expSweepMeasurement.AsioDriverName,
                expSweepMeasurement.AsioOutputChannelOffset,
                expSweepMeasurement.WasapiRenderEndpointId,
                expSweepMeasurement.WasapiRenderEndpointName,
                expSweepMeasurement.WasapiBufferMilliseconds);

    }
}
