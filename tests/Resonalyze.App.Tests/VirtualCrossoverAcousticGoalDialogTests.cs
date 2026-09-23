using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

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

        Assert.Equal(kept, dialog.HighPassGoal);
        Assert.Equal(stated, dialog.LowPassGoal);
    });

    [Fact]
    public void AFamilyKeepsTheSlopeItOffers_AndClearStatesNoGoal() => StaTest.Run(() =>
    {
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.BandPass,
            AcousticLowPass = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 36)
        };
        using var dialog = new VirtualCrossoverAcousticGoalDialog();
        dialog.Init(settings, "B");
        ThemedComboBox family = Field<ThemedComboBox>(dialog, "comboBoxLowPassFamily");

        family.SelectedItem = CrossoverFamilyChoice.Offered.First(choice => choice.Value == CrossoverFilterFamily.LinkwitzRiley);
        Assert.Equal(new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 36), dialog.LowPassGoal);

        Field<ThemedComboBox>(dialog, "comboBoxHighPassFamily").SelectedItem =
            CrossoverFamilyChoice.Offered.First(choice => choice.Value == CrossoverFilterFamily.Bessel);
        Assert.Equal(new JunctionAcousticTarget(CrossoverFilterFamily.Bessel, 12), dialog.HighPassGoal);

        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new(-5000, -5000);
        dialog.Show();
        Field<Button>(dialog, "buttonClear").PerformClick();
        Assert.Null(dialog.HighPassGoal);
        Assert.Null(dialog.LowPassGoal);
        Assert.False(Field<ThemedComboBox>(dialog, "comboBoxLowPassSlope").Enabled);
    });

    private static T Field<T>(Control root, string name) where T : Control =>
        (T)root.Controls.Find(name, searchAllChildren: true).Single();
}
