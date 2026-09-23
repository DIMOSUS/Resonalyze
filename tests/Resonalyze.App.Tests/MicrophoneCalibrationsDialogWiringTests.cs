using System.Runtime.InteropServices;
using System.Windows.Forms;
using Resonalyze.Options;
using static Resonalyze.App.Tests.CalibrationDialogFixtures;

namespace Resonalyze.App.Tests;

/// <summary>A shown calibration list driven through its controls, beside a session changed the same way: the rows, the
/// buttons and what OK hands back must be what the session and its reader say.</summary>
[Collection(WindowInput.Name)]
public sealed class MicrophoneCalibrationsDialogWiringTests
{
    public static TheoryData<string> Changes =>
    [
        "add file", "add cancelled", "rename", "rename refused", "rename unknown row", "edit path", "edit same path",
        "remove base", "add estimate", "add estimate cancelled", "edit estimate", "double click"
    ];

    [Theory]
    [MemberData(nameof(Changes))]
    public void EachEditReachesTheListAndOk(string change) => Run(() =>
    {
        using var folder = new TemporaryDirectory();
        using var list = new Calibrations(folder);
        MicrophoneCalibrationsSession expected = list.Expected;
        list.AssertShows(expected);

        switch (change)
        {
            case "add file":
                list.Files.Enqueue(folder.File("good.cal"));
                Click(list.Dialog, "buttonAddFile");
                expected.AddFile(folder.File("good.cal"));
                Assert.Equal(expected.Definitions.Count - 1, Assert.Single(list.View.SelectedIndices.Cast<int>()));
                break;
            case "add cancelled":
                list.Files.Enqueue(null);
                Click(list.Dialog, "buttonAddFile");
                break;
            case "rename":
                list.Rename(1, "Passenger", accepted: true);
                expected.Rename("broken", "Passenger");
                break;
            case "rename refused":
                list.Rename(1, "   ", accepted: false);
                break;
            case "rename unknown row":
                list.View.Items[1].Tag = null;
                list.Rename(1, "Passenger", accepted: false);
                return;
            case "edit path":
                SelectRow(list.View, 1);
                list.Files.Enqueue(folder.File("good.cal"));
                Click(list.Dialog, "buttonEdit");
                expected.SetPath(expected.Definitions[1], folder.File("good.cal"));
                break;
            case "edit same path":
                SelectRow(list.View, 0);
                list.Files.Enqueue(folder.File("left.cal"));
                Click(list.Dialog, "buttonEdit");
                break;
            case "remove base":
                SelectRow(list.View, 0);
                Click(list.Dialog, "buttonRemove");
                expected.Remove(expected.Definitions[0]);
                break;
            case "add estimate":
                Answer<AngleCalibrationDialog>(
                    () => Click(list.Dialog, "buttonAddAngle"),
                    angle =>
                    {
                        Assert.Equal(["The microphone's 0° calibration", "left", "broken"], Items(angle, "comboBoxBase"));
                        In<ThemedNumericUpDown>(angle, "numericAngle").Value = 30m;
                        In<ThemedComboBox>(angle, "comboBoxBase").SelectedIndex = 2;
                        In<Button>(angle, "buttonOk").PerformClick();
                    });
                MicrophoneCalibrationDefinition estimate = expected.NewAngle();
                estimate.AngleDegrees = 30;
                estimate.BaseId = "broken";
                expected.Add(estimate);
                break;
            case "add estimate cancelled":
                Answer<AngleCalibrationDialog>(
                    () => Click(list.Dialog, "buttonAddAngle"),
                    angle => In<Button>(angle, "buttonCancel").PerformClick());
                break;
            case "edit estimate":
                SelectRow(list.View, 2);
                Answer<AngleCalibrationDialog>(
                    () => Click(list.Dialog, "buttonEdit"),
                    angle =>
                    {
                        Assert.Equal(["The microphone's 0° calibration", "left", "broken"], Items(angle, "comboBoxBase"));
                        Assert.Equal(1, In<ThemedComboBox>(angle, "comboBoxBase").SelectedIndex);
                        In<TextBox>(angle, "textBoxName").Text = "Rear";
                        In<ThemedComboBox>(angle, "comboBoxBase").SelectedIndex = 0;
                        In<Button>(angle, "buttonOk").PerformClick();
                    });
                expected.Definitions[2].Name = "Rear";
                expected.Definitions[2].BaseId = null;
                break;
            case "double click":
                SelectRow(list.View, 3);
                Answer<AngleCalibrationDialog>(
                    () => list.DoubleClickSelected(),
                    angle =>
                    {
                        Assert.Equal("on zero", In<TextBox>(angle, "textBoxName").Text);
                        In<Button>(angle, "buttonCancel").PerformClick();
                    });
                break;
        }

        list.AssertShows(expected);
        Click(list.Dialog, "buttonOk");
        Assert.Equal(DialogResult.OK, list.Dialog.DialogResult);
        Assert.Equal(Describe(expected.Definitions), Describe(list.Dialog.Definitions));
        Assert.Empty(list.Files);
    });

    [Fact]
    public void AnAddedFileOpensIntoRename() => Run(() =>
    {
        using var folder = new TemporaryDirectory();
        using var list = new Calibrations(folder);
        list.Dialog.Activate();

        list.Files.Enqueue(folder.File("good.cal"));
        Click(list.Dialog, "buttonAddFile");

        Assert.NotEqual(IntPtr.Zero, SendMessage(list.View.Handle, ListViewGetEditControl, IntPtr.Zero, IntPtr.Zero));
    });

    [Fact]
    public void TheButtonsFollowTheSelection() => Run(() =>
    {
        using var folder = new TemporaryDirectory();
        using var list = new Calibrations(folder);

        SelectRow(list.View, 1);
        Assert.True(In<Button>(list.Dialog, "buttonEdit").Enabled);
        Assert.True(In<Button>(list.Dialog, "buttonRename").Enabled);
        Assert.True(In<Button>(list.Dialog, "buttonRemove").Enabled);
        SelectRow(list.View, null);
        Assert.False(In<Button>(list.Dialog, "buttonEdit").Enabled);
        Assert.False(In<Button>(list.Dialog, "buttonRename").Enabled);
        Assert.False(In<Button>(list.Dialog, "buttonRemove").Enabled);
    });

    [Fact]
    public void TheDialogEditsACopyUntilItIsAccepted() => Run(() =>
    {
        using var folder = new TemporaryDirectory();
        using var list = new Calibrations(folder);

        list.Rename(0, "renamed", accepted: true);
        SelectRow(list.View, 1);
        Click(list.Dialog, "buttonRemove");

        Assert.Equal(["left", "broken", "on left", "on zero"], list.Originals.Select(definition => definition.Name));
        Assert.Equal("left", list.Originals[2].BaseId);
    });

    private const int ListViewGetEditControl = 0x1018;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private static IEnumerable<string> Items(Form dialog, string name)
    {
        ThemedComboBox combo = In<ThemedComboBox>(dialog, name);
        return combo.Items.Cast<object>().Select(item => combo.GetItemText(item));
    }

    private static IEnumerable<string> Describe(IEnumerable<MicrophoneCalibrationDefinition> definitions) =>
        definitions.Select(d =>
            $"{d.Name}|{d.Kind}|{d.Path}|{d.BaseId}|{d.AngleDegrees}|{d.FrontDiameterMm}|{d.Grid}|{d.Reference}");

    private sealed class Calibrations : IDisposable
    {
        public Calibrations(TemporaryDirectory folder)
        {
            File.WriteAllText(folder.File("left.cal"), "20 0\n1000 0.5\n20000 1\n");
            File.WriteAllText(folder.File("good.cal"), "20 0\n1000 0.5\n20000 1\n");
            File.WriteAllText(folder.File("bad.cal"), "not a calibration\n");
            Originals =
            [
                new() { Id = "left", Name = "left", Kind = MicrophoneCalibrationKind.File, Path = folder.File("left.cal") },
                new() { Id = "broken", Name = "broken", Kind = MicrophoneCalibrationKind.File, Path = folder.File("bad.cal") },
                new() { Id = "on-left", Name = "on left", Kind = MicrophoneCalibrationKind.Angle, AngleDegrees = 60, BaseId = "left" },
                new() { Id = "on-zero", Name = "on zero", Kind = MicrophoneCalibrationKind.Angle, AngleDegrees = 45 }
            ];
            string zero = folder.File("left.cal");
            Expected = new MicrophoneCalibrationsSession(Originals, zero);
            Dialog = Shown(new MicrophoneCalibrationsDialog(Originals, zero, current =>
            {
                Asked.Add(current);
                return Files.Dequeue();
            }));
        }

        public List<MicrophoneCalibrationDefinition> Originals { get; }

        public MicrophoneCalibrationsSession Expected { get; }

        public MicrophoneCalibrationsDialog Dialog { get; }

        public ListView View => In<ListView>(Dialog, "listViewCalibrations");

        public Queue<string?> Files { get; } = new();

        public List<string?> Asked { get; } = [];

        public void Rename(int row, string? label, bool accepted)
        {
            var edit = new LabelEditEventArgs(row, label);
            typeof(ListView)
                .GetMethod("OnAfterLabelEdit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(View, [edit]);
            Assert.Equal(!accepted, edit.CancelEdit);
            if (accepted)
            {
                // What the native list does once an edit is not cancelled.
                View.Items[row].Text = label;
            }
        }

        public void DoubleClickSelected()
        {
            typeof(Control)
                .GetMethod("OnDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(View, [EventArgs.Empty]);
        }

        public void AssertShows(MicrophoneCalibrationsSession expected)
        {
            IReadOnlyList<MicrophoneCalibrationRow> rows = MicrophoneCalibrationRows.Read(expected, new CalibrationFileProbe());
            Assert.Equal(
                rows.Select(row => new[] { Id(row.Id), row.Name, row.Kind, row.Details, row.Status }),
                View.Items.Cast<ListViewItem>().Select(item => new[]
                {
                    Id((string)item.Tag!), item.Text, item.SubItems[1].Text, item.SubItems[2].Text, item.SubItems[3].Text
                }));
            bool one = View.SelectedItems.Count == 1;
            Assert.Equal(one, In<Button>(Dialog, "buttonEdit").Enabled);
            Assert.Equal(one, In<Button>(Dialog, "buttonRename").Enabled);
            Assert.Equal(one, In<Button>(Dialog, "buttonRemove").Enabled);
        }

        public void Dispose() => Dialog.Dispose();

        private static string Id(string id) => MicrophoneCalibrationDefinition.IsGeneratedId(id) ? "new" : id;
    }
}
