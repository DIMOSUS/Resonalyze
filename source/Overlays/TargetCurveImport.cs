namespace Resonalyze;

/// <summary>
/// Loading a target shape from a file — a house curve — for the two places that
/// share one target: the EQ Wizard and Virtual DSP. Both ask through here so a
/// curve imported from either button is the same curve, refused for the same
/// reasons and named the same way.
/// </summary>
internal static class TargetCurveImport
{
    private const string DialogTitle = "Import target curve";

    /// <summary>
    /// Asks for a file and returns the target shape it holds, or <c>null</c> when
    /// the user cancelled or the file cannot be a target — in which case the
    /// reason has already been shown.
    /// </summary>
    public static ImportedTargetCurve? Prompt(IWin32Window? owner)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Target curve (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*",
            Title = DialogTitle
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        OverlayTextCurve file;
        try
        {
            file = OverlayTextFile.ImportCurve(dialog.FileName);
        }
        catch (Exception exception)
        {
            Refuse(owner, $"The curve could not be read.\r\n{exception.Message}");
            return null;
        }

        // A deviation or an EQ correction is a difference between two curves, not a
        // goal: equalizing toward one would chase the error rather than remove it.
        // Every other role is a legitimate target — a curve exported from a target
        // slot, and equally a measured response somebody wants to voice toward.
        if (file.Metadata.Role is OverlayCurveRole.Deviation or OverlayCurveRole.EqCorrection)
        {
            string role = file.Metadata.Role == OverlayCurveRole.EqCorrection
                ? "EQ correction"
                : "deviation";
            Refuse(
                owner,
                $"This file holds a {role} curve, which is a difference from a " +
                "target rather than a target. Import the curve you want the " +
                "response to look like instead.");
            return null;
        }

        ImportedTargetCurve? curve = ImportedTargetCurve.FromPoints(
            Path.GetFileName(dialog.FileName),
            file.Points);
        if (curve == null)
        {
            Refuse(
                owner,
                "No usable target curve was found in this file. A target needs at " +
                "least two \"frequency level\" pairs — for example \"63 4.5\", one " +
                "per line, with frequencies in Hz and levels in dB.");
            return null;
        }

        return curve;
    }

    private static void Refuse(IWin32Window? owner, string message) =>
        MessageBox.Show(
            owner,
            message,
            DialogTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
}
