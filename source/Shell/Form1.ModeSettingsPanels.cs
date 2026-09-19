using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
    /// <summary>The open measurement's rate, or the one the next run is configured for.</summary>
    private int OpenSampleRate => analyzerDocument.Result?.SampleRate ?? expSweepMeasurement.SampleRate;

    private ModeDescriptor GetActiveModeDescriptor() =>
        ModeCatalog.For(modeController.ActiveTab);

    /// <summary>Opens or closes the tab's docked settings panel; a tab without one does nothing.</summary>
    private void ToggleModeSettingsPanel(ModeTab tab)
    {
        switch (tab)
        {
            case ModeTab.Impulse:
                ToggleModeOptions(
                    tab,
                    () => new IROpt(),
                    opt => opt.Init(OpenSampleRate, viewSettings.ImpulseResponse),
                    opt => opt.SetOptions(viewSettings.ImpulseResponse),
                    // These rescale or re-origin an axis, so refit instead of restoring zoom.
                    viewResetKey: () => (
                        viewSettings.ImpulseResponse.AmplitudeScale,
                        viewSettings.ImpulseResponse.TimeUnit,
                        viewSettings.ImpulseResponse.TimeOrigin));
                break;
            case ModeTab.Frequency:
                ToggleModeOptions(
                    tab,
                    () => new FROptions(),
                    opt => opt.Init(
                        analyzerDocument,
                        expSweepMeasurement.SampleRate,
                        viewSettings.FrequencyResponse,
                        viewSettings.FrequencyResponseVisibility,
                        CalibrationEntries()),
                    opt => opt.SetOptions(viewSettings.FrequencyResponse, viewSettings.FrequencyResponseVisibility),
                    viewResetKey: () => viewSettings.FrequencyResponse.MagnitudeScale);
                break;
            case ModeTab.Phase:
                ToggleModeOptions(
                    tab,
                    () => new PROpt(),
                    opt => opt.Init(
                        analyzerDocument,
                        expSweepMeasurement.SampleRate,
                        viewSettings.PhaseResponse,
                        viewSettings.PhaseResponseVisibility,
                        compareSelection.GetAnalysisSource),
                    opt => opt.SetOptions(viewSettings.PhaseResponse, viewSettings.PhaseResponseVisibility));
                break;
            case ModeTab.GroupDelay:
                ToggleModeOptions(
                    tab,
                    () => new GDOpt(),
                    opt => opt.Init(
                        analyzerDocument,
                        expSweepMeasurement.SampleRate,
                        viewSettings.GroupDelay,
                        viewSettings.GroupDelayVisibility,
                        compareSelection.GetAnalysisSource),
                    opt => opt.SetOptions(viewSettings.GroupDelay, viewSettings.GroupDelayVisibility));
                break;
            case ModeTab.Waterfall:
                ToggleModeOptions(
                    tab,
                    () => new WaterfallOptions(),
                    opt => opt.Init(analyzerDocument, expSweepMeasurement.SampleRate, viewSettings.Waterfall),
                    opt => opt.SetOptions(viewSettings.Waterfall));
                break;
            case ModeTab.Burst:
                ToggleModeOptions(
                    tab,
                    () => new BDOpt(),
                    opt => opt.Init(analyzerDocument, expSweepMeasurement.SampleRate, viewSettings.BurstDecay),
                    opt => opt.SetOptions(viewSettings.BurstDecay));
                break;
            case ModeTab.LiveSpectrum:
                ToggleLiveSpectrumOptions();
                break;
            case ModeTab.Autocorrelation:
                ToggleModeOptions(
                    tab,
                    () => new ACOpt(),
                    opt => opt.Init(viewSettings.ImpulseResponse),
                    opt => opt.SetOptions(viewSettings.ImpulseResponse));
                break;
        }
    }
}
