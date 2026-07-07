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
        private readonly ExpSweepMeasurement expSweepMeasurement = new();
        private readonly NoiseMeasurement noiseMeasurement = new();
        private readonly Dictionary<string, CalibrationFile> calibrationCache = new(
            StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> reportedCalibrationProblems = new(
            StringComparer.OrdinalIgnoreCase);
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
        private readonly Dictionary<Mode, List<int>> activeOverlaySlotsByMode = new();
        private readonly PlotLabelsPanelController plotLabelsPanelController;
        private readonly InputLevelMeterController inputLevelMeterController;
        private readonly DockedModeSettingsHost dockedModeSettingsHost;
        private readonly DockedModeSettingsHost dockedMeasurementSettingsHost;
        private readonly DockedModeSettingsHost dockedHistoryHost;
        private Guid? currentHistoryEntryId;
        private bool hasCurrentImpulseResponse;
        private bool closingPrepared;
        private bool closingInProgress;
        private bool resourcesDisposed;
        private bool updateCheckStarted;
        private bool measurementSettingsSavePending;
        private readonly System.Windows.Forms.Timer measurementSettingsSaveTimer = new()
        {
            Interval = MeasurementSettingsSaveDelayMilliseconds
        };
        private readonly System.Windows.Forms.Timer recordButtonLongPressTimer = new()
        {
            Interval = RecordButtonLongPressMilliseconds
        };
        private CompareMeasurementSelection? compareMeasurement;
        private ContextMenuStrip? compareMenuStrip;
        private CancellationTokenSource? startupAudioWarmupCancellation;
        private Task? startupAudioWarmupTask;
        private bool recordButtonLongPressTriggered;
        private bool suppressNextRecordButtonClick;

        public Form1()
        {
            InitializeComponent();
            ConfigureToolTips();
            PlotInteraction.EnableDoubleClickAxisReset(plotView1);
            plotView1.Paint += (_, _) => AppProfiler.FrameMark("main-plot");
            measurementSettings = MeasurementSettingsFile.LoadOrDefault();
            Form1ControllerDependencies dependencies = CreateControllerDependencies();
            overlayCollection = dependencies.OverlayCollection;
            plotLabelsPanelController = dependencies.PlotLabelsPanelController;
            plotModelFactory = dependencies.PlotModelFactory;
            liveSpectrumController = dependencies.LiveSpectrumController;
            modeController = dependencies.ModeController;
            commandController = dependencies.CommandController;
            timeAlignmentController = dependencies.TimeAlignmentController;
            inputLevelMeterController = dependencies.InputLevelMeterController;
            dockedModeSettingsHost = dependencies.DockedModeSettingsHost;
            dockedMeasurementSettingsHost = dependencies.DockedMeasurementSettingsHost;
            dockedHistoryHost = dependencies.DockedHistoryHost;
            eqWizardPanel.RenderProvider = overlayCollection.BuildEqWizardRender;
            eqWizardPanel.TargetOffsetSetter = overlayCollection.ApplyEqWizardTargetOffset;
            eqWizardPanel.ResultsChanged = eqResultsPanel.SetResults;
            eqWizardPanel.OverlaySettingsRequested = OpenEqWizardOverlaySettings;
            signalGeneratorPanel.PlaybackSettingsProvider = CreateSignalGeneratorPlaybackSettings;
            virtualCrossoverPanel.HistoryService = measurementHistoryService;
            RefreshCalibrationConsumers();
            virtualCrossoverPanel.OverlayCaptureRequested = SaveVirtualCrossoverOverlay;
            virtualCrossoverPanel.MetricChanged = (text, detail) =>
            {
                virtualDspMetricLabel.Text = text;
                toolTip1.SetToolTip(virtualDspMetricLabel, detail);
            };
            modeDescriptors = CreateModeDescriptors();
            ApplyPersistedSettings();
            WireControllerEvents();
            InitializeStartupState();
            WireFormEvents();
        }

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

            MessageBox.Show(
                this,
                $"{summary}\r\n\r\n{error.Message}",
                "Measurement",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        // A configured calibration that fails to load must not silently produce
        // uncalibrated curves. Warned once per path per session; queued through
        // BeginInvoke so a plot build is never interrupted by a modal dialog.
        // Called from Task.Run plot builds, so the set is guarded by a lock.
        private void WarnCalibrationProblemOnce(string path, string? reason)
        {
            if (closingInProgress)
            {
                return;
            }
            lock (reportedCalibrationProblems)
            {
                if (!reportedCalibrationProblems.Add(path))
                {
                    return;
                }
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
                expSweepMeasurement.AsioOutputChannelOffset);

    }
}
