namespace Resonalyze;

/// <summary>
/// The analysis modes the shell switches between. Lives here rather than inside
/// <c>Form1</c> because it is not a shell detail: overlays, plotting, curve tags
/// and the settings file are all typed on it.
///
/// These names are PERSISTED. <see cref="OverlayFile.Mode"/> serializes the
/// member name into the slot JSON, and <see cref="OverlayFile.GetPath"/> uses
/// <c>mode.ToString()</c> as the on-disk directory name under
/// <c>ApplicationDataPaths.OverlaysDirectory</c>. Renaming a member orphans
/// every overlay a user already saved under the old name; adding members is
/// safe, reordering is safe only because the numeric values are not stored.
/// </summary>
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
