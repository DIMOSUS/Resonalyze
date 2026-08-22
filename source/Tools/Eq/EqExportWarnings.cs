using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// What an export to a given format would silently leave behind, worded once.
/// Two panels export the same banks now — the EQ Wizard and a Virtual DSP channel's
/// PEQ menu — and these warnings carry real instructions (which gain to type into
/// the device afterwards), so a second copy of them is a second chance to drift.
/// UI-free: each caller shows the text in its own dialog.
/// </summary>
internal static class EqExportWarnings
{
    /// <summary>
    /// The warning for shelving filters a format cannot state, or null when it
    /// carries them — or when the bank has none.
    /// </summary>
    public static string? ShelvingBandsDropped(
        EqWizardExportTarget target, EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(curve);

        int dropped = EqWizardImportExportCoordinator.CountShelvingBandsDroppedBy(
            target, curve);
        if (dropped == 0)
        {
            return null;
        }

        string filters = dropped == 1 ? "shelving filter" : $"{dropped} shelving filters";
        return $"{target.Name} cannot carry a shelving filter the way this EQ defines " +
            $"one, so the {filters} would be left out." +
            Environment.NewLine + Environment.NewLine +
            "The exported profile will hold the peaking filters and the preamp only, " +
            "and will not match the curve on screen. Export anyway?";
    }

    /// <summary>
    /// The warning for a preamp a format has no field for, or null when it has one —
    /// or when the preamp is 0.
    /// </summary>
    public static string? PreampDropped(
        EqWizardExportTarget target, EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(curve);

        double preampDb = EqWizardImportExportCoordinator.PreampDroppedBy(target, curve);
        if (preampDb == 0)
        {
            return null;
        }

        string gain = FormattableString.Invariant($"{preampDb:+0.0;-0.0} dB");
        // The preamp is signed: leaving out a cut makes the export louder than the
        // curve on screen, leaving out a boost makes it quieter. Say which.
        string direction = preampDb < 0 ? "louder" : "quieter";
        return $"{target.Name} has no place for the preamp, so the {gain} would be left " +
            $"out and the exported bands alone are that much {direction} than the tune " +
            "on screen." +
            Environment.NewLine + Environment.NewLine +
            $"Enter {gain} in the channel's own gain control on the device after " +
            "importing the bands. Export anyway?";
    }
}
