using OxyPlot;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
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
                    opt => opt.Init(expSweepMeasurement, impulseResponseOptions),
                    opt => opt.SetOptions(impulseResponseOptions),
                    viewResetKey: () => impulseResponseOptions.Logarithmic)),
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
                    opt => opt.Init(expSweepMeasurement, frequencyResponseOptions),
                    opt => opt.SetOptions(frequencyResponseOptions))),
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
                    opt => opt.Init(expSweepMeasurement, phaseResponseOptions),
                    opt => opt.SetOptions(phaseResponseOptions))),
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
                    opt => opt.Init(expSweepMeasurement, groupDelayOptions),
                    opt => opt.SetOptions(groupDelayOptions))),
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
                    opt => opt.Init(expSweepMeasurement, waterfallGenOptions),
                    opt => opt.SetOptions(waterfallGenOptions))),
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
                    opt => opt.Init(expSweepMeasurement, burstDecayGenOptions),
                    opt => opt.SetOptions(burstDecayGenOptions))),
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
                    opt => opt.Init(expSweepMeasurement, impulseResponseOptions),
                    opt => opt.SetOptions(impulseResponseOptions))),
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
            [ModeTab.ToolsIrComparer] = new(
                ModeTab.ToolsIrComparer,
                Mode.IrComparer,
                SupportsCurveDrawing: false,
                MainContent: MainContentKind.IrComparer,
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
        IrComparer
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

        public bool ShowsIrComparerPanel => MainContent == MainContentKind.IrComparer;
    }
}
