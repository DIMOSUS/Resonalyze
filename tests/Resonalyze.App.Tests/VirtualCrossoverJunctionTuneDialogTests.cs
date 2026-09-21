using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverJunctionTuneDialogTests
{
    [Fact]
    public void ApplyStandsForASearchThatRan_AndAChangedQuestionRetiresIt() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        int searches = 0;
        dialog.Init(
            ["A-B", "B-C"],
            index => index == 0
                ? new JunctionTuneDefaults(
                    80,
                    200,
                    [CrossoverFilterFamily.LinkwitzRiley],
                    new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24))
                : new JunctionTuneDefaults(500, 2_000, [CrossoverFilterFamily.Butterworth], null),
            request =>
            {
                searches++;
                return Task.FromResult(new JunctionTuneOutcome(
                    [JunctionTuneLine.Of("Junction tune A/B: applied.")],
                    CanApply: true,
                    "A better crossover was found.",
                    false));
            });
        Button apply = Field<Button>(dialog, "buttonApply");
        Assert.False(apply.Enabled);
        Assert.Null(dialog.Result);

        Field<ThemedComboBox>(dialog, "comboBoxJunction").SelectedIndex = 1;
        Assert.Equal(500m, Field<ThemedNumericUpDown>(dialog, "numericMinHz").Value);
        Assert.Equal(
            CrossoverFilterFamily.LinkwitzRiley,
            (Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").SelectedItem
                as CrossoverFamilyChoice)?.Value);
        Assert.Equal(24, Field<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem);

        Assert.True(Field<CheckBox>(dialog, "checkLinkwitzRiley").Checked);
        Assert.False(Field<CheckBox>(dialog, "checkButterworth").Checked);
        foreach (string name in new[] { "checkButterworth", "checkLinkwitzRiley", "checkBessel" })
        {
            CheckBox box = Field<CheckBox>(dialog, name);
            Assert.True(
                box.Bottom <= dialog.ClientSize.Height,
                $"{name} {box.Bounds} falls outside {dialog.ClientSize}.");
        }

        Field<CheckBox>(dialog, "checkLinkwitzRiley").Checked = false;
        Run(dialog);
        Assert.Equal(0, searches);
        Assert.False(apply.Enabled);
        Assert.Contains("family", Field<Label>(dialog, "labelStatus").Text);

        Field<CheckBox>(dialog, "checkLinkwitzRiley").Checked = true;
        Run(dialog);

        Assert.Equal(1, searches);
        Assert.True(apply.Enabled);
        JunctionTuneRequest asked = Assert.IsType<JunctionTuneRequest>(dialog.Result);
        Assert.Equal(1, asked.JunctionIndex);
        Assert.Equal(new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24), asked.AcousticGoal);
        Assert.Contains("applied", Field<RichTextBox>(dialog, "textBoxReport").Text);

        Field<ThemedNumericUpDown>(dialog, "numericMaxHz").Value = 1_500m;
        Assert.Null(dialog.Result);
        Assert.False(apply.Enabled);
    });

    [Fact]
    public void AQuestionChangedWhileTheSearchRan_TakesNoAnswerAtAll() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        var searching = new TaskCompletionSource<JunctionTuneOutcome>();
        dialog.Init(
            ["A-B"],
            _ => new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.Butterworth], null),
            _ => searching.Task);
        Button apply = Field<Button>(dialog, "buttonApply");

        Task run = Start(dialog);
        Field<ThemedNumericUpDown>(dialog, "numericMaxHz").Value = 150m;
        searching.SetResult(new JunctionTuneOutcome(
            [JunctionTuneLine.Of("Junction tune A/B: applied.")],
            CanApply: true,
            "A better crossover was found.",
            false));
        StaTest.Settle(run);

        Assert.Null(dialog.Result);
        Assert.False(apply.Enabled);
        Assert.Contains("changed", Field<Label>(dialog, "labelStatus").Text);
        Assert.DoesNotContain("applied", Field<RichTextBox>(dialog, "textBoxReport").Text);

        Run(dialog);
        Assert.NotNull(dialog.Result);
        Assert.True(apply.Enabled);
    });

    [Fact]
    public void SwitchingJunction_MovesOnlyTheCornerWindow_AndDropsTheOldReport() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        dialog.Init(
            ["A-B", "B-C"],
            index => index == 0
                ? new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.Butterworth], null)
                : new JunctionTuneDefaults(
                    500,
                    2_000,
                    [CrossoverFilterFamily.LinkwitzRiley],
                    new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)),
            _ => Task.FromResult(new JunctionTuneOutcome(
                [JunctionTuneLine.Of("A/B report")], CanApply: true, "found", false)));
        var junction = Field<ThemedComboBox>(dialog, "comboBoxJunction");
        SetQuestion(dialog);
        Field<ThemedNumericUpDown>(dialog, "numericMinHz").Value = 90m;
        Field<ThemedNumericUpDown>(dialog, "numericMaxHz").Value = 190m;
        Run(dialog);
        Assert.Contains("A/B report", Field<RichTextBox>(dialog, "textBoxReport").Text);

        junction.SelectedIndex = 1;

        Assert.Equal(500m, Field<ThemedNumericUpDown>(dialog, "numericMinHz").Value);
        Assert.Equal(2_000m, Field<ThemedNumericUpDown>(dialog, "numericMaxHz").Value);
        AssertQuestion(dialog);
        Assert.DoesNotContain("A/B report", Field<RichTextBox>(dialog, "textBoxReport").Text);
        Assert.Null(dialog.Result);

        junction.SelectedIndex = 0;

        Assert.Equal(90m, Field<ThemedNumericUpDown>(dialog, "numericMinHz").Value);
        Assert.Equal(190m, Field<ThemedNumericUpDown>(dialog, "numericMaxHz").Value);
        AssertQuestion(dialog);
    });

    [Fact]
    public void ReopeningFindsTheDialogAsItWasLeft() => StaTest.Run(() =>
    {
        Func<int, JunctionTuneDefaults> defaults = index => index == 0
            ? new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.Butterworth], null, CornerHz: 125)
            : new JunctionTuneDefaults(500, 2_000, [CrossoverFilterFamily.LinkwitzRiley], null, CornerHz: 1_000);
        Func<JunctionTuneRequest, Task<JunctionTuneOutcome>> search =
            _ => Task.FromResult(new JunctionTuneOutcome([], false, "kept", false));
        VirtualCrossoverJunctionTuneSettings left;
        using (var first = new VirtualCrossoverJunctionTuneDialog())
        {
            first.Init(["A-B", "B-C"], defaults, search);
            Field<ThemedComboBox>(first, "comboBoxJunction").SelectedIndex = 1;
            SetQuestion(first);
            Field<ThemedNumericUpDown>(first, "numericMinHz").Value = 700m;
            Field<ThemedNumericUpDown>(first, "numericMaxHz").Value = 1_400m;
            left = first.Remembered();
        }

        using var second = new VirtualCrossoverJunctionTuneDialog();
        second.Init(["A-B", "B-C"], defaults, search, left);

        Assert.Equal(1, Field<ThemedComboBox>(second, "comboBoxJunction").SelectedIndex);
        Assert.Equal(700m, Field<ThemedNumericUpDown>(second, "numericMinHz").Value);
        Assert.Equal(1_400m, Field<ThemedNumericUpDown>(second, "numericMaxHz").Value);
        AssertQuestion(second);

        using var moved = new VirtualCrossoverJunctionTuneDialog();
        moved.Init(
            ["A-B", "B-C"],
            index => index == 0
                ? defaults(0)
                : new JunctionTuneDefaults(
                    2_000, 4_000, [CrossoverFilterFamily.LinkwitzRiley], null, CornerHz: 2_800),
            search,
            left);
        Assert.Equal(2_000m, Field<ThemedNumericUpDown>(moved, "numericMinHz").Value);
        AssertQuestion(moved);
    });

    private static void SetQuestion(VirtualCrossoverJunctionTuneDialog dialog)
    {
        Field<CheckBox>(dialog, "checkBessel").Checked = true;
        Field<CheckBox>(dialog, "checkBoxIndependentSlopes").Checked = false;
        Field<CheckBox>(dialog, "checkBoxSplitCorners").Checked = false;
        Field<ThemedComboBox>(dialog, "comboBoxMinSlope").SelectedItem = 18;
        Field<ThemedComboBox>(dialog, "comboBoxMaxSlope").SelectedItem = 36;
        Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").SelectedItem = CrossoverFamilyChoice.Offered
            .First(choice => choice.Value == CrossoverFilterFamily.Butterworth);
        Field<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem = 30;
        Field<RadioButton>(dialog, "radioAcoustic").Checked = true;
        Field<ThemedNumericUpDown>(dialog, "numericSumBudget").Value = 0.6m;
    }

    private static void AssertQuestion(VirtualCrossoverJunctionTuneDialog dialog)
    {
        Assert.True(Field<CheckBox>(dialog, "checkBessel").Checked);
        Assert.False(Field<CheckBox>(dialog, "checkBoxIndependentSlopes").Checked);
        Assert.False(Field<CheckBox>(dialog, "checkBoxSplitCorners").Checked);
        Assert.Equal(18, Field<ThemedComboBox>(dialog, "comboBoxMinSlope").SelectedItem);
        Assert.Equal(36, Field<ThemedComboBox>(dialog, "comboBoxMaxSlope").SelectedItem);
        Assert.Equal(
            CrossoverFilterFamily.Butterworth,
            (Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").SelectedItem as CrossoverFamilyChoice)?.Value);
        Assert.Equal(30, Field<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem);
        Assert.True(Field<RadioButton>(dialog, "radioAcoustic").Checked);
        Assert.Equal(0.6m, Field<ThemedNumericUpDown>(dialog, "numericSumBudget").Value);
    }
    [Fact]
    public void TheModeSaysWhatIsBeingTunedFor_AndOnlyTheAcousticOneCarriesAGoal() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        JunctionTuneRequest? asked = null;
        dialog.Init(
            ["A-B"],
            _ => new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.LinkwitzRiley], null),
            request =>
            {
                asked = request;
                return Task.FromResult(new JunctionTuneOutcome(
                    [JunctionTuneLine.Of("read")], CanApply: false, "kept", false));
            });
        RadioButton summation = Field<RadioButton>(dialog, "radioSummation");
        RadioButton acoustic = Field<RadioButton>(dialog, "radioAcoustic");
        Label hint = Field<Label>(dialog, "labelGoalHint");

        Assert.True(summation.Checked);
        Assert.False(Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").Enabled);
        ThemedNumericUpDown budget = Field<ThemedNumericUpDown>(dialog, "numericSumBudget");
        Assert.False(budget.Enabled);
        Assert.Contains("sums best", hint.Text, StringComparison.Ordinal);
        Run(dialog);
        Assert.Null(asked!.AcousticGoal);
        Assert.True(asked.IndependentSlopes);
        Assert.True(asked.SplitCorners);

        acoustic.Checked = true;
        Assert.True(Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").Enabled);
        Assert.Contains("Driver and filter together", hint.Text, StringComparison.Ordinal);
        Assert.True(budget.Enabled);
        Assert.Equal(1.0m, budget.Value);
        Field<CheckBox>(dialog, "checkBoxSplitCorners").Checked = false;
        Run(dialog);

        Assert.NotNull(asked!.AcousticGoal);
        Assert.Equal(CrossoverFilterFamily.LinkwitzRiley, asked.AcousticGoal!.Family);
        Assert.False(asked.SplitCorners);
        Assert.Equal(VirtualCrossoverJunctionTuneDialog.DefaultSumBudgetDb, asked.SumSlackDb);
    });

    [Fact]
    public void TheSlopeWindowIsWhatTheSearchMayUse_AndDefaultsToTheWholeMenu() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        JunctionTuneRequest? asked = null;
        dialog.Init(
            ["A-B"],
            _ => new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.LinkwitzRiley], null),
            request =>
            {
                asked = request;
                return Task.FromResult(new JunctionTuneOutcome([], false, "kept", false));
            });

        Run(dialog);
        Assert.Equal([12, 18, 24, 30, 36, 42, 48], asked!.Slopes);

        Field<ThemedComboBox>(dialog, "comboBoxMinSlope").SelectedItem = 24;
        Field<ThemedComboBox>(dialog, "comboBoxMaxSlope").SelectedItem = 36;
        Assert.Null(dialog.Result);
        Run(dialog);

        Assert.Equal([24, 30, 36], asked!.Slopes);

        Field<RadioButton>(dialog, "radioAcoustic").Checked = true;
        Assert.False(Field<ThemedComboBox>(dialog, "comboBoxMinSlope").Enabled);
        Assert.False(Field<ThemedComboBox>(dialog, "comboBoxMaxSlope").Enabled);
        Run(dialog);

        Assert.Empty(asked!.Slopes);
    });

    [Fact]
    public void AJunctionWhoseCardsAlreadyStateAGoal_OpensOnIt() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        dialog.Init(
            ["A-B"],
            _ => new JunctionTuneDefaults(
                80,
                200,
                [CrossoverFilterFamily.Butterworth],
                new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18)),
            _ => Task.FromResult(new JunctionTuneOutcome([], false, "kept", false)));

        Assert.True(Field<RadioButton>(dialog, "radioAcoustic").Checked);
        Assert.Equal(
            CrossoverFilterFamily.Butterworth,
            (Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").SelectedItem as CrossoverFamilyChoice)?.Value);
        Assert.Equal(18, Field<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem);
    });

    [Fact]
    public void WithNoJunctionInView_ItSaysSoInsteadOfOfferingASearch() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        dialog.Init(
            [],
            _ => new JunctionTuneDefaults(20, 20_000, [CrossoverFilterFamily.LinkwitzRiley], null),
            _ => throw new InvalidOperationException("nothing should be searched"));

        Assert.False(Field<Button>(dialog, "buttonRun").Enabled);
        Assert.Contains("no junction", Field<Label>(dialog, "labelStatus").Text);
    });

    [Fact]
    public void AWindowNoDecimalCanHold_OpensClampedRatherThanThrowing() => StaTest.Run(() =>
    {
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        var remembered = new VirtualCrossoverJunctionTuneSettings
        {
            Junction = "A-B",
            Windows = { ["A-B"] = [1e100, 2e100] }
        };

        dialog.Init(
            ["A-B"],
            _ => new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.LinkwitzRiley], null),
            _ => Task.FromResult(new JunctionTuneOutcome([], false, "kept", false)),
            remembered);

        ThemedNumericUpDown max = Field<ThemedNumericUpDown>(dialog, "numericMaxHz");
        Assert.Equal(max.Maximum, max.Value);
    });

    [Fact]
    public void UndoLastApply_IsOfferedOnlyWhileThereIsOne_AndClosesTheDialogToDoIt() => StaTest.Run(() =>
    {
        using var nothing = new VirtualCrossoverJunctionTuneDialog();
        nothing.Init(
            ["A-B"],
            _ => new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.LinkwitzRiley], null),
            _ => Task.FromResult(new JunctionTuneOutcome([], false, "kept", false)));
        Assert.False(Field<Button>(nothing, "buttonUndo").Enabled);

        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        dialog.Init(
            ["A-B"],
            _ => new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.LinkwitzRiley], null),
            _ => Task.FromResult(new JunctionTuneOutcome([], false, "kept", false)),
            undoable: "A/B");
        Button undo = Field<Button>(dialog, "buttonUndo");
        Assert.True(undo.Enabled);

        // PerformClick refuses on a never-shown form, so the click is raised as the framework does.
        typeof(Control)
            .GetMethod("InvokeOnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [undo, EventArgs.Empty]);

        Assert.True(dialog.UndoRequested);
        Assert.NotEqual(DialogResult.OK, dialog.DialogResult);
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
    });

    private static void Run(VirtualCrossoverJunctionTuneDialog dialog) =>
        Start(dialog).GetAwaiter().GetResult();

    /// <summary>Starts the search without waiting for it, for a question that changes mid-flight.</summary>
    private static Task Start(VirtualCrossoverJunctionTuneDialog dialog) =>
        (Task)typeof(VirtualCrossoverJunctionTuneDialog)
            .GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, null)!;

    private static void AssertNothingCoversTheActionButtons(Form dialog)
    {
        foreach (string name in new[] { "buttonApply", "buttonCancel", "buttonRun", "buttonUndo" })
        {
            Button button = Field<Button>(dialog, name);
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

    private static T Field<T>(object target, string name) where T : class =>
        (T)target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(target)!;
}
