using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Resonalyze;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The junctions table: one row per adjacent pair, the window the search ran on written back into it, and
/// the options block still below it. See docs/tech/crossover-auto-setup.md#per-junction-windows.</summary>
public sealed class VirtualCrossoverAutoSetupJunctionRowTests
{
    private const double SampleRate = 48_000;

    private static List<SignalPoint> BandCurve(double lowHz, double highHz, double levelDb = 0)
    {
        var points = new List<SignalPoint>();
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y = levelDb;
            if (frequency < lowHz)
            {
                y -= 24.0 * Math.Log2(lowHz / frequency);
            }
            else if (frequency > highHz)
            {
                y -= 24.0 * Math.Log2(frequency / highHz);
            }

            points.Add(new SignalPoint(frequency, y));
        }

        return points;
    }

    private static AutoSetupWizardChannel Channel(
        string name,
        double lowHz,
        double highHz)
    {
        List<SignalPoint> curve = BandCurve(lowHz, highHz);
        return new AutoSetupWizardChannel(
            name,
            Color.White,
            VirtualCrossoverAlignmentStage.FrontChain,
            curve,
            null,
            null,
            CrossoverAutoSetup.EstimateBand(curve),
            null,
            null,
            null);
    }

    private static IReadOnlyList<AutoSetupWizardChannel> FourWay() =>
    [
        Channel("A sub", 20, 90),
        Channel("B midbass", 60, 900),
        Channel("C mid", 250, 6_000),
        Channel("D tweeter", 2_200, 20_000)
    ];

    private static T Field<T>(VirtualCrossoverAutoSetupDialog dialog, string name) =>
        (T)typeof(VirtualCrossoverAutoSetupDialog)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dialog)!;

    [Fact]
    public void AFourWay_GetsOneJunctionRowPerAdjacentPair()
    {
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());

            var junctions = (System.Collections.IList)Field<object>(dialog, "junctions");
            Assert.Equal(3, junctions.Count);
        });
    }

    [Fact]
    public void AnUntouchedRow_ShowsTheWindowTheSearchActuallyRanOn()
    {
        // The row is not a blank form: it prints the wizard's own answer, so nothing about it is a guess.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());

            var junctions = (System.Collections.IList)Field<object>(dialog, "junctions");
            var ranges = new List<(decimal Low, decimal High)>();
            foreach (object? junction in junctions)
            {
                ranges.Add((
                    ((ThemedNumericUpDown)junction!.GetType().GetProperty("MinHz")!
                        .GetValue(junction)!).Value,
                    ((ThemedNumericUpDown)junction.GetType().GetProperty("MaxHz")!
                        .GetValue(junction)!).Value));
            }

            // A pinned junction reads low == high legitimately; what must never happen is a crossed pair, and the
            // fields must not all still be sitting on the control's own default.
            Assert.All(ranges, range => Assert.True(
                range.Low <= range.High, $"{range.Low}-{range.High} Hz is inverted."));
            Assert.Contains(ranges, range => range.Low < range.High);
            Assert.Contains(ranges, range => range.Low > 20m);
        });
    }

    [Fact]
    public void EverySlopeWindowOffered_HoldsTheStandardSlope()
    {
        // The user may narrow the slopes but never out of reach of 24 dB/oct, which the score is anchored on.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());

            var boxes = Field<TableLayoutPanel>(dialog, "tableJunctions")
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
    public void NarrowingAJunction_MovesWhereThatJunctionLands()
    {
        // The window has to reach the search, not just the screen.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());

            var junctions = (System.Collections.IList)Field<object>(dialog, "junctions");
            object top = junctions[2]!;
            var minHz = (ThemedNumericUpDown)top.GetType()
                .GetProperty("MinHz")!.GetValue(top)!;
            var maxHz = (ThemedNumericUpDown)top.GetType()
                .GetProperty("MaxHz")!.GetValue(top)!;
            decimal before = minHz.Value;

            minHz.Value = 4_000m;
            maxHz.Value = 6_000m;

            Assert.True(minHz.Value >= 4_000m || minHz.Value > before,
                $"The tweeter junction floor stayed at {minHz.Value} Hz.");
            IReadOnlyList<CrossoverProposal> proposals = Apply(dialog);
            double corner = proposals[3].HighPassEdge!.Value.FrequencyHz;
            Assert.InRange(corner, 4_000, 6_000);
        });
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
                var channels = Field<TableLayoutPanel>(dialog, "tableChannels");
                var junctionsTable = Field<TableLayoutPanel>(dialog, "tableJunctions");
                var filters = Field<Label>(dialog, "labelFilters");
                // The card, not the label inside it: the label's Bottom is relative to its parent now.
                var preview = Field<Control>(dialog, "panelPreview");

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
    public void ANoteAppearingAfterShow_PushesTheOptionsDown()
    {
        // The notes line under a row shows up on an edit, long after the first layout: the table grows, and the
        // options below used to stay where they were and cover it.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());
            dialog.Show();
            try
            {
                var junctions = (System.Collections.IList)Field<object>(dialog, "junctions");
                foreach (object? junction in junctions)
                {
                    // Beyond what the drivers can take, so every bound is moved and says so.
                    ((ThemedNumericUpDown)junction!.GetType().GetProperty("MinHz")!
                        .GetValue(junction)!).Value = 20m;
                    ((ThemedNumericUpDown)junction.GetType().GetProperty("MaxHz")!
                        .GetValue(junction)!).Value = 20_000m;
                }

                StaTest.Settle(dialog.PendingPreview);
                Assert.Contains(
                    junctions.Cast<object>(),
                    junction => ((Label)junction.GetType().GetProperty("Notes")!
                        .GetValue(junction)!).Visible);

                var junctionsTable = Field<TableLayoutPanel>(dialog, "tableJunctions");
                var filters = Field<Label>(dialog, "labelFilters");
                var preview = Field<Control>(dialog, "panelPreview");
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
                var junctions = (System.Collections.IList)Field<object>(dialog, "junctions");
                foreach (object? junction in junctions)
                {
                    ((CheckBox)junction!.GetType().GetProperty("Split")!
                        .GetValue(junction)!).Checked = true;
                }

                StaTest.Settle(dialog.PendingPreview);
                var table = Field<TableLayoutPanel>(dialog, "tableJunctions");
                Assert.True(
                    table.Right <= dialog.ClientSize.Width,
                    $"The junctions table ends at {table.Right}, past the client area " +
                    $"{dialog.ClientSize.Width}.");

                foreach (object? junction in junctions)
                {
                    var verdict = (Label)junction!.GetType().GetProperty("Verdict")!
                        .GetValue(junction)!;
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

    [Fact]
    public void AJunctionNarrowedByHand_SurvivesAReorderElsewhere()
    {
        // A reorder disposes and rebuilds every row. A window the user typed into a pair the reorder never touched
        // must come back, or Apply quietly runs on automatic bounds instead of the ones on screen.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());

            var junctions = (System.Collections.IList)Field<object>(dialog, "junctions");
            object top = junctions[2]!;
            var minHz = (ThemedNumericUpDown)top.GetType().GetProperty("MinHz")!.GetValue(top)!;
            var maxHz = (ThemedNumericUpDown)top.GetType().GetProperty("MaxHz")!.GetValue(top)!;
            ((CheckBox)top.GetType().GetProperty("Split")!.GetValue(top)!).Checked = true;
            minHz.Value = 4_000m;
            maxHz.Value = 6_000m;

            // Move the two lowest drivers past each other: the mid/tweeter pair is not involved. Driven through
            // MoveInChain rather than the arrow, because Button.PerformClick needs a shown window to select.
            var rows = (System.Collections.IList)Field<object>(dialog, "rows");
            typeof(VirtualCrossoverAutoSetupDialog)
                .GetMethod("MoveInChain", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(dialog, [rows[0], 1]);
            Assert.NotSame(top, ((System.Collections.IList)Field<object>(dialog, "junctions"))[2]);

            var rebuilt = (System.Collections.IList)Field<object>(dialog, "junctions");
            object again = rebuilt[2]!;
            Assert.Equal(
                4_000m,
                ((ThemedNumericUpDown)again.GetType().GetProperty("MinHz")!.GetValue(again)!).Value);
            Assert.Equal(
                6_000m,
                ((ThemedNumericUpDown)again.GetType().GetProperty("MaxHz")!.GetValue(again)!).Value);
            Assert.True(
                ((CheckBox)again.GetType().GetProperty("Split")!.GetValue(again)!).Checked,
                "The Split the user asked for was lost with the rebuild.");
        });
    }

    [Fact]
    public void TheJunctionFields_FreezeWhileTheRankedRunOwnsTheSnapshot()
    {
        // Apply snapshots the options and ranks off the UI thread for seconds. A junction edited in that window
        // would show one thing and apply another, and its preview would hand the Apply button back mid-ranking.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoSetupDialog();
            dialog.Init(SampleRate, SampleRate, FourWay());
            typeof(VirtualCrossoverAutoSetupDialog)
                .GetMethod("SetRankingInputsEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(dialog, [false]);

            var junctions = (System.Collections.IList)Field<object>(dialog, "junctions");
            foreach (object? junction in junctions)
            {
                foreach (string name in new[] { "MinHz", "MaxHz", "MinSlope", "MaxSlope", "Split" })
                {
                    var control = (Control)junction!.GetType().GetProperty(name)!.GetValue(junction)!;
                    Assert.False(control.Enabled, $"{name} stayed live during the ranked run.");
                }
            }
        });
    }

    private static IReadOnlyList<CrossoverProposal> Apply(VirtualCrossoverAutoSetupDialog dialog)
    {
        typeof(VirtualCrossoverAutoSetupDialog)
            .GetMethod("ApplyClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, [null, EventArgs.Empty]);
        Assert.NotNull(dialog.Result);
        return dialog.Result!;
    }
}
