using System.Windows.Forms;
using Resonalyze.Dsp;
using static Resonalyze.App.Tests.AutoSetupWizardFixtures;

namespace Resonalyze.App.Tests;

/// <summary>The junctions: one per adjacent pair, the window the search runs on, and the table that shows them with
/// the options block still below it. See docs/tech/crossover-auto-setup.md#per-junction-windows.</summary>
public sealed class VirtualCrossoverAutoSetupJunctionRowTests
{
    private static T Find<T>(Control dialog, string name) where T : Control =>
        (T)dialog.Controls.Find(name, searchAllChildren: true).Single();

    [Fact]
    public void AFourWay_GetsOneJunctionPerAdjacentPair()
    {
        List<AutoSetupWizardJunction> junctions = Session(FourWay()).Junctions();

        Assert.Equal(["A sub/B midbass", "B midbass/C mid", "C mid/D tweeter"],
            junctions.Select(junction => $"{junction.Lower.Name}/{junction.Upper.Name}"));
        Assert.Equal([0, 1, 2], junctions.Select(junction => junction.IndexInGroup));
    }

    [Fact]
    public void AnUntouchedJunction_ResolvesToTheWindowTheSearchRunsOn()
    {
        var ranges = AutoSetupWizardPlan.ResolvedWindows(Session(FourWay()))
            .Select(item => (
                Low: AutoSetupWizardPlan.FieldHz(item.Window.LowHz),
                High: AutoSetupWizardPlan.FieldHz(item.Window.HighHz)))
            .ToList();

        // A pinned junction reads low == high legitimately; what must never happen is a crossed pair, and the
        // fields must not all be sitting on the field's own floor.
        Assert.Equal(3, ranges.Count);
        Assert.All(ranges, range => Assert.True(range.Low <= range.High, $"{range.Low}-{range.High} Hz is inverted."));
        Assert.Contains(ranges, range => range.Low < range.High);
        Assert.Contains(ranges, range => range.Low > AutoSetupWizardPlan.FieldMinimumHz);
    }

    [Fact]
    public void EverySlopeWindowOffered_HoldsTheStandardSlope()
    {
        // The user may narrow the slopes but never out of reach of 24 dB/oct, which the score is anchored on.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());

            List<ThemedComboBox> boxes = Find<TableLayoutPanel>(dialog, "tableJunctions")
                .Controls.OfType<ThemedComboBox>()
                .ToList();
            Assert.Equal(6, boxes.Count);
            for (int i = 0; i < boxes.Count; i += 2)
            {
                int min = (int)boxes[i].SelectedItem!;
                int max = (int)boxes[i + 1].SelectedItem!;
                Assert.True(
                    min <= CrossoverAutoSetup.MandatorySlopeDbPerOctave &&
                        max >= CrossoverAutoSetup.MandatorySlopeDbPerOctave,
                    $"Junction {i / 2} offers {min}-{max} dB/oct, which leaves out " +
                    $"{CrossoverAutoSetup.MandatorySlopeDbPerOctave}.");
            }
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void NarrowingAJunction_MovesWhereThatJunctionLands()
    {
        // The window has to reach the search, not just the screen.
        AutoSetupWizardSession session = Session(FourWay());
        AutoSetupWizardJunction top = session.Junctions()[2];

        session.Edit(top, AutoSetupJunctionEdits.None with { MinHz = 4_000m, MaxHz = 6_000m });

        Assert.InRange(Proposals(session)[3].HighPassEdge!.Value.FrequencyHz, 4_000, 6_000);
    }

    [Fact]
    public void AJunctionNarrowedByHand_SurvivesAReorderElsewhere()
    {
        // A reorder rebuilds every row. A window the user set on a pair the reorder never touched must come back, or
        // Apply quietly runs on automatic bounds instead of the ones on screen.
        AutoSetupWizardSession session = Session(FourWay());
        var edits = new AutoSetupJunctionEdits(4_000m, 6_000m, null, 36, Split: true);
        session.Edit(session.Junctions()[2], edits);

        Assert.True(session.MoveInChain(session.Rows[0], +1));

        AutoSetupWizardJunction again = session.Junctions()[2];
        Assert.Equal(("C mid", "D tweeter"), (again.Lower.Name, again.Upper.Name));
        Assert.Equal(edits, session.EditsOf(again));
        Assert.Equal(AutoSetupJunctionEdits.None, session.EditsOf(session.Junctions()[0]));
    }

    [Fact]
    public void AJunctionWhosePairIsBrokenUp_KeepsItsWindowForWhenThePairReturns()
    {
        AutoSetupWizardSession session = Session(FourWay());
        var edits = AutoSetupJunctionEdits.None with { MaxHz = 70m };
        session.Edit(session.Junctions()[0], edits);

        session.MoveInChain(session.Rows[0], +1);
        Assert.DoesNotContain(session.Junctions(), junction => session.EditsOf(junction) == edits);
        session.MoveInChain(session.Rows[1], -1);

        Assert.Equal(edits, session.EditsOf(session.Junctions()[0]));
    }

    [Fact]
    public void AJunctionBackToNothingSet_IsForgotten()
    {
        AutoSetupWizardSession session = Session(FourWay());
        AutoSetupWizardJunction junction = session.Junctions()[1];

        session.Edit(junction, AutoSetupJunctionEdits.None with { Split = true });
        session.Edit(junction, AutoSetupJunctionEdits.None);

        Assert.Same(AutoSetupJunctionEdits.None, session.EditsOf(junction));
        Assert.Equal(new JunctionSearchWindow(), session.EditsOf(junction).ToWindow());
    }

    [Fact]
    public void TheOptionsBlock_StaysBelowTheJunctionsTable()
    {
        // The options used to sit under the channel table; a second table between them has to push them, not overlap.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());
            dialog.Show();
            try
            {
                var channels = Find<TableLayoutPanel>(dialog, "tableChannels");
                var junctionsTable = Find<TableLayoutPanel>(dialog, "tableJunctions");
                var filters = Find<Label>(dialog, "labelFilters");
                // The card, not the label inside it: the label's Bottom is relative to its parent now.
                var preview = Find<Control>(dialog, "panelPreview");

                Assert.True(
                    junctionsTable.Top >= channels.Bottom,
                    $"The junctions table at {junctionsTable.Top} overlaps the channels table " +
                    $"ending at {channels.Bottom}.");
                Assert.True(
                    filters.Top >= junctionsTable.Bottom,
                    $"The options at {filters.Top} overlap the junctions table ending at " +
                    $"{junctionsTable.Bottom}.");
                Assert.True(
                    preview.Bottom <= dialog.ClientSize.Height,
                    $"The preview ends at {preview.Bottom}, past the client area " +
                    $"{dialog.ClientSize.Height}.");
                Assert.True(
                    junctionsTable.Right <= dialog.ClientSize.Width,
                    $"The junctions table ends at {junctionsTable.Right}, past the client " +
                    $"area {dialog.ClientSize.Width}.");
            }
            finally
            {
                dialog.Hide();
            }
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void ANoteAppearingAfterShow_PushesTheOptionsDown()
    {
        // The notes line under a row shows up on an edit, long after the first layout, and grows the table.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());
            dialog.Show();
            try
            {
                var junctionsTable = Find<TableLayoutPanel>(dialog, "tableJunctions");
                foreach (ThemedNumericUpDown field in junctionsTable.Controls.OfType<ThemedNumericUpDown>())
                {
                    // Beyond what the drivers can take, so every bound is moved and says so.
                    field.Value = junctionsTable.GetColumn(field) == 1
                        ? AutoSetupWizardPlan.FieldMinimumHz
                        : AutoSetupWizardPlan.FieldMaximumHz;
                }

                StaTest.Settle(dialog.PendingPreview);
                Assert.Contains(
                    junctionsTable.Controls.OfType<Label>(),
                    label => junctionsTable.GetColumn(label) == 0 &&
                        junctionsTable.GetRow(label) % 2 == 1 &&
                        label.Visible);

                var filters = Find<Label>(dialog, "labelFilters");
                var preview = Find<Control>(dialog, "panelPreview");
                Assert.True(
                    filters.Top >= junctionsTable.Bottom,
                    $"The options at {filters.Top} overlap the junctions table ending at " +
                    $"{junctionsTable.Bottom}.");
                Assert.True(
                    preview.Bottom <= dialog.ClientSize.Height,
                    $"The preview ends at {preview.Bottom}, past the client area " +
                    $"{dialog.ClientSize.Height}.");
            }
            finally
            {
                dialog.Hide();
            }
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void ASplitVerdict_FitsInsideTheWindow()
    {
        // A split junction prints both corners and may add "inverted", which is the longest the column ever gets.
        // The verdict arrives after the one-shot layout pass has sized the window, so the window has to grow to it.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());
            dialog.Show();
            try
            {
                var table = Find<TableLayoutPanel>(dialog, "tableJunctions");
                foreach (CheckBox split in table.Controls.OfType<CheckBox>())
                {
                    split.Checked = true;
                }

                StaTest.Settle(dialog.PendingPreview);
                Assert.True(
                    table.Right <= dialog.ClientSize.Width,
                    $"The junctions table ends at {table.Right}, past the client area " +
                    $"{dialog.ClientSize.Width}.");

                for (int line = 0; line < table.RowCount; line += 2)
                {
                    var verdict = (Label)table.GetControlFromPosition(8, line)!;
                    Assert.True(
                        verdict.Width >= verdict.PreferredSize.Width,
                        $"\"{verdict.Text}\" is {verdict.Width}px wide but needs " +
                        $"{verdict.PreferredSize.Width}px.");
                    // The verdict's own bounds are its parent's: a table-relative Right says nothing about the window.
                    int right = table.Left + verdict.Right;
                    Assert.True(
                        right <= dialog.ClientSize.Width,
                        $"\"{verdict.Text}\" ends at {right}, past the client area " +
                        $"{dialog.ClientSize.Width}.");
                }
            }
            finally
            {
                dialog.Hide();
            }
        });
    }
}
