using OxyPlot.WindowsForms;

namespace Resonalyze;

public partial class TimeAlignmentPanel : UserControl
{
    public TimeAlignmentPanel()
    {
        InitializeComponent();
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
