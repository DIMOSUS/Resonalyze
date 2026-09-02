using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// The review dialog is the one gate between an assistant's reply and the tune,
/// so what it lets through is pinned: admissible rows start ticked, rejected
/// rows are listed and cannot be ticked, Apply needs at least one tick, and the
/// status is a word before it is a colour.
/// </summary>
public sealed class AgentProposalDialogTests
{
    [Fact]
    public void Rows_StartTickedWhereApplicable_AndRejectedRowsCannotBeTicked()
    {
        StaTest.Run(() =>
        {
            using var dialog = new AgentProposalDialog(Review());
            DataGridView grid = Grid(dialog);

            // The parser's rejections lead, then the operations in reply order.
            Assert.Equal(4, grid.Rows.Count);
            Assert.Equal(false, grid.Rows[0].Cells[0].Value);
            Assert.Equal("Rejected", grid.Rows[0].Cells[5].Value);
            Assert.True(grid.Rows[0].Cells[0].ReadOnly);
            Assert.Equal(true, grid.Rows[1].Cells[0].Value);
            Assert.Equal("OK", grid.Rows[1].Cells[5].Value);
            Assert.Equal(true, grid.Rows[2].Cells[0].Value);
            Assert.Equal("Warning", grid.Rows[2].Cells[5].Value);
            Assert.Equal(false, grid.Rows[3].Cells[0].Value);
            Assert.Equal("Rejected", grid.Rows[3].Cells[5].Value);
            Assert.True(grid.Rows[3].Cells[0].ReadOnly);

            // Even a value forced into a rejected row's box is not a selection.
            grid.Rows[3].Cells[0].Value = true;
            Assert.Equal(["op-1", "op-2"], dialog.Selected.Select(verdict => verdict.Id));

            Assert.Equal("A right", grid.Rows[1].Cells[1].Value);
            Assert.Equal("-2.0 dB", grid.Rows[1].Cells[3].Value);
            Assert.Equal("-3.0 dB", grid.Rows[1].Cells[4].Value);
        });
    }

    [Fact]
    public void ApplyNeedsATick_AndTheDetailBoxCarriesTheMessageAndAdvice()
    {
        StaTest.Run(() =>
        {
            using var dialog = new AgentProposalDialog(Review());
            DataGridView grid = Grid(dialog);
            Button apply = dialog.Controls.OfType<Button>().Single(button => button.Text == "Apply selected");
            Assert.True(apply.Enabled);

            grid.Rows[1].Cells[0].Value = false;
            grid.Rows[2].Cells[0].Value = false;
            Assert.Empty(dialog.Selected);
            Assert.False(apply.Enabled);

            grid.ClearSelection();
            grid.Rows[3].Selected = true;
            TextBox detail = (TextBox)dialog.Controls["textBoxDetail"]!;
            Assert.Contains("op-3", detail.Text);
            Assert.Contains("Rejected", detail.Text);
            Assert.Contains("changed since the package was copied", detail.Text);
            Assert.Contains("Run Auto delay afterwards.", detail.Text);
            Assert.Contains("https://example.com/datasheet.pdf", detail.Text);
            Assert.Contains("different package", dialog.Controls["labelWarnings"]!.Text);
        });
    }

    [Fact]
    public void ActionButtons_StayInsideTheClientArea()
    {
        StaTest.Run(() =>
        {
            using var dialog = new AgentProposalDialog(Review());
            foreach (string text in new[] { "Apply selected", "Cancel" })
            {
                Button button = dialog.Controls.OfType<Button>().Single(candidate => candidate.Text == text);
                Assert.True(button.Bottom <= dialog.ClientSize.Height, text);
                Assert.True(button.Right <= dialog.ClientSize.Width, text);
            }
            Assert.True(Grid(dialog).Bottom < dialog.Controls["textBoxDetail"]!.Top);
        });
    }

    private static DataGridView Grid(Form dialog) => (DataGridView)dialog.Controls["gridView"]!;

    private static AgentProposalReview Review()
    {
        var aRight = new VirtualCrossoverChannelSettings { GainDb = -2.0, DelayMs = 1.42 };
        var bLeft = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 250, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2800, 24)
        };
        var session = new AgentSessionSnapshot(
            [
                new AgentChannelSnapshot("A", AgentChannelSide.Right, aRight),
                new AgentChannelSnapshot("B", AgentChannelSide.Left, bLeft)
            ],
            96_000, 10, "11111111-1111-1111-1111-111111111111");
        var proposal = new AgentProposal(
            "22222222-2222-2222-2222-222222222222",
            "The left mid/tweeter junction cancels near 3.1 kHz.",
            ["Run Auto delay afterwards."],
            [new AgentSource("https://example.com/datasheet.pdf", "Datasheet", ["Fs 65 Hz"])],
            [
                new SetGainOperation("op-1", "A:right", "level", -2.0, -3.0),
                new SetDelayOperation("op-2", "A:right", "arrival", 1.42, 12.5),
                new SetPolarityOperation("op-3", "B:left", "phase", true, false)
            ],
            [new AgentRejectedOperation("op-9", "setTarget", "Unsupported operation 'setTarget'.")]);
        return AgentProposalValidator.Review(proposal, session);
    }
}
