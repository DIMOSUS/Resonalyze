using System.Windows.Forms;
using Resonalyze.Dsp;
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
        TimeAlignment
    }

    public partial class Form1 : Form
    {
        private const string PeakInfoAnnotationTag = "PeakInfoAnnotation";

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
            Window = 2048,
            LeftTukeyWindow = 16,
            RightTukeyWindow = 256,
            SmoothingInverseOctaves = 12,
            Offset = 0,
        };
        private readonly FrequencyResponseOptions groupDelayOptions = new()
        {
            Window = 2048,
            LeftTukeyWindow = 16,
            RightTukeyWindow = 256,
            SmoothingInverseOctaves = 12,
            Offset = 0,
        };
        private readonly ImpulseResponseOptions impulseResponseOptions = new();
        private readonly LiveSpectrumOptions liveSpectrumOptions = new();
        private readonly TimeAlignmentOptions timeAlignmentOptions = new();
        private readonly PlotModelFactory plotModelFactory;
        private readonly ModeController modeController;
        private readonly ChromeTitleBarController titleBarController;
        private readonly LiveSpectrumController liveSpectrumController;
        private readonly TimeAlignmentPanelController timeAlignmentController;
        private readonly MainCommandController commandController;
        private readonly MeasurementSettingsFile measurementSettings;
        private readonly IReadOnlyDictionary<ModeTab, ModeDescriptor> modeDescriptors;
        private readonly PlotLabelsPanelController plotLabelsPanelController;
        private readonly InputLevelMeterController inputLevelMeterController;
        private readonly DockedModeSettingsHost dockedModeSettingsHost;
        private readonly DockedModeSettingsHost dockedMeasurementSettingsHost;
        private bool hasCurrentImpulseResponse;
        private bool closingPrepared;
        private bool resourcesDisposed;
        private bool updateCheckStarted;

        public Form1()
        {
            InitializeComponent();
            ConfigureToolTips();
            measurementSettings = MeasurementSettingsFile.LoadOrDefault();
            Form1ControllerDependencies dependencies = CreateControllerDependencies();
            titleBarController = dependencies.TitleBarController;
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
            modeDescriptors = CreateModeDescriptors();
            ApplyPersistedSettings();
            WireControllerEvents();
            InitializeStartupState();
            WireFormEvents();
        }

    }
}
