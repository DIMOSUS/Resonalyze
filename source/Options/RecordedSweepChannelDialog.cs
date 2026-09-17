using Resonalyze.Ui;

namespace Resonalyze.Options;

/// <summary>Shown only when ambiguous (<see cref="RecordedSweepChannels.IsAmbiguous"/>): a DAW track holding the played
/// sweep matches best and measures flat, and only the person who recorded it knows the microphone track.</summary>
internal sealed partial class RecordedSweepChannelDialog : Form
{
    public RecordedSweepChannelDialog(
        IReadOnlyList<float[]> channels,
        IReadOnlyList<double> qualities)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(qualities);

        InitializeComponent();
        StyleGrid();

        for (int channel = 0; channel < channels.Count; channel++)
        {
            AudioChannelLevel level = RecordedLevelMetering.MeasureSamples(channels[channel]);
            channelGridView.Rows.Add(
                RecordedSweepFile.DescribeChannel(channel, channels.Count),
                FormattableString.Invariant($"{qualities[channel]:0.000}"),
                FormattableString.Invariant($"{level.RmsDbFs:0.0} dBFS"),
                FormattableString.Invariant($"{level.PeakDbFs:0.0} dBFS"));
        }

        SelectedChannel = RecordedSweepChannels.Best(qualities);
        channelGridView.Rows[SelectedChannel].Selected = true;
        channelGridView.SelectionChanged += (_, _) =>
        {
            if (channelGridView.CurrentRow is { } row)
            {
                SelectedChannel = row.Index;
            }
        };
    }

    public int SelectedChannel { get; private set; }

    private void StyleGrid()
    {
        channelGridView.EnableHeadersVisualStyles = false;
        channelGridView.GridColor = UiPalette.Border;
        channelGridView.DefaultCellStyle.BackColor = UiPalette.DialogBackground;
        channelGridView.DefaultCellStyle.ForeColor = UiPalette.TextPrimary;
        channelGridView.DefaultCellStyle.SelectionBackColor = UiPalette.ButtonPressedBackground;
        channelGridView.DefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
        channelGridView.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.ControlSurface;
        channelGridView.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
        channelGridView.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiPalette.ControlSurface;
        channelGridView.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
        for (int column = 1; column < channelGridView.Columns.Count; column++)
        {
            channelGridView.Columns[column].DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;
        }
    }

    private void channelGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            SelectedChannel = e.RowIndex;
            DialogResult = DialogResult.OK;
        }
    }
}
