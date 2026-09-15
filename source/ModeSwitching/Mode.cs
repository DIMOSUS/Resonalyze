namespace Resonalyze;

/// <summary>Member names are PERSISTED (overlay slot JSON and overlay directory names): renaming orphans saved overlays.</summary>
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
    VirtualCrossover,
    FirConstructor
}
