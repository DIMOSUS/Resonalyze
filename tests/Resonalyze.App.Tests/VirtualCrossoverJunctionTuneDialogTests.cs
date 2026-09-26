using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>The dialog shown and driven through its controls: each edit reaches the question, and the question comes back
/// into the controls.</summary>
public sealed class VirtualCrossoverJunctionTuneDialogTests
{
    private static readonly JunctionTuneOutcome Found =
        new([JunctionTuneLine.Of("Junction tune A/B: applied.")], CanApply: true, "A better crossover was found.", false);

    private static Func<int, JunctionTuneDefaults> Defaults => index => index == 0
        ? new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.LinkwitzRiley],
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24))
        : new JunctionTuneDefaults(500, 2_000, [CrossoverFilterFamily.Butterworth], null);

    private static VirtualCrossoverJunctionTuneDialog Shown(
        Func<JunctionTuneRequest, Task<JunctionTuneOutcome>> search, string? undoable = null, params string[] junctions)
    {
        var dialog = new VirtualCrossoverJunctionTuneDialog { StartPosition = FormStartPosition.Manual, Location = new(-5000, -5000) };
        dialog.Init(junctions.Length > 0 ? junctions : ["A-B", "B-C"], Defaults, search, undoable: undoable);
        dialog.Show();
        StaTest.Pump();
        return dialog;
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        (T)root.Controls.Find(name, searchAllChildren: true).Single();

    private static void Click(Form dialog, string name)
    {
        Find<Button>(dialog, name).PerformClick();
        StaTest.Pump();
    }

    [Fact]
    public void TheFieldsShowTheJunctionsOpening_AndASearchIsAskedWithWhatTheyState() => StaTest.Run(() =>
    {
        JunctionTuneRequest? asked = null;
        using VirtualCrossoverJunctionTuneDialog dialog = Shown(request =>
        {
            asked = request;
            return Task.FromResult(Found);
        });
        Button apply = Find<Button>(dialog, "buttonApply");

        Assert.Equal(80m, Find<ThemedNumericUpDown>(dialog, "numericMinHz").Value);
        Assert.True(Find<RadioButton>(dialog, "radioAcoustic").Checked);
        Assert.Equal(24, Find<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem);
        Assert.True(Find<ThemedComboBox>(dialog, "comboBoxGoalFamily").Enabled);
        Assert.False(Find<ThemedComboBox>(dialog, "comboBoxMinSlope").Enabled);
        Assert.Contains("Driver and filter together", Find<Label>(dialog, "labelGoalHint").Text);
        Assert.False(apply.Enabled);

        Find<ThemedComboBox>(dialog, "comboBoxJunction").SelectedIndex = 1;
        Find<CheckBox>(dialog, "checkBessel").Checked = true;
        Find<CheckBox>(dialog, "checkBoxSplitCorners").Checked = false;
        Find<ThemedNumericUpDown>(dialog, "numericSumBudget").Value = 0.6m;
        Find<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem = 48;
        Assert.Equal(500m, Find<ThemedNumericUpDown>(dialog, "numericMinHz").Value);
        Click(dialog, "buttonRun");

        JunctionTuneRequest request = Assert.IsType<JunctionTuneRequest>(asked);
        Assert.Equal(1, request.JunctionIndex);
        Assert.Equal((500, 2_000), (request.MinHz, request.MaxHz));
        Assert.Equal([CrossoverFilterFamily.LinkwitzRiley, CrossoverFilterFamily.Bessel], request.Families);
        Assert.False(request.SplitCorners);
        Assert.Equal(0.6, request.SumSlackDb);
        Assert.Equal(new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 48), request.AcousticGoal);
        Assert.Same(request, dialog.Result);
        Assert.True(apply.Enabled);
        Assert.Contains("applied", Find<RichTextBox>(dialog, "textBoxReport").Text);
        Assert.Equal(UiPalette.Warning, Find<Label>(dialog, "labelStatus").ForeColor);

        Find<RadioButton>(dialog, "radioSummation").Checked = true;
        Assert.Null(dialog.Result);
        Assert.False(apply.Enabled);
        Assert.True(Find<ThemedComboBox>(dialog, "comboBoxMinSlope").Enabled);
        Assert.False(Find<ThemedComboBox>(dialog, "comboBoxGoalSlope").Enabled);
        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.Again, Find<Label>(dialog, "labelStatus").Text);

        Find<ThemedComboBox>(dialog, "comboBoxMaxSlope").SelectedItem = 12;
        Find<ThemedComboBox>(dialog, "comboBoxMinSlope").SelectedItem = 24;
        Click(dialog, "buttonRun");
        Assert.Equal([12, 18, 24], asked!.Slopes);
        Assert.Null(asked.AcousticGoal);

        foreach (string family in new[] { "checkLinkwitzRiley", "checkBessel" })
        {
            Find<CheckBox>(dialog, family).Checked = false;
        }

        Click(dialog, "buttonRun");
        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.NoFamily, Find<Label>(dialog, "labelStatus").Text);
        Assert.Equal(UiPalette.Warning, Find<Label>(dialog, "labelStatus").ForeColor);
        VirtualCrossoverJunctionTuneSettings remembered = dialog.Remembered();
        Assert.Equal("B-C", remembered.Junction);
        Assert.Equal(12, remembered.MaxSlopeDbPerOctave);
    });

    [Fact]
    public void WhileTheSearchRuns_OnlyTheQuestionStaysOpen_AndAChangeDropsItsAnswer() => StaTest.Run(() =>
    {
        var searching = new TaskCompletionSource<JunctionTuneOutcome>();
        using VirtualCrossoverJunctionTuneDialog dialog = Shown(_ => searching.Task);

        Click(dialog, "buttonRun");
        Assert.False(Find<Button>(dialog, "buttonRun").Enabled);
        Assert.False(Find<Button>(dialog, "buttonCancel").Enabled);
        Assert.True(dialog.UseWaitCursor);
        Assert.Equal("Searching…", Find<Label>(dialog, "labelStatus").Text);
        dialog.Close();
        Assert.True(dialog.Visible);

        Find<ThemedNumericUpDown>(dialog, "numericMaxHz").Value = 150m;
        searching.SetResult(Found);
        StaTest.Pump();

        Assert.Null(dialog.Result);
        Assert.False(Find<Button>(dialog, "buttonApply").Enabled);
        Assert.True(Find<Button>(dialog, "buttonRun").Enabled);
        Assert.False(dialog.UseWaitCursor);
        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.Again, Find<Label>(dialog, "labelStatus").Text);
        Assert.DoesNotContain("applied", Find<RichTextBox>(dialog, "textBoxReport").Text);
    });

    [Fact]
    public void WithNoJunctionInView_ItSaysSoInsteadOfOfferingASearch() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        dialog.Init(
            [],
            _ => throw new InvalidOperationException("nothing to open"),
            _ => throw new InvalidOperationException("nothing should be searched"));

        Assert.False(Find<Button>(dialog, "buttonRun").Enabled);
        Assert.Contains("no junction", Find<Label>(dialog, "labelStatus").Text);
    });

    [Fact]
    public void UndoLastApply_IsOfferedOnlyWhileThereIsOne_AndClosesTheDialogToDoIt() => StaTest.Run(() =>
    {
        using VirtualCrossoverJunctionTuneDialog nothing = Shown(_ => Task.FromResult(Found));
        Assert.False(Find<Button>(nothing, "buttonUndo").Enabled);

        using VirtualCrossoverJunctionTuneDialog dialog = Shown(_ => Task.FromResult(Found), undoable: "A/B");
        Assert.True(Find<Button>(dialog, "buttonUndo").Enabled);

        Click(dialog, "buttonUndo");

        Assert.True(dialog.UndoRequested);
        Assert.NotEqual(DialogResult.OK, dialog.DialogResult);
    });

    [Fact]
    public void WhileTheSearchRuns_UndoIsOff_SinceTheDialogCannotCloseToDoIt() => StaTest.Run(() =>
    {
        var searching = new TaskCompletionSource<JunctionTuneOutcome>();
        using VirtualCrossoverJunctionTuneDialog dialog = Shown(_ => searching.Task, undoable: "A/B");

        Click(dialog, "buttonRun");

        Assert.False(Find<Button>(dialog, "buttonUndo").Enabled);
        searching.SetResult(Found);
        StaTest.Pump();
        Assert.True(Find<Button>(dialog, "buttonUndo").Enabled);
    });

    [Fact]
    public void TheActionButtonsStayVisibleAtEveryHeight() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        AssertNothingCoversTheActionButtons(dialog);

        dialog.Size = dialog.Size with { Height = 1 };
        Assert.Equal(dialog.MinimumSize.Height, dialog.Height);
        AssertNothingCoversTheActionButtons(dialog);

        dialog.ClientSize = dialog.ClientSize with { Height = 1_100 };
        AssertNothingCoversTheActionButtons(dialog);
        foreach (string name in new[] { "checkButterworth", "checkLinkwitzRiley", "checkBessel" })
        {
            CheckBox box = Find<CheckBox>(dialog, name);
            Assert.True(box.Bottom <= dialog.ClientSize.Height, $"{name} {box.Bounds} falls outside {dialog.ClientSize}.");
        }
    });

    private static void AssertNothingCoversTheActionButtons(Form dialog)
    {
        foreach (string name in new[] { "buttonApply", "buttonCancel", "buttonRun", "buttonUndo" })
        {
            Button button = Find<Button>(dialog, name);
            Assert.True(
                button.Top >= 0 && button.Bottom <= dialog.ClientSize.Height,
                $"{name} is outside the client area: {button.Bounds} in {dialog.ClientSize}.");
            int index = dialog.Controls.GetChildIndex(button);
            foreach (Control sibling in dialog.Controls)
            {
                if (ReferenceEquals(sibling, button) ||
                    dialog.Controls.GetChildIndex(sibling) > index)
                {
                    continue;
                }

                Assert.False(
                    sibling.Bounds.IntersectsWith(button.Bounds),
                    $"{sibling.Name} {sibling.Bounds} covers {name} {button.Bounds}.");
            }
        }
    }
}
