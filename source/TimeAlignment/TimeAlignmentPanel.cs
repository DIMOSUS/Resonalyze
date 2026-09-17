using OxyPlot.WindowsForms;

namespace Resonalyze;

public partial class TimeAlignmentPanel : UserControl
{
    public TimeAlignmentPanel()
    {
        InitializeComponent();
        Ui.ThemedScrollBars.Apply(this);
    }

    internal Label SourceSummaryLabel => sourceSummaryLabel;

    internal Label CompareLabel => compareLabel;

    internal RadioButton BandModeFullRadio => bandModeFullRadio;

    internal RadioButton BandModeAutoRadio => bandModeAutoRadio;

    internal RadioButton BandModeManualRadio => bandModeManualRadio;

    internal Label AutoBandLabel => autoBandLabel;

    internal ThemedNumericUpDown BandpassCenterNumeric => bandpassCenterNumeric;

    internal ThemedNumericUpDown BandpassPassOctavesNumeric => bandpassPassOctavesNumeric;

    internal ThemedNumericUpDown BandpassFadeOctavesNumeric => bandpassFadeOctavesNumeric;

    internal PlotView BandpassPlotView => bandpassPlotView;

    internal PlotView EnvelopePlotView => envelopePlotView;

    internal StatusRichTextBox StatusTextBox => statusTextBox;
}
