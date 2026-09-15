using System.Runtime.InteropServices;

namespace Resonalyze.Ui;

/// <summary>Window-wide file drop. WinForms drag events do not bubble, so every control in the tree (and each added later) is registered.</summary>
/// <remarks>Unrecognized drags are left untouched (the EQ wizard reorders strips by drag). The control under the pointer is passed on:
/// where a file lands can matter (IR on Compare = reference).</remarks>
internal sealed class FileDropTarget
{
    private readonly HashSet<Control> registered = [];
    private readonly Control root;
    private readonly Func<Control, IReadOnlyList<string>, bool> accepts;
    private readonly Action<Control, IReadOnlyList<string>> dropped;

    private FileDropTarget(
        Control root,
        Func<Control, IReadOnlyList<string>, bool> accepts,
        Action<Control, IReadOnlyList<string>> dropped)
    {
        this.root = root;
        this.accepts = accepts;
        this.dropped = dropped;
    }

    /// <param name="accepts">Asked on every drag move, so it must be cheap.</param>
    internal static void Attach(
        Control root,
        Func<Control, IReadOnlyList<string>, bool> accepts,
        Action<Control, IReadOnlyList<string>> dropped)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(accepts);
        ArgumentNullException.ThrowIfNull(dropped);
        new FileDropTarget(root, accepts, dropped).Register(root);
    }

    internal static IReadOnlyList<string> FilesOf(IDataObject? data) =>
        CarriesFiles(data) && data!.GetData(DataFormats.FileDrop) is string[] files
            ? files
            : [];

    /// <summary>For other controls' drag handlers, so refusing their own kind does not refuse the window's.</summary>
    internal static bool CarriesFiles(IDataObject? data) =>
        data != null && data.GetDataPresent(DataFormats.FileDrop);

    private void Register(Control control)
    {
        if (!registered.Add(control))
        {
            return;
        }

        control.AllowDrop = true;
        control.DragEnter += HandleDragOver;
        control.DragOver += HandleDragOver;
        control.DragDrop += HandleDragDrop;
        control.ControlAdded += HandleControlAdded;
        control.Disposed += HandleDisposed;
        foreach (Control child in control.Controls)
        {
            Register(child);
        }
    }

    private void HandleControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control != null)
        {
            Register(e.Control);
        }
    }

    private void HandleDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            registered.Remove(control);
        }
    }

    private void HandleDragOver(object? sender, DragEventArgs e)
    {
        IReadOnlyList<string> files = FilesOf(e.Data);
        if (files.Count == 0)
        {
            return;
        }

        e.Effect = CanAccept(Over(sender), files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void HandleDragDrop(object? sender, DragEventArgs e)
    {
        IReadOnlyList<string> files = FilesOf(e.Data);
        Control over = Over(sender);
        if (files.Count == 0 || !CanAccept(over, files))
        {
            return;
        }

        e.Effect = DragDropEffects.Copy;
        dropped(over, files);
    }

    private Control Over(object? sender) => sender as Control ?? root;

    private bool CanAccept(Control over, IReadOnlyList<string> files) =>
        IsTakingInput() && accepts(over, files);

    /// <summary>A modal dialog disables its owner at window level without changing <see cref="Control.Enabled"/>.</summary>
    private bool IsTakingInput() =>
        root.IsHandleCreated && IsWindowEnabled(root.Handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);
}
