using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The DSP processor dialog decides the rate every simulated filter is built at, so
/// what it REMEMBERS while the user browses the model list is not cosmetic: coming
/// back to Custom with the wrong answer restored changes the simulation without
/// anyone touching the rate again.
/// </summary>
public sealed class DspProcessorDialogTests
{
    private const int MeasurementRate = 48_000;

    [Fact]
    public void LookingAtAPreset_DoesNotForgetAStatedRate()
    {
        StaTest.Run(() =>
        {
            // Opened on "follow", the user states 96 kHz, looks at a device, and comes
            // back to Custom: their 96 kHz has to still be there.
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
            // And the other way round: opened on a stated rate, the user switches to
            // "follow", looks at a device, and comes back — still following.
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
            // A project set up before its measurements: the entry says only "Follow
            // measurements", with no rate to name, and the profile still answers with
            // something the simulation can run at.
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
            // The project stores "no notes" as null, so the dialog has to answer the
            // same for a field the user cleared or filled with whitespace — otherwise
            // every OK would count as an edit and schedule a save.
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
            // The limit is enforced by the field itself, so OK never has to refuse;
            // and the field is the tallest thing on the form, so it is the one that
            // would push the buttons off the bottom if the designer numbers slipped.
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

    private static Form Open(
        bool followsMeasurements,
        int measurementRateHz = MeasurementRate,
        bool? phaseControl = null)
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
                phaseControl
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

    // The rate list's first entry is the "follow" marker; every other one is an int.
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
