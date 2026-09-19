using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
    /// <summary>The open measurement's rate, or the one the next run is configured for.</summary>
    private int OpenSampleRate => analyzerDocument.Result?.SampleRate ?? expSweepMeasurement.SampleRate;

    private ModeDescriptor GetActiveModeDescriptor() =>
        GetModeDescriptor(modeController.ActiveTab);

    private ModeDescriptor GetModeDescriptor(ModeTab tab) => modeDescriptors[tab];

    private IReadOnlyDictionary<ModeTab, ModeDescriptor> CreateModeDescriptors() =>
        new Dictionary<ModeTab, ModeDescriptor>
        {
            [ModeTab.Impulse] = new(
                ModeTab.Impulse,
                Mode.ImpulseResponse,
                SupportsCurveDrawing: true,
                MainContent: MainContentKind.Plot,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowOverlayCurves: true,
                CreatePlotModel: includeCurves => plotModelFactory.CreateImpulseResponse(includeCurves),
                OpenSettings: () => ToggleModeOptions(
                    ModeTab.Impulse,
                    () => new IROpt(),
                    opt => opt.Init(OpenSampleRate, viewSettings.ImpulseResponse),
                    opt => opt.SetOptions(viewSettings.ImpulseResponse),
                    // These rescale or re-origin an axis, so refit instead of restoring zoom.
                    viewResetKey: () => (
                        viewSettings.ImpulseResponse.AmplitudeScale,
                        viewSettings.ImpulseResponse.TimeUnit,
                        viewSettings.ImpulseResponse.TimeOrigin))),
            [ModeTab.Frequency] = new(
                ModeTab.Frequency,
                Mode.FrequencyResponse,
                SupportsCurveDrawing: true,
                MainContent: MainContentKind.Plot,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowOverlayCurves: true,
                CreatePlotModel: includeCurves => plotModelFactory.CreateFrequencyResponse(includeCurves),
                OpenSettings: () => ToggleModeOptions(
                    ModeTab.Frequency,
                    () => new FROptions(),
                    opt => opt.Init(
                        analyzerDocument,
                        expSweepMeasurement.SampleRate,
                        viewSettings.FrequencyResponse,
                        viewSettings.FrequencyResponseVisibility,
                        CalibrationEntries()),
                    opt => opt.SetOptions(viewSettings.FrequencyResponse, viewSettings.FrequencyResponseVisibility),
                    viewResetKey: () => viewSettings.FrequencyResponse.MagnitudeScale)),
            [ModeTab.Phase] = new(
                ModeTab.Phase,
                Mode.PhaseResponse,
                SupportsCurveDrawing: true,
                MainContent: MainContentKind.Plot,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowOverlayCurves: true,
                CreatePlotModel: includeCurves => plotModelFactory.CreatePhaseResponse(includeCurves),
                OpenSettings: () => ToggleModeOptions(
                    ModeTab.Phase,
                    () => new PROpt(),
                    opt => opt.Init(analyzerDocument, expSweepMeasurement.SampleRate, viewSettings.PhaseResponse, viewSettings.PhaseResponseVisibility, compareSelection.GetAnalysisSource),
                    opt => opt.SetOptions(viewSettings.PhaseResponse, viewSettings.PhaseResponseVisibility))),
            [ModeTab.GroupDelay] = new(
                ModeTab.GroupDelay,
                Mode.GroupDelay,
                SupportsCurveDrawing: true,
                MainContent: MainContentKind.Plot,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowOverlayCurves: true,
                CreatePlotModel: includeCurves => plotModelFactory.CreateGroupDelay(includeCurves),
                OpenSettings: () => ToggleModeOptions(
                    ModeTab.GroupDelay,
                    () => new GDOpt(),
                    opt => opt.Init(analyzerDocument, expSweepMeasurement.SampleRate, viewSettings.GroupDelay, viewSettings.GroupDelayVisibility, compareSelection.GetAnalysisSource),
                    opt => opt.SetOptions(viewSettings.GroupDelay, viewSettings.GroupDelayVisibility))),
            [ModeTab.Waterfall] = new(
                ModeTab.Waterfall,
                Mode.CumulativeSpectrumDecay,
                SupportsCurveDrawing: true,
                MainContent: MainContentKind.Plot,
                HasOverlayPanel: false,
                HasDockedSettings: true,
                ShowOverlayCurves: false,
                CreatePlotModel: includeCurves => plotModelFactory.CreateWaterfall(includeCurves),
                OpenSettings: () => ToggleModeOptions(
                    ModeTab.Waterfall,
                    () => new WaterfallOptions(),
                    opt => opt.Init(analyzerDocument, expSweepMeasurement.SampleRate, viewSettings.Waterfall),
                    opt => opt.SetOptions(viewSettings.Waterfall))),
            [ModeTab.Burst] = new(
                ModeTab.Burst,
                Mode.BurstDecay,
                SupportsCurveDrawing: true,
                MainContent: MainContentKind.Plot,
                HasOverlayPanel: false,
                HasDockedSettings: true,
                ShowOverlayCurves: false,
                CreatePlotModel: includeCurves => plotModelFactory.CreateBurstDecay(includeCurves),
                OpenSettings: () => ToggleModeOptions(
                    ModeTab.Burst,
                    () => new BDOpt(),
                    opt => opt.Init(analyzerDocument, expSweepMeasurement.SampleRate, viewSettings.BurstDecay),
                    opt => opt.SetOptions(viewSettings.BurstDecay))),
            [ModeTab.LiveSpectrum] = new(
                ModeTab.LiveSpectrum,
                Mode.LiveSpectrum,
                SupportsCurveDrawing: false,
                MainContent: MainContentKind.Plot,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowOverlayCurves: false,
                CreatePlotModel: _ => plotModelFactory.CreateLiveSpectrum(),
                OpenSettings: ToggleLiveSpectrumOptions),
            [ModeTab.Autocorrelation] = new(
                ModeTab.Autocorrelation,
                Mode.Autocorrelation,
                SupportsCurveDrawing: true,
                MainContent: MainContentKind.Plot,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowOverlayCurves: true,
                CreatePlotModel: includeCurves => plotModelFactory.CreateAutocorrelation(includeCurves),
                OpenSettings: () => ToggleModeOptions(
                    ModeTab.Autocorrelation,
                    () => new ACOpt(),
                    opt => opt.Init(viewSettings.ImpulseResponse),
                    opt => opt.SetOptions(viewSettings.ImpulseResponse))),
            [ModeTab.TimeAlignment] = new(
                ModeTab.TimeAlignment,
                Mode.TimeAlignment,
                SupportsCurveDrawing: false,
                MainContent: MainContentKind.TimeAlignment,
                HasOverlayPanel: false,
                HasDockedSettings: false,
                ShowOverlayCurves: false,
                CreatePlotModel: null,
                OpenSettings: null),
            [ModeTab.ToolsEqWizard] = new(
                ModeTab.ToolsEqWizard,
                Mode.EqWizard,
                SupportsCurveDrawing: false,
                MainContent: MainContentKind.EqWizard,
                HasOverlayPanel: false,
                HasDockedSettings: false,
                ShowOverlayCurves: false,
                CreatePlotModel: null,
                OpenSettings: null),
            [ModeTab.ToolsSignalGenerator] = new(
                ModeTab.ToolsSignalGenerator,
                Mode.SignalGenerator,
                SupportsCurveDrawing: false,
                MainContent: MainContentKind.SignalGenerator,
                HasOverlayPanel: false,
                HasDockedSettings: false,
                ShowOverlayCurves: false,
                CreatePlotModel: null,
                OpenSettings: null),
            [ModeTab.ToolsVirtualCrossover] = new(
                ModeTab.ToolsVirtualCrossover,
                Mode.VirtualCrossover,
                SupportsCurveDrawing: false,
                MainContent: MainContentKind.VirtualCrossover,
                HasOverlayPanel: false,
                HasDockedSettings: false,
                ShowOverlayCurves: false,
                CreatePlotModel: null,
                OpenSettings: null),
            [ModeTab.ToolsFirConstructor] = new(
                ModeTab.ToolsFirConstructor,
                Mode.FirConstructor,
                SupportsCurveDrawing: false,
                MainContent: MainContentKind.FirConstructor,
                HasOverlayPanel: false,
                HasDockedSettings: false,
                ShowOverlayCurves: false,
                CreatePlotModel: null,
                OpenSettings: null)
        };

    private enum MainContentKind
    {
        Plot,
        TimeAlignment,
        EqWizard,
        SignalGenerator,
        VirtualCrossover,
        FirConstructor
    }

    private sealed record ModeDescriptor(
        ModeTab Tab,
        Mode Mode,
        bool SupportsCurveDrawing,
        MainContentKind MainContent,
        bool HasOverlayPanel,
        bool HasDockedSettings,
        bool ShowOverlayCurves,
        Func<bool, PlotModel>? CreatePlotModel,
        Action? OpenSettings)
    {
        public bool HasPlotView => MainContent == MainContentKind.Plot;

        public bool ShowsTimeAlignmentPanel => MainContent == MainContentKind.TimeAlignment;

        public bool ShowsEqWizardPanel => MainContent == MainContentKind.EqWizard;

        public bool ShowsSignalGeneratorPanel => MainContent == MainContentKind.SignalGenerator;

        public bool ShowsVirtualCrossoverPanel => MainContent == MainContentKind.VirtualCrossover;

        public bool ShowsFirConstructorPanel => MainContent == MainContentKind.FirConstructor;

        /// <summary>Tools modes own their sources; the capture block is hidden so no button starts a sweep behind the tool.</summary>
        public bool HasCaptureControls => MainContent
            is not MainContentKind.EqWizard
            and not MainContentKind.SignalGenerator
            and not MainContentKind.VirtualCrossover
            and not MainContentKind.FirConstructor;

        /// <summary>False for Live Spectrum and Tools; an IR arriving elsewhere takes the app to Frequency Response first.</summary>
        public bool ShowsLoadedMeasurement =>
            HasCaptureControls && Tab != ModeTab.LiveSpectrum;
    }
}
