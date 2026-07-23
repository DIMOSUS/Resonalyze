using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.History;
using Resonalyze.Options;

namespace Resonalyze
{
    public enum Mode : int
    {
        None = 0,
        ImpulseResponse,
        FrequencyResponse,
        PhaseResponse,
        GroupDelay,
        CumulativeSpectrumDecay,
        BurstDecay,
        LiveSpectrum,
        Autocorrelation,
        TimeAlignment,
        EqWizard,
        SignalGenerator,
        VirtualCrossover
    }

    public partial class Form1 : Form
    {
        private const string PeakInfoAnnotationTag = "PeakInfoAnnotation";
        private const int MeasurementSettingsSaveDelayMilliseconds = 10_000;
        private const int RecordButtonLongPressMilliseconds = 650;

        public Mode CurrentMode { get; private set; }

        private readonly OverlayCollection overlayCollection;
        // Composition root: the one place the audio backends are wired together.
        // Everything downstream depends only on the IAudioSessionFactory abstraction.
        private readonly IAudioSessionFactory audioSessionFactory =
            new AudioSessionFactory(AudioBackendRegistry.CreateDefault());
        private readonly ExpSweepMeasurement expSweepMeasurement;
        private readonly NoiseMeasurement noiseMeasurement;
        private readonly MicrophoneCalibrationService microphoneCalibration;
        private readonly WaterfallGenerateOptions waterfallGenOptions = new()
        {
            WaterfallMode = WaterfallMode.Fourier,
        };
        private readonly WaterfallGenerateOptions burstDecayGenOptions = new()
        {
            WaterfallMode = WaterfallMode.BurstDecay,
            Window = 1024,
            LeftTukeyWindow = 8,
            RightTukeyWindow = 128,
            SmoothingInverseOctaves = 6,
        };

        private readonly FrequencyResponseOptions frequencyResponseOptions = new();
        private readonly FrequencyResponseOptions phaseResponseOptions = new()
        {
            // Phase windowing (gate ms + τ) comes from the FrequencyResponseOptions
            // defaults; only the smoothing default differs from the shared value.
            SmoothingInverseOctaves = FrequencyResponseOptions.DefaultPhaseSmoothingInverseOctaves,
        };
        private readonly FrequencyResponseOptions groupDelayOptions = new()
        {
            // Group delay uses the ms-based gate (left fade + plateau + right fade)
            // from the FrequencyResponseOptions defaults; only smoothing differs from
            // the shared value.
            SmoothingInverseOctaves = FrequencyResponseOptions.DefaultGroupDelaySmoothingInverseOctaves,
        };
        // Per-mode curve visibility (presentation flags), one instance per
        // frequency-response-family mode, mirroring the options objects above.
        private readonly CurveVisibilityOptions frequencyResponseVisibility = new();
        private readonly CurveVisibilityOptions phaseResponseVisibility = new();
        private readonly CurveVisibilityOptions groupDelayVisibility = new();
        private readonly ImpulseResponseOptions impulseResponseOptions = new();
        private readonly LiveSpectrumOptions liveSpectrumOptions = new();
        private readonly TimeAlignmentOptions timeAlignmentOptions = new();
        private readonly PlotModelFactory plotModelFactory;
        private readonly ModeController modeController;
        private readonly LiveSpectrumController liveSpectrumController;
        private readonly TimeAlignmentPanelController timeAlignmentController;
        private readonly MainCommandController commandController;
        private readonly MeasurementSettingsFile measurementSettings;
        private readonly MeasurementHistoryService measurementHistoryService = new();
        private readonly IReadOnlyDictionary<ModeTab, ModeDescriptor> modeDescriptors;
        private readonly ActiveOverlaySlotTracker activeOverlaySlots = new();
        private readonly PlotLabelsPanelController plotLabelsPanelController;
        private readonly InputLevelMeterController inputLevelMeterController;
        private readonly DockedModeSettingsHost dockedModeSettingsHost;
        private readonly DockedModeSettingsHost dockedMeasurementSettingsHost;
        private readonly DockedModeSettingsHost dockedHistoryHost;
        private readonly MeasurementSessionTracker sessionTracker;
        private bool closingPrepared;
        private bool closingInProgress;
        private bool resourcesDisposed;
        // Set on CloseReason.WindowsShutDown: DisposeAppResources must skip its
        // blocking device teardown while the OS is waiting for the process to exit.
        private bool shutdownFastClose;
        private bool updateCheckStarted;
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
            PlotInteraction.EnableDoubleClickAxisReset(plotView1);
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
                GetConfiguredMicrophoneCalibrationPath,
                ShowCalibrationProblem);
            sessionTracker = new MeasurementSessionTracker(
                measurementHistoryService,
                CaptureCurrentSessionSnapshot);
            Form1ControllerDependencies dependencies = CreateControllerDependencies();
            overlayCollection = dependencies.OverlayCollection;
            plotLabelsPanelController = dependencies.PlotLabelsPanelController;
            plotModelFactory = dependencies.PlotModelFactory;
            // Overlays are tagged with, and gated by, the magnitude scale the plot is
            // ACTUALLY rendered in — SPL only when the plot is showing SPL (selected and
            // available). Live Spectrum shares Frequency Response's overlay slots, so it
            // must report its OWN effective scale here; otherwise a Live SPL plot would
            // gate overlays as Relative and show dBr overlays on the dB SPL axis. Every
            // other mode, and any dBr fallback, is Relative. Wired after plotModelFactory
            // is assigned (the lambda reads it).
            overlayCollection.SetMagnitudeScaleProvider(
                () => CurrentMode switch
                {
                    Mode.FrequencyResponse => plotModelFactory.EffectiveFrequencyResponseScale,
                    Mode.LiveSpectrum => plotModelFactory.EffectiveLiveSpectrumScale,
                    _ => Dsp.MagnitudeScale.Relative
                });
            liveSpectrumController = dependencies.LiveSpectrumController;
            // Lets a captured overlay store the RAW (unsmoothed) reference and seed its
            // own smoothing with the mode's, so lowering the overlay's smoothing to Off
            // reveals the original curve instead of the mode-smoothed one. The live RTA
            // is served by its controller (which holds the drawn snapshot); every swept
            // curve by the plot factory. Wired after both are assigned.
            overlayCollection.SetRawCurveProvider(tag =>
                tag == LiveSpectrumController.LiveSpectrumInputMagnitudeTag
                    ? liveSpectrumController.BuildRawRtaCapture()
                    : plotModelFactory.BuildRawCurve(tag));
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
            eqWizardPanel.SettingsChanged += () =>
            {
                measurementSettings.EqWizard = eqWizardPanel.CaptureSettings();
                ScheduleMeasurementSettingsSave();
            };
            signalGeneratorPanel.PlaybackSettingsProvider = CreateSignalGeneratorPlaybackSettings;
            signalGeneratorPanel.AudioSessionFactory = audioSessionFactory;
            virtualCrossoverPanel.HistoryService = measurementHistoryService;
            RefreshCalibrationConsumers();
            virtualCrossoverPanel.OverlayCaptureRequested = SaveVirtualCrossoverOverlay;
            virtualCrossoverPanel.MetricChanged = (text, detail) =>
            {
                virtualDspMetricLabel.Text = text;
                virtualDspMetricDetail = detail;
            };
            // The metric breakdown is long, and the automatic ToolTip auto-pops
            // after seconds (capped at ~32 s) — unreadable. Shown manually it
            // stays until the mouse leaves the label. The tip is placed fully
            // to the LEFT of the label on purpose: a tip under the cursor
            // steals the mouse, fires MouseLeave and flickers in a
            // show-hide-show loop.
            virtualDspMetricLabel.MouseEnter += (_, _) =>
            {
                if (virtualDspMetricDetail.Length == 0)
                {
                    return;
                }

                Size tipSize = TextRenderer.MeasureText(
                    virtualDspMetricDetail, SystemFonts.StatusFont ?? Font);
                toolTip1.Show(
                    virtualDspMetricDetail,
                    virtualDspMetricLabel,
                    -tipSize.Width - 16,
                    0);
            };
            virtualDspMetricLabel.MouseLeave += (_, _) =>
                toolTip1.Hide(virtualDspMetricLabel);
            modeDescriptors = CreateModeDescriptors();
            ApplyPersistedSettings();
            WireControllerEvents();
            InitializeStartupState();
            WireFormEvents();
        }

        // The full Virtual DSP metric breakdown, shown as a persistent tooltip
        // by the MouseEnter wiring in the constructor.
        private string virtualDspMetricDetail = string.Empty;

        // BeginInvoke can still throw if the handle is destroyed between the guard
        // and the call — measurement events arrive from audio worker threads while
        // the form closes on the UI thread.
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

        // Measurement failures used to surface only as an "Error" record button;
        // the stored exception text was never shown to the user.
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

        // A configured calibration that fails to load must not silently produce
        // uncalibrated curves. MicrophoneCalibrationService already deduplicates
        // (once per path per session) and calls this from Task.Run plot builds,
        // so the warning is queued through BeginInvoke — a plot build is never
        // interrupted by a modal dialog.
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
