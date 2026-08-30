using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot.WindowsForms;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>
/// The window accepts a dropped file anywhere on itself, which in WinForms means
/// every control on it has to be registered as a drop target: drag events do not
/// bubble, and a control that never registered refuses the drag where it stands.
/// These pin that reach, and the two things it must not do — take a drag that
/// belongs to somebody else, or open a file while a dialog has the window.
/// </summary>
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

        FileDropTarget.Attach(form, _ => true, _ => { });

        Assert.True(form.AllowDrop);
        Assert.True(panel.AllowDrop);
        Assert.True(button.AllowDrop);
    });

    [Fact]
    public void AControlBuiltLaterRegistersItself() => StaTest.Run(() =>
    {
        // Mode settings are docked in on demand and filter strips are built as they
        // are added, so the tree the shell shows is not the tree it started with.
        using Form form = ShownForm();
        var panel = new Panel();
        form.Controls.Add(panel);
        FileDropTarget.Attach(form, _ => true, _ => { });

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
        // Time Alignment fills much of its panel with a read-only RichTextBox, and
        // that is the one control type whose AllowDrop goes down a path of its own —
        // the native RichEdit drop target rather than the WinForms one. Pinned
        // because a control that threw here would take the whole shell's wiring with
        // it at construction, not just its own corner of the window.
        using Form form = ShownForm();
        var box = new RichTextBox { Size = new Size(100, 60), ReadOnly = true };
        form.Controls.Add(box);

        FileDropTarget.Attach(form, _ => true, _ => { });
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
        FileDropTarget.Attach(form, _ => true, _ => { });

        DragEventArgs drag = RaiseDragOver(button, Files("measurement.json"));

        Assert.Equal(DragDropEffects.Copy, drag.Effect);
    });

    [Fact]
    public void AFileTheShellCannotOpenIsRefusedWhileItHovers() => StaTest.Run(() =>
    {
        using Form form = ShownForm();
        Button button = DeepChild(form);
        FileDropTarget.Attach(form, _ => false, _ => Assert.Fail("must not open"));

        DragEventArgs drag = RaiseDragOver(button, Files("photo.png"));
        RaiseDragDrop(button, Files("photo.png"));

        Assert.Equal(DragDropEffects.None, drag.Effect);
    });

    [Fact]
    public void SomebodyElseSDragIsLeftExactlyAsItWas() => StaTest.Run(() =>
    {
        // The EQ wizard moves its filter strips by dragging, over the very controls
        // this is registered on. Its drag carries no files, and clearing the effect
        // here would cancel a move the bank had already accepted.
        using Form form = ShownForm();
        Button button = DeepChild(form);
        FileDropTarget.Attach(form, _ => true, _ => Assert.Fail("must not open"));

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
        FileDropTarget.Attach(form, _ => true, files => opened = [.. files]);

        RaiseDragDrop(button, Files("capture.json"));

        Assert.Equal(["capture.json"], opened);
    });

    [Fact]
    public void AWindowThatIsNotTakingInputTakesNoFileEither() => StaTest.Run(() =>
    {
        // What a modal dialog leaves behind: the owner window is disabled while the
        // dialog is up. A file opened underneath one would replace the very
        // measurement the dialog is asking about.
        using Form form = ShownForm();
        Button button = DeepChild(form);
        FileDropTarget.Attach(form, _ => true, _ => Assert.Fail("must not open"));
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
    public void ThePanelsTheShellShowsAllTakeBeingMadeDropTargets() => StaTest.Run(() =>
    {
        // Registering means setting AllowDrop on every control the window carries,
        // and a control type that refused would throw where the shell wires this up:
        // in its constructor, taking the whole application with it rather than one
        // corner of one panel. The heavy panels are built and realized here for that
        // reason — a plot, a rich text box, faders, numeric boxes and combos among
        // them — and the assert is simply that they are all drop targets afterwards.
        using Form form = ShownForm();
        var wizard = new EqWizardPanel();
        var virtualDsp = new VirtualCrossoverPanel();
        var timeAlignment = new TimeAlignmentPanel();
        var plot = new PlotView();
        form.Controls.AddRange([wizard, virtualDsp, timeAlignment, plot]);

        FileDropTarget.Attach(form, _ => true, _ => { });

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
        // Off screen: these realize a handle (a drop target is registered with OLE at
        // that moment) without a window flashing over the test run.
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

    // The framework raises these; a test has no drag to make it do so, so the event
    // is raised the way the framework would and the handlers run as they really do.
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
