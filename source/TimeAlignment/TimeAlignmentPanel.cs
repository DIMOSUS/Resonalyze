using OxyPlot.WindowsForms;

namespace Resonalyze;

public partial class TimeAlignmentPanel : UserControl
{
    public TimeAlignmentPanel()
    {
        InitializeComponent();
        // Below its designed size the panel scrolls on native scrollbars; theme
        // them dark so they match the app instead of showing the default light
        // bar, the way the Virtual DSP panel already does.
        Ui.DarkScrollBars.Apply(this);
    }

    internal Label SourceSummaryLabel => sourceSummaryLabel;

    internal Label CompareLabel => compareLabel;

    internal RadioButton BandModeFullRadio => bandModeFullRadio;

    internal RadioButton BandModeAutoRadio => bandModeAutoRadio;

    internal RadioButton BandModeManualRadio => bandModeManualRadio;

    internal Label AutoBandLabel => autoBandLabel;

    internal DarkNumericUpDown BandpassCenterNumeric => bandpassCenterNumeric;

    internal DarkNumericUpDown BandpassPassOctavesNumeric => bandpassPassOctavesNumeric;

    internal DarkNumericUpDown BandpassFadeOctavesNumeric => bandpassFadeOctavesNumeric;

    internal PlotView BandpassPlotView => bandpassPlotView;

    internal PlotView EnvelopePlotView => envelopePlotView;

    internal StatusRichTextBox StatusTextBox => statusTextBox;
}
