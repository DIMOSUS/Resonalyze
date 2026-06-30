namespace Resonalyze;

// Summary of how well an EQ fits its target, shown in the EQ Wizard results panel.
internal sealed record EqTuneStats(
    double RmsErrorDb,
    double MaxErrorDb,
    int FiltersUsed,
    double PeakBoostDb,
    double PeakCutDb,
    double HeadroomDb);

// Replaces the overlay panel in EQ Wizard mode with a compact, colour-coded
// read-out of the current tuning result.
public sealed partial class EqResultsPanel : UserControl
{
    private static readonly Color NeutralColor = Color.FromArgb(225, 228, 235);
    private static readonly Color GoodColor = Color.FromArgb(120, 200, 130);
    private static readonly Color WarnColor = Color.FromArgb(235, 196, 90);
    private static readonly Color BadColor = Color.FromArgb(232, 86, 76);
    private static readonly Color InfoColor = Color.FromArgb(0, 209, 255);

    public EqResultsPanel()
    {
        InitializeComponent();
        Clear();
    }

    public void Clear()
    {
        foreach (Label value in new[]
                 {
                     rmsValue, maxValue, filtersValue, boostValue, cutValue, headroomValue
                 })
        {
            value.Text = "-";
            value.ForeColor = NeutralColor;
        }
    }

    internal void SetResults(EqTuneStats? stats)
    {
        if (stats == null)
        {
            Clear();
            return;
        }

        rmsValue.Text = $"{stats.RmsErrorDb:0.0} dB";
        rmsValue.ForeColor = QualityColor(stats.RmsErrorDb, 3.0, 6.0);

        maxValue.Text = $"{stats.MaxErrorDb:0.0} dB";
        maxValue.ForeColor = QualityColor(stats.MaxErrorDb, 6.0, 12.0);

        filtersValue.Text = stats.FiltersUsed.ToString();
        filtersValue.ForeColor = NeutralColor;

        boostValue.Text = $"{stats.PeakBoostDb:+0.0;-0.0;0.0} dB";
        boostValue.ForeColor = stats.PeakBoostDb > 0.05 ? BadColor : GoodColor;

        cutValue.Text = $"{stats.PeakCutDb:+0.0;-0.0;0.0} dB";
        cutValue.ForeColor = InfoColor;

        headroomValue.Text = $"{stats.HeadroomDb:+0.0;-0.0;0.0} dB";
        headroomValue.ForeColor = stats.HeadroomDb < -0.05 ? BadColor : GoodColor;
    }

    // Lower error is better: green when comfortably small, red when large.
    private static Color QualityColor(double errorDb, double goodBelow, double badAbove)
    {
        if (errorDb <= goodBelow)
        {
            return GoodColor;
        }

        return errorDb >= badAbove ? BadColor : WarnColor;
    }
}
