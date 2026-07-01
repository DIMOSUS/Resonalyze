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

    internal CheckBox BandpassCheckBox => bandpassCheckBox;

    internal DarkNumericUpDown BandpassCenterNumeric => bandpassCenterNumeric;

    internal DarkNumericUpDown BandpassPassOctavesNumeric => bandpassPassOctavesNumeric;

    internal DarkNumericUpDown BandpassFadeOctavesNumeric => bandpassFadeOctavesNumeric;

    internal PlotView BandpassPlotView => bandpassPlotView;

    internal PlotView EnvelopePlotView => envelopePlotView;

    internal StatusRichTextBox StatusTextBox => statusTextBox;
}
