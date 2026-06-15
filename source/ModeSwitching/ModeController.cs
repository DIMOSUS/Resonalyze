namespace Resonalyze;

internal sealed class ModeController
{
    private readonly Func<Mode, Task> changeModeAsync;
    private readonly Action<ModeTab> activeTabChanged;
    private readonly Action hideOverlays;
    private readonly Action<bool> drawSelectedMode;
    private readonly Func<bool> canDrawCurrentMeasurement;
    private readonly Action updateDrawButton;

    public ModeController(
        Func<Mode, Task> changeModeAsync,
        Action<ModeTab> activeTabChanged,
        Action hideOverlays,
        Action<bool> drawSelectedMode,
        Func<bool> canDrawCurrentMeasurement,
        Action updateDrawButton)
    {
        this.changeModeAsync = changeModeAsync;
        this.activeTabChanged = activeTabChanged;
        this.hideOverlays = hideOverlays;
        this.drawSelectedMode = drawSelectedMode;
        this.canDrawCurrentMeasurement = canDrawCurrentMeasurement;
        this.updateDrawButton = updateDrawButton;
    }

    public ModeTab ActiveTab { get; private set; } = ModeTab.Frequency;

    public async Task SelectAsync(ModeTab tab)
    {
        await changeModeAsync(GetMode(tab));
        ActiveTab = tab;
        activeTabChanged(tab);
        hideOverlays();

        bool includeCurves =
            tab != ModeTab.LiveSpectrum &&
            canDrawCurrentMeasurement();
        drawSelectedMode(includeCurves);
        updateDrawButton();
    }

    public static Mode GetMode(ModeTab tab) =>
        tab switch
        {
            ModeTab.Impulse => Mode.ImpulseResponse,
            ModeTab.Frequency => Mode.FrequencyResponse,
            ModeTab.Phase => Mode.PhaseResponse,
            ModeTab.GroupDelay => Mode.GroupDelay,
            ModeTab.Waterfall => Mode.CumulativeSpectrumDecay,
            ModeTab.Burst => Mode.CumulativeSpectrumDecay,
            ModeTab.LiveSpectrum => Mode.LiveSpectrum,
            ModeTab.Autocorrelation => Mode.Autocorrelation,
            _ => Mode.None
        };
}
