using System.Drawing;
using System.Windows.Forms;
using Resonalyze.Integration.Rew;
using Resonalyze.Ui;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze.App.Tests;

public sealed class RewImportDialogTests
{
    private static readonly RewMeasurementSummary WithOffset = new()
    {
        Title = "test4.0", Uuid = "with-offset", SampleRate = 96_000, TimingOffsetSeconds = 0.004
    };

    private static readonly RewMeasurementSummary WithoutOffset = new()
    {
        Title = "older session", Uuid = "without-offset", SampleRate = 96_000
    };

    [Fact]
    public void AMeasurementWithoutARecordedOffset_DoesNotInheritTheLastRowsOffset() =>
        StaTest.Run(() => WithDialog(new RewLevel { Value = -12.0, Unit = "dBFS" }, dialog =>
        {
            Select(dialog, 0);
            Assert.Equal(4.0m, Offset(dialog).Value);

            Select(dialog, 1);

            Assert.Equal(0m, Offset(dialog).Value);
            Assert.False(Unknown(dialog).Checked);
        }));

    [Fact]
    public void AnOffsetTheUserStated_StaysWithItsOwnMeasurement() =>
        StaTest.Run(() => WithDialog(new RewLevel { Value = -12.0, Unit = "dBFS" }, dialog =>
        {
            Select(dialog, 1);
            Offset(dialog).Value = 1.5m;
            Select(dialog, 0);
            Assert.Equal(4.0m, Offset(dialog).Value);

            Select(dialog, 1);

            Assert.Equal(1.5m, Offset(dialog).Value);
        }));

    [Theory]
    [InlineData("dBFS", false)]
    [InlineData("dBV", true)]
    [InlineData(null, true)]
    public void TheLevelField_SaysWhenItDidNotComeFromRew(string? unit, bool warns) =>
        StaTest.Run(() => WithDialog(unit == null ? null : new RewLevel { Value = -6.0, Unit = unit }, dialog =>
        {
            var source = (Label)dialog.Controls["labelLevelSource"]!;

            Assert.Equal(warns, source.ForeColor == UiPalette.Warning);
            Assert.Equal(warns, source.Text.Contains("enter the dBFS", StringComparison.Ordinal));
        }));

    private static void WithDialog(RewLevel? level, Action<RewImportDialog> act)
    {
        var catalog = new RewMeasurementCatalog("5.40 Beta 134", [WithOffset, WithoutOffset], WithOffset.Uuid, level);
        using var dialog = new RewImportDialog(
            "http://localhost:4735/",
            96_000,
            (_, _) => Task.FromResult(catalog),
            (_, _, _, _, _) => Task.FromResult(new RewImportPreparation(null, "unused")));
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-6000, -6000);
        dialog.Show();
        // Shown is posted, and the list loads from it.
        Application.DoEvents();
        try
        {
            act(dialog);
        }
        finally
        {
            dialog.Hide();
        }
    }

    private static void Select(RewImportDialog dialog, int row)
    {
        var grid = (DataGridView)dialog.Controls["measurementGridView"]!;
        grid.CurrentCell = grid.Rows[row].Cells[0];
    }

    private static ThemedNumericUpDown Offset(RewImportDialog dialog) => (ThemedNumericUpDown)dialog.Controls["numericOffset"]!;

    private static CheckBox Unknown(RewImportDialog dialog) => (CheckBox)dialog.Controls["checkOffsetUnknown"]!;
}
