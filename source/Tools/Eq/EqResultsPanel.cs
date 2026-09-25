namespace Resonalyze;

/// <param name="RmsErrorDb">Null, like <paramref name="MaxErrorDb"/>, when no point of the curve lies in the window.</param>
internal sealed record EqTuneStats(
    double? RmsErrorDb,
    double? MaxErrorDb,
    int FiltersUsed,
    double PeakBoostDb,
    double PeakCutDb,
    double HeadroomDb);

public sealed partial class EqResultsPanel : UserControl
{
    private static readonly Color NeutralColor = UiPalette.TextValue;
    private static readonly Color GoodColor = UiPalette.Success;
    private static readonly Color WarnColor = UiPalette.Warning;
    private static readonly Color BadColor = UiPalette.Error;
    private static readonly Color InfoColor = UiPalette.AccentMark;

    private readonly WrappingToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 8000,
        ShowAlways = true
    };

    public EqResultsPanel()
    {
        InitializeComponent();
        InitializeToolTips();
        Clear();
    }

    private void InitializeToolTips()
    {
        SetTip(rmsCaption, rmsValue,
            "Root-mean-square deviation of Source + EQ from the target, within the " +
            "From/To window. Lower is better.");
        SetTip(maxCaption, maxValue,
            "Largest absolute deviation of Source + EQ from the target, within the window.");
        SetTip(filtersCaption, filtersValue, "Number of active bands (non-zero gain).");
        SetTip(boostCaption, boostValue,
            "Largest positive gain of the combined EQ. A net boost risks clipping.");
        SetTip(cutCaption, cutValue, "Largest attenuation of the combined EQ.");
        SetTip(headroomCaption, headroomValue,
            "Margin to 0 dB (−peak boost). Negative means the EQ nets a boost that " +
            "could clip.");
    }

    private void SetTip(Label caption, Label value, string text)
    {
        toolTip.SetToolTip(caption, text);
        toolTip.SetToolTip(value, text);
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

    /// <summary>A standing condition of the setup rather than of one tune; null hides it.</summary>
    internal void SetWarning(string? warning)
    {
        warningLabel.Text = warning ?? string.Empty;
        warningLabel.Visible = warning != null;
    }

    internal void SetResults(EqTuneStats? stats)
    {
        if (stats == null)
        {
            Clear();
            return;
        }

        ShowError(rmsValue, stats.RmsErrorDb, 3.0, 6.0);
        ShowError(maxValue, stats.MaxErrorDb, 6.0, 12.0);

        filtersValue.Text = stats.FiltersUsed.ToString();
        filtersValue.ForeColor = NeutralColor;

        boostValue.Text = $"{stats.PeakBoostDb:+0.0;-0.0;0.0} dB";
        boostValue.ForeColor = stats.PeakBoostDb > 0.05 ? BadColor : GoodColor;

        cutValue.Text = $"{stats.PeakCutDb:+0.0;-0.0;0.0} dB";
        cutValue.ForeColor = InfoColor;

        headroomValue.Text = $"{stats.HeadroomDb:+0.0;-0.0;0.0} dB";
        headroomValue.ForeColor = stats.HeadroomDb < -0.05 ? BadColor : GoodColor;
    }

    private static void ShowError(Label value, double? errorDb, double goodBelow, double badAbove)
    {
        value.Text = errorDb is { } db ? $"{db:0.0} dB" : "-";
        value.ForeColor = errorDb is { } error ? QualityColor(error, goodBelow, badAbove) : NeutralColor;
    }

    private static Color QualityColor(double errorDb, double goodBelow, double badAbove)
    {
        if (errorDb <= goodBelow)
        {
            return GoodColor;
        }

        return errorDb >= badAbove ? BadColor : WarnColor;
    }
}
