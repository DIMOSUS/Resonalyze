using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot.WindowsForms;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>WinForms drag events do not bubble, so every control must be registered as a drop target.</summary>
public sealed class FileDropTargetTests
{
    [Fact]
    public void EveryControlOnTheWindowTakesTheDrag() => StaTest.Run(() =>
    {
        using Form form = ShownForm();
        var panel = new Panel { Size = new Size(80, 80) };
        var button = new Button { Size = new Size(40, 20) };
        panel.Controls.Add(button);
        form.Controls.Add(panel);

        FileDropTarget.Attach(form, (_, _) => true, (_, _) => { });

        Assert.True(form.AllowDrop);
        Assert.True(panel.AllowDrop);
        Assert.True(button.AllowDrop);
    });

    [Fact]
    public void AControlBuiltLaterRegistersItself() => StaTest.Run(() =>
    {
        // Mode settings and filter strips are added later, so the control tree changes after start.
        using Form form = ShownForm();
        var panel = new Panel();
        form.Controls.Add(panel);
        FileDropTarget.Attach(form, (_, _) => true, (_, _) => { });

        var late = new Label();
        var laterStill = new Button();
        late.Controls.Add(laterStill);
        panel.Controls.Add(late);

        Assert.True(late.AllowDrop);
        Assert.True(laterStill.AllowDrop);
    });

    [Fact]
    public void ARichTextBoxTakesTheDragLikeAnythingElse() => StaTest.Run(() =>
    {
        // RichTextBox.AllowDrop goes through the native RichEdit target; a throw here would break the shell's constructor.
        using Form form = ShownForm();
        var box = new RichTextBox { Size = new Size(100, 60), ReadOnly = true };
        form.Controls.Add(box);

        FileDropTarget.Attach(form, (_, _) => true, (_, _) => { });
        box.CreateControl();
        DragEventArgs drag = RaiseDragOver(box, Files("measurement.json"));

        Assert.True(box.AllowDrop);
        Assert.Equal(DragDropEffects.Copy, drag.Effect);
    });

    [Fact]
    public void AFileOverADeepChildIsOffered() => StaTest.Run(() =>
    {
        using Form form = ShownForm();
        Button button = DeepChild(form);
        FileDropTarget.Attach(form, (_, _) => true, (_, _) => { });

        DragEventArgs drag = RaiseDragOver(button, Files("measurement.json"));

        Assert.Equal(DragDropEffects.Copy, drag.Effect);
    });

    [Fact]
    public void AFileTheShellCannotOpenIsRefusedWhileItHovers() => StaTest.Run(() =>
    {
        using Form form = ShownForm();
        Button button = DeepChild(form);
        FileDropTarget.Attach(form, (_, _) => false, (_, _) => Assert.Fail("must not open"));

        DragEventArgs drag = RaiseDragOver(button, Files("photo.png"));
        RaiseDragDrop(button, Files("photo.png"));

        Assert.Equal(DragDropEffects.None, drag.Effect);
    });

    [Fact]
    public void SomebodyElseSDragIsLeftExactlyAsItWas() => StaTest.Run(() =>
    {
        // The EQ wizard's strip drag carries no files; clearing the effect would cancel a move the bank accepted.
        using Form form = ShownForm();
        Button button = DeepChild(form);
        FileDropTarget.Attach(form, (_, _) => true, (_, _) => Assert.Fail("must not open"));

        var payload = new DataObject();
        payload.SetData("a PEQ strip");
        DragEventArgs drag = RaiseDragOver(button, payload, effect: DragDropEffects.Move);
        RaiseDragDrop(button, payload);

        Assert.Equal(DragDropEffects.Move, drag.Effect);
    });

    [Fact]
    public void ADroppedFileReachesTheShellByName() => StaTest.Run(() =>
    {
        using Form form = ShownForm();
        Button button = DeepChild(form);
        List<string>? opened = null;
        FileDropTarget.Attach(form, (_, _) => true, (_, files) => opened = [.. files]);

        RaiseDragDrop(button, Files("capture.json"));

        Assert.Equal(["capture.json"], opened);
    });

    [Fact]
    public void TheControlTheFileLandedOnIsNamedWithIt() => StaTest.Run(() =>
    {
        // Drag events reach only the control under the pointer, and Compare reads the file as the reference.
        using Form form = ShownForm();
        Button button = DeepChild(form);
        Control? landedOn = null;
        FileDropTarget.Attach(form, (_, _) => true, (over, _) => landedOn = over);

        RaiseDragDrop(button, Files("measurement.json"));

        Assert.Same(button, landedOn);
    });

    [Fact]
    public void AControlMayRefuseWhatTheRestOfTheWindowTakes() => StaTest.Run(() =>
    {
        using Form form = ShownForm();
        Button button = DeepChild(form);
        Control panel = button.Parent!;
        FileDropTarget.Attach(
            form,
            (over, _) => over != button,
            (_, _) => Assert.Fail("must not open on the refusing control"));

        DragEventArgs overButton = RaiseDragOver(button, Files("sweep.wav"));
        DragEventArgs overPanel = RaiseDragOver(panel, Files("sweep.wav"));
        RaiseDragDrop(button, Files("sweep.wav"));

        Assert.Equal(DragDropEffects.None, overButton.Effect);
        Assert.Equal(DragDropEffects.Copy, overPanel.Effect);
    });

    [Fact]
    public void AWindowThatIsNotTakingInputTakesNoFileEither() => StaTest.Run(() =>
    {
        // A modal dialog disables the owner; opening a file underneath would replace what the dialog is about.
        using Form form = ShownForm();
        Button button = DeepChild(form);
        FileDropTarget.Attach(form, (_, _) => true, (_, _) => Assert.Fail("must not open"));
        form.Enabled = false;

        DragEventArgs drag = RaiseDragOver(button, Files("measurement.json"));
        RaiseDragDrop(button, Files("measurement.json"));

        Assert.Equal(DragDropEffects.None, drag.Effect);
    });

    [Fact]
    public void ADragCarryingNoFilesReadsAsEmptyRatherThanThrowing()
    {
        var payload = new DataObject();
        payload.SetData("a PEQ strip");

        Assert.Empty(FileDropTarget.FilesOf(payload));
        Assert.Empty(FileDropTarget.FilesOf(null));
        Assert.False(FileDropTarget.CarriesFiles(payload));
        Assert.False(FileDropTarget.CarriesFiles(null));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void ThePanelsTheShellShowsAllTakeBeingMadeDropTargets() => StaTest.Run(() =>
    {
        using Form form = ShownForm();
        var wizard = new EqWizardPanel();
        var virtualDsp = new VirtualCrossoverPanel();
        var timeAlignment = new TimeAlignmentPanel();
        var plot = new PlotView();
        form.Controls.AddRange([wizard, virtualDsp, timeAlignment, plot]);

        FileDropTarget.Attach(form, (_, _) => true, (_, _) => { });

        foreach (Control control in Descendants(form))
        {
            Assert.True(control.AllowDrop, $"{control.GetType().Name} takes no drop");
        }
    });

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static Form ShownForm()
    {
        // Off screen: a handle registers the OLE drop target without a window flashing.
        var form = new Form
        {
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ClientSize = new Size(200, 200)
        };
        form.Show();
        return form;
    }

    private static Button DeepChild(Form form)
    {
        var panel = new Panel { Size = new Size(120, 120) };
        var button = new Button { Size = new Size(60, 24) };
        panel.Controls.Add(button);
        form.Controls.Add(panel);
        return button;
    }

    private static DataObject Files(params string[] names)
    {
        var payload = new DataObject();
        payload.SetData(DataFormats.FileDrop, names);
        return payload;
    }

    private static DragEventArgs RaiseDragOver(
        Control control, IDataObject data, DragDropEffects effect = DragDropEffects.None) =>
        Raise(control, "OnDragOver", data, effect);

    private static DragEventArgs RaiseDragDrop(
        Control control, IDataObject data, DragDropEffects effect = DragDropEffects.None) =>
        Raise(control, "OnDragDrop", data, effect);

    private static DragEventArgs Raise(
        Control control, string method, IDataObject data, DragDropEffects effect)
    {
        var args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.All, effect);
        typeof(Control)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [args]);
        return args;
    }
}
