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

        // The first junction opens on its own filters and its cards' wish; switching junction then moves the
        // corner window and leaves the rest as set.
        Field<ThemedComboBox>(dialog, "comboBoxJunction").SelectedIndex = 1;
        Assert.Equal(500m, Field<ThemedNumericUpDown>(dialog, "numericMinHz").Value);
        Assert.Equal(
            CrossoverFilterFamily.LinkwitzRiley,
            (Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").SelectedItem
                as CrossoverFamilyChoice)?.Value);
        Assert.Equal(24, Field<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem);

        // The families the dialog opened on stay, and every one on offer is visible at once.
        Assert.True(Field<CheckBox>(dialog, "checkLinkwitzRiley").Checked);
        Assert.False(Field<CheckBox>(dialog, "checkButterworth").Checked);
        foreach (string name in new[] { "checkButterworth", "checkLinkwitzRiley", "checkBessel" })
        {
            CheckBox box = Field<CheckBox>(dialog, name);
            Assert.True(
                box.Bottom <= dialog.ClientSize.Height,
                $"{name} {box.Bounds} falls outside {dialog.ClientSize}.");
        }

        // No family ticked is a question with no candidates, and it does not reach the search.
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

        // Moving the corner window asks a different question, so the answer is retired.
        Field<ThemedNumericUpDown>(dialog, "numericMaxHz").Value = 1_500m;
        Assert.Null(dialog.Result);
        Assert.False(apply.Enabled);
    });

    [Fact]
    public void AQuestionChangedWhileTheSearchRan_TakesNoAnswerAtAll() => StaTest.Run(() =>
    {
        // The boxes stay live during a search, so the user can retune the question while the sum is being
        // measured. The answer then belongs to a question nobody is asking any more: neither the report nor
        // Apply may stand for it.
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

        // And the same question asked again does land.
        Run(dialog);
        Assert.NotNull(dialog.Result);
        Assert.True(apply.Enabled);
    });

    [Fact]
    public void SwitchingJunction_MovesOnlyTheCornerWindow_AndDropsTheOldReport() => StaTest.Run(() =>
    {
        // Reported from the field: switching junction reset settings the user had just made. Only the corner
        // window belongs to a junction; families, slopes, the mode and the goal are the question being asked.
        // Coming back finds the window as it was left, and the report of another junction is not left standing.
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
        // Asked for from the field: every opening started from scratch, even within one session.
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

        // A window set for a crossover that has since moved out of it is not the question any more: the
        // junction reopens on the default around where it is crossed now.
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

    /// <summary>A question unlike any default: the goal, families, both toggles and the slope window all set.</summary>
    private static void SetQuestion(VirtualCrossoverJunctionTuneDialog dialog)
    {
        Field<CheckBox>(dialog, "checkBessel").Checked = true;
        Field<CheckBox>(dialog, "checkBoxIndependentSlopes").Checked = true;
        Field<CheckBox>(dialog, "checkBoxSplitCorners").Checked = true;
        Field<ThemedComboBox>(dialog, "comboBoxMinSlope").SelectedItem = 18;
        Field<ThemedComboBox>(dialog, "comboBoxMaxSlope").SelectedItem = 36;
        Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").SelectedItem = CrossoverFamilyChoice.Offered
            .First(choice => choice.Value == CrossoverFilterFamily.Butterworth);
        Field<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem = 30;
        Field<RadioButton>(dialog, "radioAcoustic").Checked = true;
    }

    private static void AssertQuestion(VirtualCrossoverJunctionTuneDialog dialog)
    {
        Assert.True(Field<CheckBox>(dialog, "checkBessel").Checked);
        Assert.True(Field<CheckBox>(dialog, "checkBoxIndependentSlopes").Checked);
        Assert.True(Field<CheckBox>(dialog, "checkBoxSplitCorners").Checked);
        Assert.Equal(18, Field<ThemedComboBox>(dialog, "comboBoxMinSlope").SelectedItem);
        Assert.Equal(36, Field<ThemedComboBox>(dialog, "comboBoxMaxSlope").SelectedItem);
        Assert.Equal(
            CrossoverFilterFamily.Butterworth,
            (Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").SelectedItem as CrossoverFamilyChoice)?.Value);
        Assert.Equal(30, Field<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem);
        Assert.True(Field<RadioButton>(dialog, "radioAcoustic").Checked);
    }
    [Fact]
    public void TheModeSaysWhatIsBeingTunedFor_AndOnlyTheAcousticOneCarriesAGoal() => StaTest.Run(() =>
    {
        // Without the switch the dialog never said what it optimised, and a run with the goal boxes left alone was
        // a search for nothing in particular.
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
        Assert.Contains("sums best", hint.Text, StringComparison.Ordinal);
        Run(dialog);
        Assert.Null(asked!.AcousticGoal);
        Assert.False(asked.SplitCorners);

        // Switching mode states a goal rather than leaving the boxes empty, and says so in the hint.
        acoustic.Checked = true;
        Assert.True(Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").Enabled);
        Assert.Contains("Driver and filter together", hint.Text, StringComparison.Ordinal);
        Field<CheckBox>(dialog, "checkBoxSplitCorners").Checked = true;
        Run(dialog);

        Assert.NotNull(asked!.AcousticGoal);
        Assert.Equal(CrossoverFilterFamily.LinkwitzRiley, asked.AcousticGoal!.Family);
        Assert.True(asked.SplitCorners);
    });

    [Fact]
    public void TheSlopeWindowIsWhatTheSearchMayUse_AndDefaultsToTheWholeMenu() => StaTest.Run(() =>
    {
        // The engine takes a list of slopes and the dialog used to send none, so "12 to 48" was the only search
        // there was. Narrowing the window is the point of the field.
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
        // Narrowing the window is a different question, so the previous answer is retired.
        Assert.Null(dialog.Result);
        Run(dialog);

        Assert.Equal([24, 30, 36], asked!.Slopes);

        // The window belongs to the summation mode: an acoustic goal states what the answer must come to, so tying
        // the electrical slopes down as well would only take filters away from the search.
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
        foreach (string name in new[] { "buttonApply", "buttonCancel", "buttonRun" })
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
