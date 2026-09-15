using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Export-loss warnings worded once for the EQ Wizard and the Virtual DSP PEQ menu (they carry device instructions).</summary>
internal static class EqExportWarnings
{
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
            "The exported profile will be missing those filters and will not match " +
            "the curve on screen. Export anyway?";
    }

    public static string? AllPassBandsDropped(
        EqWizardExportTarget target, EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(curve);

        int dropped = EqWizardImportExportCoordinator.CountAllPassBandsDroppedBy(
            target, curve);
        if (dropped == 0)
        {
            return null;
        }

        // "cannot carry an all-pass" would read as a lie next to exported AP2 rows.
        bool onlyFirstOrder =
            target.SupportsAllPass(PeqBandType.AllPassSecondOrder) &&
            !target.SupportsAllPass(PeqBandType.AllPassFirstOrder);
        string kind = onlyFirstOrder ? "first-order all-pass" : "all-pass";
        string filters = dropped == 1
            ? $"the {kind} filter"
            : $"the {dropped} {kind} filters";
        return $"{target.Name} has no {kind} filter, so {filters} would be left out." +
            Environment.NewLine + Environment.NewLine +
            "The exported profile will not turn phase the way the tune on screen " +
            "does. Export anyway?";
    }

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
        string direction = preampDb < 0 ? "louder" : "quieter";
        return $"{target.Name} has no place for the preamp, so the {gain} would be left " +
            $"out and the exported bands alone are that much {direction} than the tune " +
            "on screen." +
            Environment.NewLine + Environment.NewLine +
            $"Enter {gain} in the channel's own gain control on the device after " +
            "importing the bands. Export anyway?";
    }
}
