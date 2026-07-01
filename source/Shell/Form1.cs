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
        SignalGenerator
    }

    public partial class Form1 : Form
    {
        private const string PeakInfoAnnotationTag = "PeakInfoAnnotation";
        private const int MeasurementSettingsSaveDelayMilliseconds = 10_000;

        public Mode CurrentMode { get; private set; }

        private readonly OverlayCollection overlayCollection;
        private readonly ExpSweepMeasurement expSweepMeasurement = new();
        private readonly NoiseMeasurement noiseMeasurement = new();
        private readonly CalibrationFile calibration = new(
            Path.Combine(AppContext.BaseDirectory, "calibration.txt"));
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
        private bool resourcesDisposed;
        private bool updateCheckStarted;
        private bool measurementSettingsSavePending;
        private readonly System.Windows.Forms.Timer measurementSettingsSaveTimer = new()
        {
            Interval = MeasurementSettingsSaveDelayMilliseconds
        };
        private CancellationTokenSource? startupAudioWarmupCancellation;
        private Task? startupAudioWarmupTask;

        public Form1()
        {
            InitializeComponent();
            ConfigureToolTips();
            PlotInteraction.EnableDoubleClickAxisReset(plotView1);
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
            modeDescriptors = CreateModeDescriptors();
            ApplyPersistedSettings();
            WireControllerEvents();
            InitializeStartupState();
            WireFormEvents();
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
