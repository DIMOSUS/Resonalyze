namespace Resonalyze;

/// <summary>Shared by EQ Wizard and Virtual DSP so both import, refuse and name curves identically.</summary>
internal static class TargetCurveImport
{
    private const string DialogTitle = "Import target curve";

    /// <summary>Null on cancel or refusal; the reason has already been shown.</summary>
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

        // A deviation or correction is a difference, not a goal; equalizing toward it chases the error.
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
