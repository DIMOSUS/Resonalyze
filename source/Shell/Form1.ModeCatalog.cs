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
                HasPlotView: true,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowsTimeAlignmentPanel: false,
                ShowOverlayCurves: true,
                CreatePlotModel: includeCurves => plotModelFactory.CreateImpulseResponse(includeCurves),
                OpenSettings: () => ToggleModeOptions(
                    ModeTab.Impulse,
                    () => new IROpt(),
                    opt => opt.Init(expSweepMeasurement, impulseResponseOptions),
                    opt => opt.SetOptions(impulseResponseOptions))),
            [ModeTab.Frequency] = new(
                ModeTab.Frequency,
                Mode.FrequencyResponse,
                SupportsCurveDrawing: true,
                HasPlotView: true,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowsTimeAlignmentPanel: false,
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
                HasPlotView: true,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowsTimeAlignmentPanel: false,
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
                HasPlotView: true,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowsTimeAlignmentPanel: false,
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
                HasPlotView: true,
                HasOverlayPanel: false,
                HasDockedSettings: true,
                ShowsTimeAlignmentPanel: false,
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
                HasPlotView: true,
                HasOverlayPanel: false,
                HasDockedSettings: true,
                ShowsTimeAlignmentPanel: false,
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
                HasPlotView: true,
                HasOverlayPanel: true,
                HasDockedSettings: true,
                ShowsTimeAlignmentPanel: false,
                ShowOverlayCurves: false,
                CreatePlotModel: _ => plotModelFactory.CreateLiveSpectrum(),
                OpenSettings: () => ToggleModeOptions(
                    ModeTab.LiveSpectrum,
                    () => new LiveSpectrumOpt(),
                    opt => opt.Init(liveSpectrumOptions),
                    opt => opt.SetOptions(liveSpectrumOptions))),
            [ModeTab.Autocorrelation] = new(
                ModeTab.Autocorrelation,
                Mode.Autocorrelation,
                SupportsCurveDrawing: true,
                HasPlotView: true,
                HasOverlayPanel: true,
                HasDockedSettings: false,
                ShowsTimeAlignmentPanel: false,
                ShowOverlayCurves: true,
                CreatePlotModel: includeCurves => plotModelFactory.CreateAutocorrelation(includeCurves),
                OpenSettings: null),
            [ModeTab.TimeAlignment] = new(
                ModeTab.TimeAlignment,
                Mode.TimeAlignment,
                SupportsCurveDrawing: false,
                HasPlotView: false,
                HasOverlayPanel: false,
                HasDockedSettings: false,
                ShowsTimeAlignmentPanel: true,
                ShowOverlayCurves: false,
                CreatePlotModel: null,
                OpenSettings: null)
        };

    private sealed record ModeDescriptor(
        ModeTab Tab,
        Mode Mode,
        bool SupportsCurveDrawing,
        bool HasPlotView,
        bool HasOverlayPanel,
        bool HasDockedSettings,
        bool ShowsTimeAlignmentPanel,
        bool ShowOverlayCurves,
        Func<bool, PlotModel>? CreatePlotModel,
        Action? OpenSettings);
}
