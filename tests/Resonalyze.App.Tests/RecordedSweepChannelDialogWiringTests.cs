using System.Windows.Forms;
using Resonalyze.Options;
using static Resonalyze.App.Tests.CalibrationDialogFixtures;

namespace Resonalyze.App.Tests;

public sealed class RecordedSweepChannelDialogWiringTests
{
    private static readonly float[][] Channels = [Level(0.001f), Level(0.5f), Level(0.25f)];
    private static readonly double[] Qualities = [0.01, 0.7, 1.0];

    [Fact]
    public void TheBestMatchStaysPickedWhenTheDialogShows() => StaTest.Run(() =>
    {
        using RecordedSweepChannelDialog dialog = Shown(new RecordedSweepChannelDialog(Channels, Qualities));
        DataGridView grid = In<DataGridView>(dialog, "channelGridView");

        Assert.Equal(2, dialog.SelectedChannel);
        Assert.Equal(2, grid.CurrentCell?.RowIndex);
        Assert.Equal([2], grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index));
    });

    [Fact]
    public void TheRowsAreTheChoicesRows() => StaTest.Run(() =>
    {
        var expected = new RecordedSweepChannelChoice(Channels, Qualities);
        using RecordedSweepChannelDialog dialog = Shown(new RecordedSweepChannelDialog(Channels, Qualities));

        Assert.Equal(
            expected.Rows.Select(row => new string?[] { row.Channel, row.Match, row.Rms, row.Peak }),
            In<DataGridView>(dialog, "channelGridView").Rows.Cast<DataGridViewRow>()
                .Select(row => row.Cells.Cast<DataGridViewCell>().Select(cell => (string?)cell.Value).ToArray()));
    });

    [Fact]
    public void AClickedRowIsTheOneMeasured() => StaTest.Run(() =>
    {
        using RecordedSweepChannelDialog dialog = Shown(new RecordedSweepChannelDialog(Channels, Qualities));
        DataGridView grid = In<DataGridView>(dialog, "channelGridView");

        ClickCell(grid, 0, 1);
        Assert.Equal(0, dialog.SelectedChannel);

        ClickCell(grid, 1, 0);
        Assert.Equal(1, dialog.SelectedChannel);
        Assert.Equal(DialogResult.None, dialog.DialogResult);
    });

    [Fact]
    public void ADoubleClickedRowIsMeasuredAtOnce() => StaTest.Run(() =>
    {
        using RecordedSweepChannelDialog dialog = Shown(new RecordedSweepChannelDialog(Channels, Qualities));

        ClickCell(In<DataGridView>(dialog, "channelGridView"), 1, 3, twice: true);

        Assert.Equal(1, dialog.SelectedChannel);
        Assert.Equal(DialogResult.OK, dialog.DialogResult);
    });

    private static float[] Level(float amplitude) =>
        Enumerable.Range(0, 4_800).Select(i => amplitude * MathF.Sin(i * 0.1f)).ToArray();
}
