namespace Resonalyze;

/// <summary>What each tab shows and offers; the main window lays itself out and draws from this table.</summary>
internal static class ModeCatalog
{
    private static readonly IReadOnlyDictionary<ModeTab, ModeDescriptor> Descriptors =
        new Dictionary<ModeTab, ModeDescriptor>
        {
            [ModeTab.Impulse] = Plot(ModeTab.Impulse, Mode.ImpulseResponse),
            [ModeTab.Frequency] = Plot(ModeTab.Frequency, Mode.FrequencyResponse),
            [ModeTab.Phase] = Plot(ModeTab.Phase, Mode.PhaseResponse),
            [ModeTab.GroupDelay] = Plot(ModeTab.GroupDelay, Mode.GroupDelay),
            [ModeTab.Waterfall] = Plot(ModeTab.Waterfall, Mode.CumulativeSpectrumDecay) with
            {
                HasOverlayPanel = false,
                ShowOverlayCurves = false
            },
            [ModeTab.Burst] = Plot(ModeTab.Burst, Mode.BurstDecay) with
            {
                HasOverlayPanel = false,
                ShowOverlayCurves = false
            },
            [ModeTab.LiveSpectrum] = Plot(ModeTab.LiveSpectrum, Mode.LiveSpectrum) with
            {
                SupportsCurveDrawing = false,
                ShowOverlayCurves = false
            },
            [ModeTab.Autocorrelation] = Plot(ModeTab.Autocorrelation, Mode.Autocorrelation),
            [ModeTab.TimeAlignment] = Panel(ModeTab.TimeAlignment, Mode.TimeAlignment, MainContentKind.TimeAlignment),
            [ModeTab.ToolsEqWizard] = Panel(ModeTab.ToolsEqWizard, Mode.EqWizard, MainContentKind.EqWizard),
            [ModeTab.ToolsSignalGenerator] =
                Panel(ModeTab.ToolsSignalGenerator, Mode.SignalGenerator, MainContentKind.SignalGenerator),
            [ModeTab.ToolsVirtualCrossover] =
                Panel(ModeTab.ToolsVirtualCrossover, Mode.VirtualCrossover, MainContentKind.VirtualCrossover),
            [ModeTab.ToolsFirConstructor] =
                Panel(ModeTab.ToolsFirConstructor, Mode.FirConstructor, MainContentKind.FirConstructor)
        };

    public static ModeDescriptor For(ModeTab tab) => Descriptors[tab];

    private static ModeDescriptor Plot(ModeTab tab, Mode mode) =>
        new(
            tab,
            mode,
            SupportsCurveDrawing: true,
            MainContent: MainContentKind.Plot,
            HasOverlayPanel: true,
            HasDockedSettings: true,
            ShowOverlayCurves: true);

    private static ModeDescriptor Panel(ModeTab tab, Mode mode, MainContentKind content) =>
        new(
            tab,
            mode,
            SupportsCurveDrawing: false,
            MainContent: content,
            HasOverlayPanel: false,
            HasDockedSettings: false,
            ShowOverlayCurves: false);
}

internal enum MainContentKind
{
    Plot,
    TimeAlignment,
    EqWizard,
    SignalGenerator,
    VirtualCrossover,
    FirConstructor
}

/// <param name="SupportsCurveDrawing">Draws the open measurement's curves; false where the mode has none to draw.</param>
/// <param name="ShowOverlayCurves">Draws the overlay slots over the measurement.</param>
internal sealed record ModeDescriptor(
    ModeTab Tab,
    Mode Mode,
    bool SupportsCurveDrawing,
    MainContentKind MainContent,
    bool HasOverlayPanel,
    bool HasDockedSettings,
    bool ShowOverlayCurves)
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
