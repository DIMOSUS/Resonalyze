namespace Resonalyze.Options;

/// <summary>How a settings field reads: normal, in the warning colour, or muted because the mode ignores it.</summary>
internal enum LiveSettingTone
{
    Normal,
    Warning,
    Muted
}

/// <summary>How the Live Spectrum settings look for the mode. Muted means ignored; what MMM pins is forced, not muted, so
/// it keeps the normal colour and only stops responding.</summary>
internal static class LiveSpectrumSettingsLook
{
    /// <summary>RTA and MMM draw no transfer or coherence curve and force the RTA on.</summary>
    public static bool CurvesMuted(LiveSpectrumSettingsSession session) => session.IsReferenceFree;

    /// <summary>MMM is band-power dB SPL by definition; Transfer has no scalar SPL; RTA warns when a live curve would be
    /// hidden for want of a calibration.</summary>
    public static LiveSettingTone Spl(LiveSpectrumSettingsSession session) =>
        session.IsMmm
            ? LiveSettingTone.Normal
            : !session.IsRta
                ? LiveSettingTone.Muted
                : session.SplViewOnlyConflict
                    ? LiveSettingTone.Warning
                    : LiveSettingTone.Normal;

    public static LiveSettingTone Tilt(LiveSpectrumSettingsSession session) =>
        session.IsMmm || session.TiltApplicable ? LiveSettingTone.Normal : LiveSettingTone.Muted;

    /// <summary>Amber only for an active override: Transfer selected without a loopback.</summary>
    public static LiveSettingTone Transfer(LiveSpectrumSettingsSession session) =>
        session.Mode == LiveAnalysisMode.TransferFunction && !session.HasTransferReference
            ? LiveSettingTone.Warning
            : LiveSettingTone.Normal;
}
