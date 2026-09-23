using System.Globalization;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The dialog binds its fields to a <see cref="DspProcessorSession"/>: each pick reaches the choice and the choice
/// comes back into the fields.</summary>
public sealed class DspProcessorDialogTests
{
    private static DspProcessorSession Choice(int measurementRateHz = 48_000, DspProcessorProfile? profile = null) =>
        new(
            profile ?? DspProcessorProfile.Custom(48_000, PeqQConvention.Rbj),
            followsMeasurements: true,
            measurementRateHz,
            phaseControl: null,
            firFilters: null);

    private static T Find<T>(Control root, string name) where T : Control =>
        (T)root.Controls.Find(name, searchAllChildren: true).Single();

    private static void Pick(Form dialog, string name, Func<object, bool> match)
    {
        ThemedComboBox combo = Find<ThemedComboBox>(dialog, name);
        combo.SelectedItem = combo.Items.Cast<object>().First(match);
    }

    [Fact]
    public void EachPickReachesTheChoice_AndTheChoiceComesBackIntoTheFields() => StaTest.Run(() =>
    {
        DspProcessorSession choice = Choice();
        using var dialog = new DspProcessorDialog(choice);
        ThemedComboBox rate = Find<ThemedComboBox>(dialog, "comboBoxSampleRate");
        Label status = Find<Label>(dialog, "labelStatus");

        Assert.Equal("Follow measurements (48 kHz)", rate.GetItemText(rate.SelectedItem));
        Assert.True(rate.Enabled);
        Assert.Equal(DspProcessorStatus.Text(choice), status.Text);

        Pick(dialog, "comboBoxSampleRate", item => item is 96_000);
        Pick(dialog, "comboBoxQConvention", item => item is PeqQConvention.Symmetric);
        Assert.Equal(96_000, choice.SampleRate);
        Assert.Equal(PeqQConvention.Symmetric, choice.QConvention);
        Assert.Equal(DspProcessorStatus.Text(choice), status.Text);

        DspProcessorPreset helix = DspProcessorCatalog.Preset("helix-dsp-ultra-s")!;
        Pick(dialog, "comboBoxModel", item => ReferenceEquals(item, helix));
        Assert.Same(helix, choice.Model);
        Assert.False(rate.Enabled);
        Assert.False(Find<ThemedComboBox>(dialog, "comboBoxQConvention").Enabled);
        Assert.Equal(helix.SampleRateHz, rate.SelectedItem);
        Assert.True(Find<CheckBox>(dialog, "checkBoxPhaseControl").Checked);

        Find<CheckBox>(dialog, "checkBoxPhaseControl").Checked = false;
        Find<CheckBox>(dialog, "checkBoxFirFilters").Checked = true;
        Assert.False(choice.PhaseControl);
        Assert.True(choice.FirFilters);

        Pick(dialog, "comboBoxModel", item => item is not DspProcessorPreset);
        Assert.Null(choice.Model);
        Assert.Equal(96_000, rate.SelectedItem);
        ThemedComboBox model = Find<ThemedComboBox>(dialog, "comboBoxModel");
        Assert.Equal("Custom", model.GetItemText(model.SelectedItem));
    });

    [Fact]
    public void AnUnlistedRateIsOffered_AndTheFollowEntryNamesWhatItFollows() => StaTest.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var dialog = new DspProcessorDialog(Choice(50_000, DspProcessorProfile.Custom(22_050, PeqQConvention.Rbj)));
        ThemedComboBox rate = Find<ThemedComboBox>(dialog, "comboBoxSampleRate");
        List<string> offered = rate.Items.Cast<object>().Select(rate.GetItemText).ToList();

        Assert.Equal("Follow measurements (50 kHz)", offered[0]);
        Assert.Contains("22.05 kHz", offered);
        Assert.Contains("50 kHz", offered);

        using var none = new DspProcessorDialog(Choice(0));
        ThemedComboBox follow = Find<ThemedComboBox>(none, "comboBoxSampleRate");
        Assert.Equal("Follow measurements", follow.GetItemText(follow.Items[0]));
    });

    [Fact]
    public void Notes_RoundTripThroughTheField_AndBlankReadsAsNone() => StaTest.Run(() =>
    {
        DspProcessorSession choice = Choice();
        choice.Notes = "2019 Passat B8, LHD.";
        using var dialog = new DspProcessorDialog(choice);
        TextBox notes = Find<TextBox>(dialog, "textBoxNotes");
        Assert.Equal("2019 Passat B8, LHD.", notes.Text);

        notes.Text = "Tweeters in the A-pillars.";
        Assert.Equal("Tweeters in the A-pillars.", choice.Notes);

        notes.Text = "   \r\n";
        Assert.Null(choice.Notes);
    });

    [Fact]
    public void Notes_FieldIsBoundedAndLaidOutInsideTheDialog() => StaTest.Run(() =>
    {
        // The field is the tallest control, so it would push the buttons off if designer numbers slipped.
        using var dialog = new DspProcessorDialog(Choice());
        TextBox notes = Find<TextBox>(dialog, "textBoxNotes");
        Assert.True(notes.Multiline);
        Assert.Equal(DspProcessorSession.MaximumNotesLength, notes.MaxLength);

        Button ok = Find<Button>(dialog, "buttonOk");
        Assert.True(notes.Top > 0);
        Assert.True(ok.Top >= notes.Bottom);
        Assert.True(ok.Bottom <= dialog.ClientSize.Height);
    });
}
