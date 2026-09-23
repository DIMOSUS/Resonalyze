using System.Windows.Forms;
using Resonalyze.Options;
using static Resonalyze.App.Tests.CalibrationDialogFixtures;

namespace Resonalyze.App.Tests;

[Collection(WindowInput.Name)]
public sealed class RecordedSweepChannelDialogWiringTests
{
    private static readonly float[][] Channels = [Level(0.001f), Level(0.5f), Level(0.25f)];
    private static readonly double[] Qualities = [0.01, 0.7, 1.0];

    [Fact]
    public void TheBestMatchStaysPickedWhenTheDialogShows() => Run(() =>
    {
        using RecordedSweepChannelDialog dialog = Shown(new RecordedSweepChannelDialog(Channels, Qualities));
        DataGridView grid = In<DataGridView>(dialog, "channelGridView");

        Assert.Equal(2, dialog.SelectedChannel);
        Assert.Equal(2, grid.CurrentCell?.RowIndex);
        Assert.Equal([2], grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index));
    });

    [Fact]
    public void TheRowsAreTheChoicesRows() => Run(() =>
    {
        var expected = new RecordedSweepChannelChoice(Channels, Qualities);
        using RecordedSweepChannelDialog dialog = Shown(new RecordedSweepChannelDialog(Channels, Qualities));

        Assert.Equal(
            expected.Rows.Select(row => new string?[] { row.Channel, row.Match, row.Rms, row.Peak }),
            In<DataGridView>(dialog, "channelGridView").Rows.Cast<DataGridViewRow>()
                .Select(row => row.Cells.Cast<DataGridViewCell>().Select(cell => (string?)cell.Value).ToArray()));
    });

    [Fact]
    public void EachRowNamesItsTrackMatchAndLevels()
    {
        var choice = new RecordedSweepChannelChoice(Channels, Qualities);
        AudioChannelLevel level = RecordedLevelMetering.MeasureSamples(Channels[1]);

        RecordedSweepChannelRow row = choice.Rows[1];

        Assert.Equal(RecordedSweepFile.DescribeChannel(1, 3), row.Channel);
        Assert.Equal("0.700", row.Match);
        Assert.Equal(FormattableString.Invariant($"{level.RmsDbFs:0.0} dBFS"), row.Rms);
        Assert.Equal(FormattableString.Invariant($"{level.PeakDbFs:0.0} dBFS"), row.Peak);
        Assert.NotEqual(row.Rms, row.Peak);
        Assert.Equal(2, choice.SelectedChannel);
    }

    [Fact]
    public void ASortedGridStillMeasuresTheTrackClicked() => Run(() =>
    {
        using RecordedSweepChannelDialog dialog = Shown(new RecordedSweepChannelDialog(Channels, Qualities));
        DataGridView grid = In<DataGridView>(dialog, "channelGridView");
        grid.Sort(grid.Columns["ColumnMatch"]!, System.ComponentModel.ListSortDirection.Descending);
        StaTest.Pump();

        ClickCell(grid, 1, 0);
        Assert.Equal(1, dialog.SelectedChannel);
        ClickCell(grid, 2, 0, twice: true);

        Assert.Equal(0, dialog.SelectedChannel);
        Assert.Equal(DialogResult.OK, dialog.DialogResult);
    });

    [Fact]
    public void AClickedRowIsTheOneMeasured() => Run(() =>
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
    public void ADoubleClickedRowIsMeasuredAtOnce() => Run(() =>
    {
        using RecordedSweepChannelDialog dialog = Shown(new RecordedSweepChannelDialog(Channels, Qualities));

        ClickCell(In<DataGridView>(dialog, "channelGridView"), 1, 3, twice: true);

        Assert.Equal(1, dialog.SelectedChannel);
        Assert.Equal(DialogResult.OK, dialog.DialogResult);
    });

    private static float[] Level(float amplitude) =>
        Enumerable.Range(0, 4_800).Select(i => amplitude * MathF.Sin(i * 0.1f)).ToArray();
}
