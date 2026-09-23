using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Options;
using static Resonalyze.App.Tests.CalibrationDialogFixtures;

namespace Resonalyze.App.Tests;

/// <summary>A shown array dialog driven through its controls, beside a session changed the same way: the list, the editor,
/// the buttons, the status and what OK hands back must be what the session and its reader say.</summary>
public sealed class ArrayMicrophonesDialogWiringTests
{
    private static readonly IReadOnlyList<MicrophoneCalibrationEntry> Calibrations =
    [
        new(MicrophoneCalibrationIds.ZeroDegrees, "0°", true),
        new("cal-1", "ECM8000 90°", true),
        new("cal-2", "broken", false)
    ];

    private static readonly IReadOnlyList<int> Inputs = [0, 1, 2, 3, 4, 5];

    private static List<ArrayMicrophoneDefinition> Microphones() =>
    [
        new() { ChannelOffset = 2, CalibrationId = "cal-1", Note = "left" },
        new() { ChannelOffset = 4, CalibrationId = "cal-gone" },
        new() { ChannelOffset = 1, Note = "on the loopback" }
    ];

    public static TheoryData<string> Changes =>
    [
        "select", "select then leave", "add", "add after editing", "add twice", "update", "update another input",
        "edit then select another", "remove", "remove last", "remove all"
    ];

    [Theory]
    [MemberData(nameof(Changes))]
    public void EachEditReachesTheDialogAndOk(string change) => Run(() =>
    {
        using var array = new Array();
        ArrayMicrophonesSession expected = array.Expected;
        array.AssertShows(expected);

        switch (change)
        {
            case "select":
                array.Select(1);
                break;
            case "select then leave":
                array.Select(0);
                array.Select(null);
                break;
            case "add":
                array.Click("buttonAdd");
                break;
            case "add after editing":
                array.Input(1);
                array.Calibration(2);
                array.Note("  far  ");
                array.Click("buttonAdd");
                break;
            case "add twice":
                array.Click("buttonAdd");
                array.Click("buttonAdd");
                array.Select(null);
                array.Click("buttonAdd");
                break;
            case "update":
                array.Select(0);
                array.Calibration(0);
                array.Note("renamed");
                array.Click("buttonUpdate");
                break;
            case "update another input":
                array.Select(1);
                array.Input(0);
                array.Click("buttonUpdate");
                break;
            case "edit then select another":
                array.Select(0);
                array.Input(1);
                array.Calibration(3);
                array.Note("never updated");
                array.Select(1);
                array.Click("buttonUpdate");
                break;
            case "remove":
                array.Select(0);
                array.Click("buttonRemove");
                break;
            case "remove last":
                array.Select(2);
                array.Click("buttonRemove");
                break;
            case "remove all":
                for (int i = 0; i < 3; i++)
                {
                    array.Select(0);
                    array.Click("buttonRemove");
                }

                break;
        }

        array.AssertShows(expected);
        CalibrationDialogFixtures.Click(array.Dialog, "buttonOk");
        Assert.Equal(DialogResult.OK, array.Dialog.DialogResult);
        Assert.Equivalent(expected.Microphones, array.Dialog.Microphones, strict: true);
    });

    [Fact]
    public void TheDialogEditsACopyUntilItIsAccepted() => Run(() =>
    {
        using var array = new Array();

        array.Select(0);
        array.Note("edited");
        array.Click("buttonUpdate");
        array.Select(1);
        array.Click("buttonRemove");

        Assert.Equivalent(Microphones(), array.Originals, strict: true);
    });

    private sealed class Array : IDisposable
    {
        public Array()
        {
            Originals = Microphones();
            Expected = new ArrayMicrophonesSession(Originals, Calibrations, Inputs, 0, 1, "test inputs");
            Dialog = Shown(new ArrayMicrophonesDialog(Originals, Calibrations, Inputs, 0, 1, "test inputs"));
        }

        public List<ArrayMicrophoneDefinition> Originals { get; }

        public ArrayMicrophonesSession Expected { get; }

        public ArrayMicrophonesDialog Dialog { get; }

        private bool rebuilt;

        private ListView List => In<ListView>(Dialog, "listViewMicrophones");

        private ThemedComboBox InputCombo => In<ThemedComboBox>(Dialog, "comboBoxInput");

        private ThemedComboBox CalibrationCombo => In<ThemedComboBox>(Dialog, "comboBoxCalibration");

        private TextBox NoteBox => In<TextBox>(Dialog, "textBoxNote");

        public void Select(int? row)
        {
            rebuilt = false;
            SelectRow(List, row);
            Expected.Select(null);
            Expected.Select(row);
        }

        public void Input(int index)
        {
            InputCombo.SelectedIndex = index;
            Expected.SetEditorChannel(Expected.EditorChannels[index]);
        }

        public void Calibration(int index)
        {
            CalibrationCombo.SelectedIndex = index;
            Expected.SetCalibrationIndex(index);
        }

        public void Note(string text)
        {
            NoteBox.Text = text;
            Expected.SetNote(text);
        }

        public void Click(string button)
        {
            rebuilt = true;
            CalibrationDialogFixtures.Click(Dialog, button);
            _ = button switch
            {
                "buttonAdd" => Expected.Add(),
                "buttonUpdate" => Expected.Update(),
                _ => Expected.Remove()
            };
        }

        public void AssertShows(ArrayMicrophonesSession expected)
        {
            Assert.Equal(
                ArrayMicrophoneRows.Read(expected).Select(row => new[] { row.Input, row.Calibration, row.Note }),
                List.Items.Cast<ListViewItem>().Select(item => item.SubItems.Cast<ListViewItem.ListViewSubItem>()
                    .Select(sub => sub.Text).ToArray()));
            Assert.True(List.OwnerDraw);
            Assert.Equal(expected.Selected is int row ? [row] : [], List.SelectedIndices.Cast<int>());
            if (rebuilt && expected.Selected is int focused)
            {
                Assert.Equal(focused, List.FocusedItem?.Index);
            }

            Assert.Equal(
                expected.EditorChannels.Select(ArrayMicrophoneRows.InputLabel),
                InputCombo.Items.Cast<object>().Select(item => InputCombo.GetItemText(item)));
            Assert.Equal(expected.EditorChannel is int channel ? expected.EditorChannels.ToList().IndexOf(channel) : -1,
                InputCombo.SelectedIndex);
            Assert.Equal(expected.EditorChannels.Count > 0, InputCombo.Enabled);
            Assert.Equal(
                expected.CalibrationOptions.Select(option => option.DisplayName),
                CalibrationCombo.Items.Cast<object>().Select(item => CalibrationCombo.GetItemText(item)));
            Assert.Equal(expected.CalibrationIndex, CalibrationCombo.SelectedIndex);
            Assert.Equal(expected.CalibrationOptions.Count > 1, CalibrationCombo.Enabled);
            Assert.Equal(expected.Note, NoteBox.Text);
            Assert.Equal(expected.Selected != null, In<Button>(Dialog, "buttonUpdate").Enabled);
            Assert.Equal(expected.Selected != null, In<Button>(Dialog, "buttonRemove").Enabled);
            Assert.Equal(expected.CanAdd, In<Button>(Dialog, "buttonAdd").Enabled);
            Assert.Equal(ArrayMicrophoneRows.Status(expected), In<Label>(Dialog, "labelStatus").Text);
        }

        public void Dispose() => Dialog.Dispose();
    }
}
