namespace Resonalyze.Options;

/// <summary>Shown only when ambiguous (<see cref="RecordedSweepChannels.IsAmbiguous"/>): a DAW track holding the played
/// sweep matches best and measures flat, and only the person who recorded it knows the microphone track. The rows
/// and the pick are a <see cref="RecordedSweepChannelChoice"/>.</summary>
internal sealed partial class RecordedSweepChannelDialog : Form
{
    private readonly RecordedSweepChannelChoice choice;
    // The grid picks its own first row while it is built and shown; only rows chosen after that are the user's.
    private bool presenting = true;

    public RecordedSweepChannelDialog(
        IReadOnlyList<float[]> channels,
        IReadOnlyList<double> qualities)
    {
        choice = new RecordedSweepChannelChoice(channels, qualities);

        InitializeComponent();
        StyleGrid();

        // A row carries its channel: a click on a header sorts the grid, and a position is no channel then.
        for (int channel = 0; channel < choice.Rows.Count; channel++)
        {
            RecordedSweepChannelRow row = choice.Rows[channel];
            channelGridView.Rows[channelGridView.Rows.Add(row.Channel, row.Match, row.Rms, row.Peak)].Tag = channel;
        }

        channelGridView.SelectionChanged += (_, _) =>
        {
            if (!presenting && channelGridView.CurrentRow is { } row)
            {
                choice.Select((int)row.Tag!);
            }
        };
        Shown += (_, _) => Present();
    }

    public int SelectedChannel => choice.SelectedChannel;

    private void Present()
    {
        presenting = true;
        try
        {
            channelGridView.CurrentCell = channelGridView.Rows.Cast<DataGridViewRow>()
                .First(row => (int)row.Tag! == choice.SelectedChannel).Cells[0];
        }
        finally
        {
            presenting = false;
        }
    }

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
            choice.Select((int)channelGridView.Rows[e.RowIndex].Tag!);
            DialogResult = DialogResult.OK;
        }
    }
}
