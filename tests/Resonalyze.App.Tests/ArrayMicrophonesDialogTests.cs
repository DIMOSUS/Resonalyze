using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class ArrayMicrophonesDialogTests
{
    private static readonly IReadOnlyList<MicrophoneCalibrationEntry> Calibrations =
    [
        new(MicrophoneCalibrationIds.ZeroDegrees, "0°", true),
        new("cal-1", "ECM8000 90°", true)
    ];

    private static ArrayMicrophonesDialog CreateDialog(
        IReadOnlyList<ArrayMicrophoneDefinition> microphones,
        IReadOnlyList<int> availableChannels,
        int? loopbackChannel = 1)
    {
        var dialog = new ArrayMicrophonesDialog(
            microphones,
            Calibrations,
            availableChannels,
            microphoneChannel: 0,
            loopbackChannel: loopbackChannel,
            "test inputs");
        // Shown off-screen: ListView and combo raise no selection until their handles exist.
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-6000, -6000);
        dialog.Show();
        return dialog;
    }

    private static T Control<T>(Form dialog, string name)
        where T : Control =>
        (T)dialog
            .GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dialog)!;

    private static string Offered(Form dialog) =>
        string.Join(
            ", ",
            Control<ThemedComboBox>(dialog, "comboBoxInput")
                .Items
                .Cast<object>()
                .Select(item => item.ToString()));

    private static void Click(Form dialog, string name) =>
        Control<Button>(dialog, name).PerformClick();

    [Fact]
    public void OnlyFreeInputsAreOffered() => StaTest.Run(() =>
    {
        using ArrayMicrophonesDialog dialog = CreateDialog(
            [new ArrayMicrophoneDefinition { ChannelOffset = 2 }],
            [0, 1, 2, 3]);

        Assert.Equal("Input 4", Offered(dialog));
    });

    [Fact]
    public void WithoutALoopbackItsInputIsFreeAgain() => StaTest.Run(() =>
    {
        using ArrayMicrophonesDialog dialog = CreateDialog([], [0, 1, 2], loopbackChannel: null);

        Assert.Equal("Input 2, Input 3", Offered(dialog));
    });

    [Fact]
    public void SelectingAMicrophoneOffersItsOwnInputBack() => StaTest.Run(() =>
    {
        using ArrayMicrophonesDialog dialog = CreateDialog(
            [new ArrayMicrophoneDefinition { ChannelOffset = 2 }],
            [0, 1, 2, 3]);

        Control<ListView>(dialog, "listViewMicrophones").Items[0].Selected = true;

        Assert.Equal("Input 3, Input 4", Offered(dialog));
    });

    [Fact]
    public void AddingTakesTheEditorsInputCalibrationAndNote() => StaTest.Run(() =>
    {
        using ArrayMicrophonesDialog dialog = CreateDialog([], [0, 1, 2, 3]);
        Control<ThemedComboBox>(dialog, "comboBoxInput").SelectedIndex = 1;
        Control<ThemedComboBox>(dialog, "comboBoxCalibration").SelectedIndex = 2;
        Control<TextBox>(dialog, "textBoxNote").Text = "  left ear  ";

        Click(dialog, "buttonAdd");

        ArrayMicrophoneDefinition added = Assert.Single(dialog.Microphones);
        Assert.Equal(3, added.ChannelOffset);
        Assert.Equal("cal-1", added.CalibrationId);
        Assert.Equal("left ear", added.Note);
    });

    [Fact]
    public void AddingTwiceCannotLandOnTheSameInput() => StaTest.Run(() =>
    {
        using ArrayMicrophonesDialog dialog = CreateDialog([], [0, 1, 2, 3]);
        Assert.Equal("Input 3, Input 4", Offered(dialog));

        Click(dialog, "buttonAdd");

        Control<ListView>(dialog, "listViewMicrophones").SelectedIndices.Clear();
        Assert.Equal("Input 4", Offered(dialog));

        Click(dialog, "buttonAdd");

        Assert.Equal([2, 3], dialog.Microphones.Select(microphone => microphone.ChannelOffset));
        Control<ListView>(dialog, "listViewMicrophones").SelectedIndices.Clear();
        Assert.Equal(string.Empty, Offered(dialog));
    });

    [Fact]
    public void AddingTwiceInARowCannotDuplicateTheFirst() => StaTest.Run(() =>
    {
        // Add, Add: the selected new row offers its own input, which was one Add away from a duplicate the settings layer drops silently.
        using ArrayMicrophonesDialog dialog = CreateDialog([], [0, 1, 2, 3]);

        Click(dialog, "buttonAdd");
        Assert.False(
            Control<Button>(dialog, "buttonAdd").Enabled,
            "the selected row's own input is on offer for editing, not for adding");

        Click(dialog, "buttonAdd");

        Assert.Equal([2], dialog.Microphones.Select(microphone => microphone.ChannelOffset));
    });

    [Fact]
    public void AnInputTheMeasurementTookIsNamedRatherThanDroppedInSilence() => StaTest.Run(() =>
    {
        // Reachable by moving the measurement mic onto an array input elsewhere; the measurement layer drops it, so the dialog names it.
        using ArrayMicrophonesDialog dialog = CreateDialog(
            [new ArrayMicrophoneDefinition { ChannelOffset = 0 }],
            [0, 1, 2, 3]);

        Assert.Contains(
            "the measurement microphone",
            Control<ListView>(dialog, "listViewMicrophones").Items[0].SubItems[0].Text);
        Assert.Contains("cannot be recorded", Control<Label>(dialog, "labelStatus").Text);
    });

    [Fact]
    public void EveryInputTakenLeavesNothingToAdd() => StaTest.Run(() =>
    {
        using ArrayMicrophonesDialog dialog = CreateDialog([], [0, 1]);

        Assert.Equal(string.Empty, Offered(dialog));
        Assert.False(Control<Button>(dialog, "buttonAdd").Enabled);
        Assert.Contains("every input is in use", Control<Label>(dialog, "labelStatus").Text);
        Assert.Contains("test inputs", Control<Label>(dialog, "labelStatus").Text);
    });

    [Fact]
    public void RemovingLeavesTheRestAndFreesTheInput() => StaTest.Run(() =>
    {
        using ArrayMicrophonesDialog dialog = CreateDialog(
            [
                new ArrayMicrophoneDefinition { ChannelOffset = 2 },
                new ArrayMicrophoneDefinition { ChannelOffset = 3 }
            ],
            [0, 1, 2, 3]);
        Control<ListView>(dialog, "listViewMicrophones").Items[0].Selected = true;

        Click(dialog, "buttonRemove");

        Assert.Equal(3, Assert.Single(dialog.Microphones).ChannelOffset);
        Assert.Contains("Input 3", Offered(dialog));
    });

    [Fact]
    public void UpdatingChangesTheSelectedMicrophoneOnly() => StaTest.Run(() =>
    {
        using ArrayMicrophonesDialog dialog = CreateDialog(
            [
                new ArrayMicrophoneDefinition { ChannelOffset = 2, Note = "first" },
                new ArrayMicrophoneDefinition { ChannelOffset = 3, Note = "second" }
            ],
            [0, 1, 2, 3]);
        Control<ListView>(dialog, "listViewMicrophones").Items[1].Selected = true;
        Control<TextBox>(dialog, "textBoxNote").Text = "renamed";

        Click(dialog, "buttonUpdate");

        Assert.Equal("first", dialog.Microphones[0].Note);
        Assert.Equal("renamed", dialog.Microphones[1].Note);
        Assert.Equal(3, dialog.Microphones[1].ChannelOffset);
    });

    [Fact]
    public void ACalibrationThatIsGoneIsNamedRatherThanReadingAsNone() => StaTest.Run(() =>
    {
        using ArrayMicrophonesDialog dialog = CreateDialog(
            [new ArrayMicrophoneDefinition { ChannelOffset = 2, CalibrationId = "cal-gone" }],
            [0, 1, 2, 3]);

        ListViewItem row = Control<ListView>(dialog, "listViewMicrophones").Items[0];
        Assert.Equal("cal-gone (missing)", row.SubItems[1].Text);
    });

    [Fact]
    public void TheDialogEditsACopyUntilItIsAccepted() => StaTest.Run(() =>
    {
        var original = new List<ArrayMicrophoneDefinition>
        {
            new() { ChannelOffset = 2, Note = "original" }
        };
        using ArrayMicrophonesDialog dialog = CreateDialog(original, [0, 1, 2, 3]);
        Control<ListView>(dialog, "listViewMicrophones").Items[0].Selected = true;
        Control<TextBox>(dialog, "textBoxNote").Text = "edited";
        Click(dialog, "buttonUpdate");

        Assert.Equal("original", original[0].Note);
        Assert.Equal("edited", dialog.Microphones[0].Note);
    });
}
