namespace Resonalyze.Options;

/// <summary>The Live Spectrum settings tooltips that change with the analyzer: dB SPL and Transfer say when
/// the choice cannot apply and why.</summary>
internal static class LiveSpectrumSettingsToolTips
{
    public static string Transfer(LiveSpectrumSettingsSession session)
    {
        const string Base =
            "Dual-channel transfer function: the microphone divided by the " +
            "loopback reference, with coherence.";
        if (session.HasTransferReference)
        {
            return Base;
        }

        return Base + "\r\n" +
            "No loopback reference channel is configured (Measurement Options), " +
            "so the analyzer runs as a reference-free RTA regardless of this " +
            "choice.";
    }

    public static string Spl(LiveSpectrumSettingsSession session)
    {
        const string Base =
            "Shows the RTA in absolute dB SPL (microphone plus the SPL " +
            "calibration offset). RTA mode only: the transfer function is a " +
            "dimensionless ratio with no scalar SPL under noise excitation.";
        if (session.SplAvailable)
        {
            return Base;
        }

        if (session.SplViewOnlyConflict)
        {
            return Base + "\r\n" +
                "View-only right now: no SPL calibration is configured for the " +
                "live input (or it was captured on a different input), so the " +
                "live curve is hidden — only overlays captured in dB SPL are " +
                "shown. Configure it in Measurement Options — Calibration; " +
                "starting the analyzer in this state switches the display back " +
                "to relative.";
        }

        return Base + "\r\n" +
            "No SPL calibration is configured for the live input (Measurement " +
            "Options — Calibration). Starting the analyzer without one switches " +
            "the display back to relative; overlays captured in dB SPL are " +
            "shown either way.";
    }
}
