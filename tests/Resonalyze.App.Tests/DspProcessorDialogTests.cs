using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class DspProcessorDialogTests
{
    private const int MeasurementRate = 48_000;

    [Fact]
    public void LookingAtAPreset_DoesNotForgetAStatedRate()
    {
        StaTest.Run(() =>
        {
            using Form dialog = Open(followsMeasurements: true);
            SelectRate(dialog, 96_000);
            SelectModel(dialog, DspProcessorCatalog.Preset("helix-next-v-eight-dsp-ultimate")!);
            SelectCustom(dialog);

            Assert.False(Follows(dialog));
            Assert.Equal(96_000, Profile(dialog).SampleRateHz);
        });
    }

    [Fact]
    public void LookingAtAPreset_DoesNotForgetTheFollowChoice()
    {
        StaTest.Run(() =>
        {
            using Form dialog = Open(followsMeasurements: false);
            SelectFollow(dialog);
            SelectModel(dialog, DspProcessorCatalog.Preset("amp-panacea-v1-v2")!);
            SelectCustom(dialog);

            Assert.True(Follows(dialog));
            Assert.Equal(MeasurementRate, Profile(dialog).SampleRateHz);
        });
    }

    [Fact]
    public void ANamedModel_NeverFollows()
    {
        StaTest.Run(() =>
        {
            using Form dialog = Open(followsMeasurements: true);
            SelectModel(dialog, DspProcessorCatalog.Preset("helix-dsp-ultra-s")!);

            Assert.False(Follows(dialog));
            Assert.Equal(96_000, Profile(dialog).SampleRateHz);
            Assert.Equal(PeqQConvention.Rbj, Profile(dialog).QConvention);
        });
    }

    [Fact]
    public void WithoutAMeasurement_FollowingStillResolvesToAUsableRate()
    {
        StaTest.Run(() =>
        {
            using Form dialog = Open(followsMeasurements: true, measurementRateHz: 0);

            Assert.True(Follows(dialog));
            Assert.True(Profile(dialog).SampleRateHz > 0);
        });
    }

    [Fact]
    public void Notes_RoundTripThroughTheField_AndEmptyReadsAsNone()
    {
        StaTest.Run(() =>
        {
            // Null means no notes; blank must match, or every OK counts as an edit and schedules a save.
            using Form dialog = Open(followsMeasurements: true);
            Assert.Null(Notes(dialog));

            SetNotes(dialog, "2019 Passat B8, LHD.\r\nTweeters in the A-pillars.");
            Assert.Equal("2019 Passat B8, LHD.\r\nTweeters in the A-pillars.", Notes(dialog));

            NotesBox(dialog).Text = "   \r\n";
            Assert.Null(Notes(dialog));

            SetNotes(dialog, null);
            Assert.Equal(string.Empty, NotesBox(dialog).Text);
        });
    }

    [Fact]
    public void Notes_FieldIsBoundedAndLaidOutInsideTheDialog()
    {
        StaTest.Run(() =>
        {
            // The field is the tallest control, so it would push the buttons off if designer numbers slipped.
            using Form dialog = Open(followsMeasurements: true);
            TextBox notes = NotesBox(dialog);
            Assert.True(notes.Multiline);
            Assert.Equal(8_000, notes.MaxLength);

            Button ok = dialog.Controls.OfType<Button>().Single(button => button.Text == "OK");
            Assert.True(notes.Top > 0);
            Assert.True(ok.Top >= notes.Bottom);
            Assert.True(ok.Bottom <= dialog.ClientSize.Height);
        });
    }

    private static string? Notes(Form dialog) => (string?)Property(dialog, "Notes");

    private static void SetNotes(Form dialog, string? value) =>
        dialog.GetType()
            .GetProperty("Notes", BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(dialog, value);

    private static TextBox NotesBox(Form dialog) =>
        (TextBox)dialog.GetType()
            .GetField("textBoxNotes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    [Fact]
    public void NamingAnotherModel_LetsTheCatalogAnswerThePhaseQuestionAgain()
    {
        StaTest.Run(() =>
        {
            // A stored phase-control yes belongs to its device; carried over it would keep rotations on hardware without the control.
            using Form dialog = Open(followsMeasurements: false, phaseControl: true);
            Assert.True(PhaseControl(dialog));

            SelectModel(dialog, DspProcessorCatalog.Preset("amp-panacea-v1-v2")!);

            Assert.False(PhaseControl(dialog));

            SelectModel(dialog, DspProcessorCatalog.Preset("helix-dsp-ultra-s")!);

            Assert.True(PhaseControl(dialog));
        });
    }

    [Fact]
    public void TheStoredPhaseAnswer_SurvivesWhileTheModelDoes()
    {
        StaTest.Run(() =>
        {
            using Form dialog = Open(followsMeasurements: false, phaseControl: true);
            SelectRate(dialog, 96_000);

            Assert.True(PhaseControl(dialog));

            using Form off = Open(followsMeasurements: false, phaseControl: false);

            Assert.False(PhaseControl(off));
        });
    }

    [Fact]
    public void APhaseAnswerGivenForThisModel_IsNotUndoneByLookingAtTheFields()
    {
        StaTest.Run(() =>
        {
            using Form dialog = Open(followsMeasurements: false, phaseControl: null);
            SelectModel(dialog, DspProcessorCatalog.Preset("helix-dsp-ultra-s")!);
            Assert.True(PhaseControl(dialog));

            SetPhaseControl(dialog, false);
            SelectRate(dialog, 96_000);

            Assert.False(PhaseControl(dialog));
        });
    }

    [Fact]
    public void TheFirTick_IsOffUntilGiven_AndNotTakenAwayByNamingAModel()
    {
        StaTest.Run(() =>
        {
            // The catalog's FIR false means "not known", and an untick detaches every kernel, so browsing models keeps the user's tick.
            using Form dialog = Open(followsMeasurements: false, firFilters: null);
            Assert.False(FirFilters(dialog));

            SetFirFilters(dialog, true);
            Assert.True(FirFilters(dialog));

            SelectModel(dialog, DspProcessorCatalog.Preset("helix-dsp-ultra-s")!);
            Assert.True(FirFilters(dialog));

            SelectModel(dialog, DspProcessorCatalog.Preset("amp-panacea-v1-v2")!);
            Assert.True(FirFilters(dialog));

            SelectCustom(dialog);
            Assert.True(FirFilters(dialog));
        });
    }

    [Fact]
    public void TheStoredFirAnswer_SurvivesTheModelList_AndOnlyTheUserUnticksIt()
    {
        StaTest.Run(() =>
        {
            using Form dialog = Open(followsMeasurements: false, firFilters: true);
            SelectRate(dialog, 96_000);
            Assert.True(FirFilters(dialog));

            SelectModel(dialog, DspProcessorCatalog.Preset("amp-panacea-v1-v2")!);
            Assert.True(FirFilters(dialog));

            SetFirFilters(dialog, false);
            SelectModel(dialog, DspProcessorCatalog.Preset("helix-dsp-ultra-s")!);
            Assert.False(FirFilters(dialog));

            using Form off = Open(followsMeasurements: false, firFilters: false);
            Assert.False(FirFilters(off));
        });
    }

    private static bool FirFilters(Form dialog) => (bool)Property(dialog, "FirFilters")!;

    private static void SetFirFilters(Form dialog, bool value) =>
        ((CheckBox)dialog.GetType()
            .GetField("checkBoxFirFilters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!).Checked = value;

    private static bool PhaseControl(Form dialog) => (bool)Property(dialog, "PhaseControl")!;

    private static void SetPhaseControl(Form dialog, bool value) =>
        ((CheckBox)dialog.GetType()
            .GetField("checkBoxPhaseControl", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!).Checked = value;

    private static Form Open(
        bool followsMeasurements,
        int measurementRateHz = MeasurementRate,
        bool? phaseControl = null,
        bool? firFilters = null)
    {
        Type type = typeof(VirtualCrossoverPanel).Assembly
            .GetType("Resonalyze.DspProcessorDialog")!;
        return (Form)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            [
                DspProcessorProfile.Custom(measurementRateHz > 0 ? measurementRateHz : 48_000,
                    PeqQConvention.Rbj),
                followsMeasurements,
                measurementRateHz,
                phaseControl,
                firFilters
            ],
            culture: null)!;
    }

    private static DspProcessorProfile Profile(Form dialog) =>
        (DspProcessorProfile)Property(dialog, "Profile")!;

    private static bool Follows(Form dialog) =>
        (bool)Property(dialog, "FollowsMeasurements")!;

    private static object? Property(Form dialog, string name) =>
        dialog.GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(dialog);

    private static void SelectRate(Form dialog, int rateHz) =>
        Select(dialog, "comboBoxSampleRate", item => item is int rate && rate == rateHz);

    private static void SelectFollow(Form dialog) =>
        Select(dialog, "comboBoxSampleRate", item => item is not int);

    private static void SelectModel(Form dialog, DspProcessorPreset preset) =>
        Select(dialog, "comboBoxModel", item => ReferenceEquals(item, preset));

    private static void SelectCustom(Form dialog) =>
        Select(dialog, "comboBoxModel", item => item is not DspProcessorPreset);

    private static void Select(Form dialog, string field, Func<object?, bool> match)
    {
        var combo = (DarkComboBox)dialog.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;
        foreach (object? item in combo.Items)
        {
            if (match(item))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        throw new InvalidOperationException($"{field} holds no matching entry.");
    }
}
