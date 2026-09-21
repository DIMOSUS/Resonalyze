using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The card's goal editor states a goal only for an edge the channel runs: that is the one Auto Tune reads.
/// See docs/specs/acoustic-crossover-target.md.</summary>
public sealed class VirtualCrossoverAcousticGoalDialogTests
{
    [Fact]
    public void AnEdgeTheChannelDoesNotRun_IsGreyed_AndAGoalKeptThereSurvivesOk() => StaTest.Run(() =>
    {
        var kept = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18);
        var stated = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24);
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            AcousticHighPass = kept,
            AcousticLowPass = stated
        };
        using var dialog = new VirtualCrossoverAcousticGoalDialog();

        dialog.Init(settings, "A");

        Assert.False(Field<ThemedComboBox>(dialog, "comboBoxHighPassFamily").Enabled);
        Assert.False(Field<ThemedComboBox>(dialog, "comboBoxHighPassSlope").Enabled);
        Assert.Contains("kept", Field<Label>(dialog, "labelHighPassElectrical").Text, StringComparison.Ordinal);
        Assert.True(Field<ThemedComboBox>(dialog, "comboBoxLowPassFamily").Enabled);
        Assert.True(Field<ThemedComboBox>(dialog, "comboBoxLowPassSlope").Enabled);

        // OK neither drops the kept goal nor lets it be edited as if it worked: it comes back as it was.
        dialog.DialogResult = DialogResult.OK;
        typeof(VirtualCrossoverAcousticGoalDialog)
            .GetMethod("OnFormClosing", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog, [new FormClosingEventArgs(CloseReason.UserClosing, false)]);
        Assert.Equal(kept, dialog.HighPassGoal);
        Assert.Equal(stated, dialog.LowPassGoal);
    });

    private static T Field<T>(object target, string name) where T : class =>
        (T)target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(target)!;
}
