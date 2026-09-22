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

    /// <summary>
    /// The Target Level to switch to when the curve's peak, hung at <paramref name="levelDb"/>, lands beyond what the plot can
    /// pan to and the user agrees to move it, already fitted to <paramref name="levelRange"/>. Null keeps the level. The curve
    /// itself is never shifted.
    /// </summary>
    public static decimal? OfferLevel(
        IWin32Window? owner,
        ImportedTargetCurve curve,
        double levelDb,
        NumericFieldRange levelRange,
        double plotMinDb,
        double plotMaxDb)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (LevelSeatingPeak(curve.PeakDb, levelDb, plotMinDb, plotMaxDb) is not { } ideal)
        {
            return null;
        }

        // Offered as the box will hold it, so the peak lands where the question says.
        decimal seated = levelRange.Clamp(ideal);

        double drawnDb = curve.PeakDb + levelDb;
        double seatedDb = curve.PeakDb + (double)seated;
        DialogResult answer = MessageBox.Show(
            owner,
            $"“{curve.Name}” peaks at {curve.PeakDb:+0.0;-0.0;0.0} dB in the file, which puts it at " +
            $"{drawnDb:0} dB on this plot — outside the {plotMinDb:0} … {plotMaxDb:0} dB it shows." +
            Environment.NewLine + Environment.NewLine +
            $"Set the Target Level to {seated:+0.##;-0.##;0} dB so the peak sits at {seatedDb:0.0} dB? " +
            "The curve keeps the file's levels either way.",
            DialogTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        return answer == DialogResult.Yes ? seated : null;
    }

    /// <summary>Null when the peak drawn at this level is on the plot; else the level seating it at 0 dB, or mid-plot where
    /// 0 dB is not on it (a dB SPL axis).</summary>
    internal static double? LevelSeatingPeak(
        double peakDb,
        double levelDb,
        double plotMinDb,
        double plotMaxDb)
    {
        double drawnDb = peakDb + levelDb;
        if (drawnDb >= plotMinDb && drawnDb <= plotMaxDb)
        {
            return null;
        }

        double seatDb = plotMinDb <= 0 && 0 <= plotMaxDb
            ? 0
            : (plotMinDb + plotMaxDb) / 2;
        return seatDb - peakDb;
    }

    private static void Refuse(IWin32Window? owner, string message) =>
        MessageBox.Show(
            owner,
            message,
            DialogTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
}
