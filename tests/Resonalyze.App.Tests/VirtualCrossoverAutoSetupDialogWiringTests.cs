using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Ui;
using static Resonalyze.App.Tests.AutoSetupWizardFixtures;

namespace Resonalyze.App.Tests;

/// <summary>
/// A shown wizard driven through its controls, beside a session the test changes the same way: what the dialog shows
/// and what Apply hands back must be what the readers make of that session. The readers have their own tests; these
/// pin the binding.
/// </summary>
public sealed class VirtualCrossoverAutoSetupDialogWiringTests
{
    public static TheoryData<string> Changes =>
    [
        "type", "families", "floor", "ceiling", "tied slopes", "elevation",
        "junction floor", "junction ceiling", "steepest slope", "split"
    ];

    [Theory]
    [MemberData(nameof(Changes))]
    public void EachControl_ReachesTheProposalApplyWrites(string change) => StaTest.Run(() =>
    {
        using var wizard = new Wizard(LoudSub());
        AutoSetupWizardSession expected = Session(LoudSub());
        Preview(expected);
        CrossoverProposal[] untouched = Proposals(expected);

        Change(change, wizard, expected);
        Preview(expected);
        wizard.Settle();

        CrossoverProposal[] applied = wizard.Apply();
        Assert.Equal(Proposals(expected), applied);
        Assert.NotEqual(untouched, applied);
        Assert.Equal(expected.RequestedChainOrder(), wizard.Dialog.ChainOrder);
    });

    [Fact]
    public void ClearingTheReorder_AsksForNoBlockOrder() => StaTest.Run(() =>
    {
        using var wizard = new Wizard(FourWay());
        wizard.Apply();
        Assert.Equal([0, 1, 2, 3], wizard.Dialog.ChainOrder);

        using var cleared = new Wizard(FourWay());
        cleared.Find<CheckBox>("reorderBlocks").Checked = false;
        cleared.Settle();
        cleared.Apply();

        Assert.Null(cleared.Dialog.ChainOrder);
    });

    [Fact]
    public void TheRows_ShowWhatTheReadersMakeOfTheSession() => StaTest.Run(() =>
    {
        using var wizard = new Wizard(FourWay());
        AutoSetupWizardSession expected = Session(FourWay());
        AssertShows(wizard, expected, Preview(expected)!);

        // 24 dB/oct stays in every window, so a gentlest slope of 30 changes no fit: only the row can show it held.
        wizard.MinHz(2).Value = 4_000m;
        wizard.Split(1).Checked = true;
        wizard.MinSlope(0).SelectedItem = 30;
        wizard.Settle();
        AutoSetupWizardJunction top = expected.Junctions()[2];
        expected.Edit(top, expected.EditsOf(top) with { MinHz = 4_000m });
        AutoSetupWizardJunction middle = expected.Junctions()[1];
        expected.Edit(middle, expected.EditsOf(middle) with { Split = true });
        AutoSetupWizardJunction bottom = expected.Junctions()[0];
        expected.Edit(bottom, expected.EditsOf(bottom) with { MinSlope = 30 });

        AssertShows(wizard, expected, Preview(expected)!);
        Assert.Equal(4_000m, wizard.MinHz(2).Value);
        Assert.Equal(30, wizard.MinSlope(0).SelectedItem);
    });

    [Fact]
    public void AnArrow_ReordersTheChain_MarksTheDoubtfulRows_AndKeepsAWindowSetByHand() => StaTest.Run(() =>
    {
        using var wizard = new Wizard(FourWay());
        wizard.MinHz(2).Value = 4_000m;
        wizard.MaxHz(2).Value = 6_000m;
        wizard.Split(2).Checked = true;

        wizard.Arrow(0, up: false).PerformClick();
        wizard.Settle();

        AutoSetupWizardSession expected = Session(FourWay());
        expected.Edit(
            expected.Junctions()[2],
            new AutoSetupJunctionEdits(4_000m, 6_000m, null, null, Split: true));
        expected.MoveInChain(expected.Rows[0], +1);
        Preview(expected);
        Assert.Equal(
            expected.Rows.Select(row => row.Source.Name),
            Enumerable.Range(0, 4).Select(line => wizard.ChannelCell(1, line).Text));
        Dictionary<AutoSetupWizardRow, VirtualCrossoverChainOrder> marks = AutoSetupWizardChainOrder.Marks(expected);
        Assert.NotEmpty(marks);
        for (int line = 0; line < 4; line++)
        {
            Color colour = marks.TryGetValue(expected.Rows[line], out VirtualCrossoverChainOrder verdict)
                ? verdict == VirtualCrossoverChainOrder.Reversed ? UiPalette.Danger : UiPalette.Warning
                : UiPalette.TextSecondary;
            Assert.Equal(colour, wizard.ChannelCell(2, line).ForeColor);
        }

        Assert.False(wizard.Arrow(0, up: true).Enabled);
        Assert.False(wizard.Arrow(3, up: false).Enabled);
        Assert.Equal(4_000m, wizard.MinHz(2).Value);
        Assert.Equal(6_000m, wizard.MaxHz(2).Value);
        Assert.True(wizard.Split(2).Checked);
        AssertShows(wizard, expected, Preview(expected)!);
    });

    [Fact]
    public void WithNoFamily_ApplyIsOffAndThePreviewSaysWhy() => StaTest.Run(() =>
    {
        using var wizard = new Wizard(FourWay());
        foreach (string box in new[] { "checkButterworth", "checkLinkwitzRiley", "checkBessel" })
        {
            wizard.Find<CheckBox>(box).Checked = false;
        }

        wizard.Settle();

        Assert.False(wizard.Find<Button>("buttonApply").Enabled);
        Assert.Equal("Enable at least one filter family.", wizard.Find<Label>("labelPreview").Text);
        wizard.Find<CheckBox>("checkBessel").Checked = true;
        wizard.Settle();
        Assert.True(wizard.Find<Button>("buttonApply").Enabled);
    });

    [Fact]
    public void TheRankedRun_FreezesEveryInputUntilItLands() => StaTest.Run(() =>
    {
        var impulse = new Complex[4_096];
        impulse[64] = 1;
        using var wizard = new Wizard(FourWay().Select(channel => channel with { ImpulseResponse = impulse }).ToList());

        wizard.Find<Button>("buttonApply").PerformClick();

        Assert.Null(wizard.Dialog.Result);
        Assert.Equal("Ranking candidates against the measured responses…", wizard.Find<Label>("labelPreview").Text);
        List<Control> inputs =
        [
            .. wizard.Find<TableLayoutPanel>("tableChannels").Controls.OfType<ThemedComboBox>(),
            .. wizard.Find<TableLayoutPanel>("tableChannels").Controls.OfType<Button>(),
            .. wizard.Find<TableLayoutPanel>("tableJunctions").Controls.OfType<ThemedNumericUpDown>(),
            .. wizard.Find<TableLayoutPanel>("tableJunctions").Controls.OfType<ThemedComboBox>(),
            .. wizard.Find<TableLayoutPanel>("tableJunctions").Controls.OfType<CheckBox>(),
            .. new[]
            {
                "checkButterworth", "checkLinkwitzRiley", "checkBessel", "minCrossover", "maxCrossover",
                "independentSlopes", "reorderBlocks", "subElevation", "buttonApply"
            }.Select(wizard.Find<Control>)
        ];
        Assert.All(inputs, input => Assert.False(input.Enabled, $"{input.Name} {input.Text} stayed live."));

        wizard.WaitForResult();
        Assert.Equal(4, wizard.Dialog.Result!.Count);
    });

    // A sub 8 dB up, so the measured bass elevation is there to be lowered.
    private static List<AutoSetupWizardChannel> LoudSub() =>
        [Channel("A sub", VirtualCrossoverAlignmentStage.FrontChain, 20, 90, levelDb: 8), .. FourWay().Skip(1)];

    private static void Change(string change, Wizard wizard, AutoSetupWizardSession expected)
    {
        AutoSetupWizardJunction middle = expected.Junctions()[1];
        AutoSetupWizardJunction top = expected.Junctions()[2];
        switch (change)
        {
            case "type":
                wizard.TypeBox(2).SelectedItem = DriverType.Midbass;
                expected.Rows[2].Type = DriverType.Midbass;
                break;
            case "families":
                wizard.Find<CheckBox>("checkLinkwitzRiley").Checked = false;
                wizard.Find<CheckBox>("checkBessel").Checked = false;
                expected.SetFamily(CrossoverFilterFamily.LinkwitzRiley, false);
                expected.SetFamily(CrossoverFilterFamily.Bessel, false);
                break;
            case "floor":
                wizard.Find<ThemedNumericUpDown>("minCrossover").Value = 60m;
                expected.MinCrossoverHz = 60m;
                break;
            case "ceiling":
                wizard.Find<ThemedNumericUpDown>("maxCrossover").Value = 9_000m;
                expected.MaxCrossoverHz = 9_000m;
                break;
            case "tied slopes":
                wizard.Find<CheckBox>("independentSlopes").Checked = false;
                expected.IndependentSlopes = false;
                break;
            case "elevation":
                var elevation = wizard.Find<ThemedNumericUpDown>("subElevation");
                Assert.Equal(expected.ElevationRange.Maximum, elevation.Maximum);
                Assert.Equal(expected.SubElevationDb, elevation.Value);
                elevation.Value = expected.SubElevationDb - 4m;
                expected.SubElevationDb -= 4m;
                break;
            case "junction floor":
                wizard.MinHz(2).Value = 4_000m;
                expected.Edit(top, expected.EditsOf(top) with { MinHz = 4_000m });
                break;
            case "junction ceiling":
                wizard.MaxHz(1).Value = 200m;
                expected.Edit(middle, expected.EditsOf(middle) with { MaxHz = 200m });
                break;
            case "steepest slope":
                wizard.MaxSlope(1).SelectedItem = 18;
                expected.Edit(middle, expected.EditsOf(middle) with { MaxSlope = 18 });
                break;
            case "split":
                // Split moves this fit only with the slopes tied.
                wizard.Find<CheckBox>("independentSlopes").Checked = false;
                wizard.Split(1).Checked = true;
                expected.IndependentSlopes = false;
                expected.Edit(middle, expected.EditsOf(middle) with { Split = true });
                break;
        }
    }

    private static void AssertShows(Wizard wizard, AutoSetupWizardSession expected, AutoSetupPreview preview)
    {
        foreach ((AutoSetupWizardJunction junction, JunctionWindowResolution window)
                 in AutoSetupWizardPlan.ResolvedWindows(expected))
        {
            int j = junction.IndexInGroup;
            AutoSetupJunctionEdits edits = expected.EditsOf(junction);
            Assert.Equal(AutoSetupWizardReport.JunctionName(junction), wizard.JunctionCell(0, j).Text);
            Assert.Equal(edits.MinHz ?? AutoSetupWizardPlan.FieldHz(window.LowHz), wizard.MinHz(j).Value);
            Assert.Equal(edits.MaxHz ?? AutoSetupWizardPlan.FieldHz(window.HighHz), wizard.MaxHz(j).Value);
            Assert.Equal(
                edits.MinSlope ?? AutoSetupWizardPlan.NearestSlope(window.MinSlopeDbPerOctave),
                wizard.MinSlope(j).SelectedItem);
            Assert.Equal(
                edits.MaxSlope ?? AutoSetupWizardPlan.NearestSlope(window.MaxSlopeDbPerOctave),
                wizard.MaxSlope(j).SelectedItem);
            Assert.Equal(edits.Split, wizard.Split(j).Checked);
            Label notes = wizard.Notes(j);
            Assert.Equal(string.Join("   ·   ", window.Notes.Select(note => note.Summary)), notes.Text);
            Assert.Equal(window.Notes.Count > 0, notes.Visible);
        }

        foreach ((AutoSetupWizardJunction junction, string verdict)
                 in AutoSetupWizardReport.JunctionVerdicts(expected, preview.Fits))
        {
            Assert.Equal(verdict, wizard.JunctionCell(8, junction.IndexInGroup).Text);
        }

        Assert.Equal(
            string.Join(Environment.NewLine, AutoSetupWizardReport.PreviewLines(expected, preview)),
            wizard.Find<Label>("labelPreview").Text);
        var elevation = wizard.Find<ThemedNumericUpDown>("subElevation");
        Assert.Equal(expected.ElevationRange.Maximum, elevation.Maximum);
        Assert.Equal(expected.SubElevationDb, elevation.Value);
        Assert.Equal(expected.SubElevationApplies, elevation.Enabled);
        for (int line = 0; line < expected.Rows.Count; line++)
        {
            Assert.Equal(expected.Rows[line].Type, wizard.TypeBox(line).SelectedItem);
            Assert.Equal(AutoSetupWizardReport.BandText(expected.Rows[line].Source), wizard.ChannelCell(2, line).Text);
        }
    }

    /// <summary>A wizard on a single chain, so every table line is a row: no group headers.</summary>
    private sealed class Wizard : IDisposable
    {
        public Wizard(IReadOnlyList<AutoSetupWizardChannel> channels)
        {
            Dialog = new VirtualCrossoverAutoSetupDialog();
            Dialog.Init(SampleRate, SampleRate, channels);
            Dialog.Show();
            Settle();
        }

        public VirtualCrossoverAutoSetupDialog Dialog { get; }

        public T Find<T>(string name) where T : Control =>
            (T)Dialog.Controls.Find(name, searchAllChildren: true).Single();

        public Control ChannelCell(int column, int line) =>
            Find<TableLayoutPanel>("tableChannels").GetControlFromPosition(column, line)!;

        public ThemedComboBox TypeBox(int line) => (ThemedComboBox)ChannelCell(3, line);

        public Button Arrow(int line, bool up) => (Button)ChannelCell(up ? 4 : 5, line);

        public Control JunctionCell(int column, int junction) =>
            Find<TableLayoutPanel>("tableJunctions").GetControlFromPosition(column, 2 * junction)!;

        public ThemedNumericUpDown MinHz(int junction) => (ThemedNumericUpDown)JunctionCell(1, junction);

        public ThemedNumericUpDown MaxHz(int junction) => (ThemedNumericUpDown)JunctionCell(3, junction);

        public ThemedComboBox MinSlope(int junction) => (ThemedComboBox)JunctionCell(4, junction);

        public ThemedComboBox MaxSlope(int junction) => (ThemedComboBox)JunctionCell(6, junction);

        public CheckBox Split(int junction) => (CheckBox)JunctionCell(7, junction);

        // GetControlFromPosition skips a hidden control, and a row without notes hides its label.
        public Label Notes(int junction)
        {
            var table = Find<TableLayoutPanel>("tableJunctions");
            return table.Controls.OfType<Label>()
                .Single(label => table.GetRow(label) == (2 * junction) + 1 && table.GetColumn(label) == 0);
        }

        public void Settle()
        {
            StaTest.Settle(Dialog.PendingPreview);
            StaTest.Pump();
        }

        public CrossoverProposal[] Apply()
        {
            Assert.True(Find<Button>("buttonApply").Enabled, "Apply is off.");
            Find<Button>("buttonApply").PerformClick();
            WaitForResult();
            return [.. Dialog.Result!];
        }

        public void WaitForResult()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (Dialog.Result == null && DateTime.UtcNow < deadline)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }

            Assert.NotNull(Dialog.Result);
        }

        public void Dispose()
        {
            Dialog.Hide();
            Dialog.Dispose();
        }
    }
}
