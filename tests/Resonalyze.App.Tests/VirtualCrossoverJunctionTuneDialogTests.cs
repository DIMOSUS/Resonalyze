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
                ? new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.Butterworth], null)
                : new JunctionTuneDefaults(
                    500,
                    2_000,
                    [CrossoverFilterFamily.LinkwitzRiley],
                    new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)),
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

        // The window opens on the junction's own defaults, and the second junction carries the card's wish.
        Field<ThemedComboBox>(dialog, "comboBoxJunction").SelectedIndex = 1;
        Assert.Equal(500m, Field<ThemedNumericUpDown>(dialog, "numericMinHz").Value);
        Assert.Equal(
            CrossoverFilterFamily.LinkwitzRiley,
            (Field<ThemedComboBox>(dialog, "comboBoxGoalFamily").SelectedItem
                as CrossoverFamilyChoice)?.Value);
        Assert.Equal(24, Field<ThemedComboBox>(dialog, "comboBoxGoalSlope").SelectedItem);

        // The junction opens on the families it already runs, and every one on offer is visible at once.
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

    private static void Run(VirtualCrossoverJunctionTuneDialog dialog)
    {
        var task = (Task)typeof(VirtualCrossoverJunctionTuneDialog)
            .GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, null)!;
        task.GetAwaiter().GetResult();
    }

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
