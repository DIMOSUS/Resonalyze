namespace Resonalyze.Options;

/// <summary>What the Frequency Response panel says about dB SPL for the open measurement. The choice is never locked:
/// the SPL axis still shows overlays captured in SPL.</summary>
internal static class FrequencyResponseSplChoice
{
    private const string Base = "Absolute dB SPL from the microphone SPL calibration.";

    /// <summary>Amber: a measurement on screen that carries no anchor cannot be drawn in SPL.</summary>
    public static bool ViewOnlyConflict(ModeSettingsMeasurement measurement) =>
        !measurement.SplAvailable && measurement.Result != null;

    public static string ToolTip(ModeSettingsMeasurement measurement)
    {
        if (measurement.SplAvailable)
        {
            return Base;
        }

        if (ViewOnlyConflict(measurement))
        {
            return Base + "\r\n" +
                "View-only: the measurement on screen carries no SPL anchor (it " +
                "is stamped at run time from the configured calibration plus the " +
                "run's loopback level), so its curves cannot be shown in dB SPL — " +
                "only overlays captured in dB SPL are. A new measurement with an " +
                "SPL calibration configured comes up in dB SPL; starting one " +
                "without returns the display to dBr/dBc.";
        }

        return Base + "\r\n" +
            "No measurement yet. With an SPL calibration configured in " +
            "Record Settings, the first run comes up in dB SPL; without one, " +
            "starting a run switches the display back to dBr/dBc. Overlays " +
            "captured in dB SPL are shown either way.";
    }
}
