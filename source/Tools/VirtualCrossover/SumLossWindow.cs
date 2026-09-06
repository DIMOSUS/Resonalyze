namespace Resonalyze;

/// <summary>
/// The time window the Virtual DSP **Sum loss** — the drawn curve and the
/// per-junction read-out column — is measured through.
/// </summary>
/// <remarks>
/// The two windows answer different questions, and neither replaces the other.
/// <see cref="Full"/> reads the sum the ear hears in the cabin, reflections
/// included; it is what the Auto delay battery is judged by and what the tuning
/// sheet quotes (the AI package carries both reads). <see cref="Direct"/> — the
/// default the owner tunes with — reads the DIRECT sound, each channel through
/// the junction phase block's 8-cycle window at its own front. On a
/// seven-position grid in the reference car the direct read moved the
/// group-delay scatter between seats from 2.3 ms to 0.4–0.8 ms — the
/// reflections are what it cuts — but its loss figures scatter MORE between
/// seats than the full read's (the early reflections a car puts within 1–3 ms
/// of the direct sound sit inside any window that still resolves a sixth of an
/// octave, and the late tail the full read keeps fills the notch), and its dips
/// run deeper. The two families of numbers are not comparable with each other.
/// </remarks>
public enum SumLossWindow
{
    /// <summary>
    /// The steady-state window the magnitude curves read (~680 ms, clamped to
    /// 32768 samples at high rates), one shared anchor at the earliest front.
    /// </summary>
    Full,

    /// <summary>
    /// Each channel through the junction phase block's 8-cycle frequency-dependent
    /// window at its own front, the spectra rotated into one absolute frame and
    /// added — the loss of the direct sound. The default.
    /// </summary>
    Direct,

    /// <summary>
    /// No loss curve on the plot; the read-out column keeps the
    /// <see cref="Full"/> read.
    /// </summary>
    Off
}

public static class SumLossWindows
{
    /// <summary>The selector's entries, in the order they are offered.</summary>
    public static readonly IReadOnlyList<SumLossWindow> All =
        [SumLossWindow.Full, SumLossWindow.Direct, SumLossWindow.Off];

    /// <summary>The name the selector shows for a window.</summary>
    public static string DisplayName(SumLossWindow window) => window switch
    {
        SumLossWindow.Full => "Full",
        SumLossWindow.Direct => "FDW-8",
        SumLossWindow.Off => "Disable",
        _ => window.ToString()
    };
}
