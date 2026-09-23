using Resonalyze.Ui;

namespace Resonalyze.Options;

/// <summary>Shown only when ambiguous (<see cref="RecordedSweepChannels.IsAmbiguous"/>): a DAW track holding the played
/// sweep matches best and measures flat, and only the person who recorded it knows the microphone track. The rows
/// and the pick are a <see cref="RecordedSweepChannelChoice"/>.</summary>
internal sealed partial class RecordedSweepChannelDialog : Form
{
    private readonly RecordedSweepChannelChoice choice;

    public RecordedSweepChannelDialog(
        IReadOnlyList<float[]> channels,
        IReadOnlyList<double> qualities)
    {
        choice = new RecordedSweepChannelChoice(channels, qualities);

        InitializeComponent();
        StyleGrid();

        foreach (RecordedSweepChannelRow row in choice.Rows)
        {
            channelGridView.Rows.Add(row.Channel, row.Match, row.Rms, row.Peak);
        }

        channelGridView.Rows[choice.SelectedChannel].Selected = true;
        channelGridView.SelectionChanged += (_, _) =>
        {
            if (channelGridView.CurrentRow is { } row)
            {
                choice.Select(row.Index);
            }
        };
    }

    public int SelectedChannel => choice.SelectedChannel;

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
            choice.Select(e.RowIndex);
            DialogResult = DialogResult.OK;
        }
    }
}
