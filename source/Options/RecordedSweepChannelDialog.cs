using Resonalyze.Ui;

namespace Resonalyze.Options;

/// <summary>
/// Asks which channel of a multi-channel recording holds the measurement.
/// <para>
/// Shown only when the answer is not obvious — see
/// <see cref="RecordedSweepChannels.IsAmbiguous"/>. The case it exists for is a
/// DAW file carrying the played sweep on one track and the microphone on
/// another: the played track is a copy of the excitation, so it matches better
/// than any acoustic take ever will, wins any automatic choice, and then measures
/// as a flat response that passes every credibility check there is. Nothing in
/// the numbers says which track the microphone was on — only the person who made
/// the recording knows.
/// </para>
/// </summary>
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

        // Preselected on the best match, which is the right answer whenever the
        // file holds no reference track — the common case even here.
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

    /// <summary>The channel to measure, valid once the dialog returns OK.</summary>
    public int SelectedChannel { get; private set; }

    private void StyleGrid()
    {
        channelGridView.EnableHeadersVisualStyles = false;
        channelGridView.GridColor = UiPalette.DialogBorder;
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
