namespace Resonalyze;

internal static class OverlayModes
{
    public static bool Supports(Mode mode) =>
        mode is
            Mode.ImpulseResponse or
            Mode.FrequencyResponse or
            Mode.PhaseResponse or
            Mode.GroupDelay or
            Mode.LiveSpectrum or
            Mode.EqWizard or
            Mode.Autocorrelation;

    // FR, Live Spectrum and EQ Wizard share axes, so they share one set of overlay slots and storage.
    public static Mode SlotModeFor(Mode mode) =>
        mode is Mode.LiveSpectrum or Mode.EqWizard ? Mode.FrequencyResponse : mode;

    /// <summary>The colour a slot shows before it holds a capture; a captured slot carries its own.</summary>
    public static Color SlotDefaultColor(int slot) =>
        Ui.UiPalette.OverlaySlotDefaults[(slot - 1) % Ui.UiPalette.OverlaySlotDefaults.Count];

    public static string? TrackerFormat(Mode mode) => mode switch
    {
        Mode.FrequencyResponse or Mode.LiveSpectrum => "{0}\n{2:0.0} Hz\n{4:0.00} dB",
        Mode.PhaseResponse => "{0}\n{2:0.0} Hz\n{4:0.0}°",
        Mode.GroupDelay => "{0}\n{2:0.0} Hz\n{4:0.000} ms",
        Mode.ImpulseResponse => "{0}\n{2:0} sample\n{4:0.00000000}",
        Mode.Autocorrelation => "{0}\n{2:0.000} ms\n{4:0.000}",
        _ => null
    };
}
