using System.Runtime.InteropServices;

namespace Resonalyze.Ui;

/// <summary>
/// Lets a whole window accept files dragged onto it from Explorer.
/// </summary>
/// <remarks>
/// WinForms drag events do not bubble. Only the control under the pointer is asked,
/// and one that never registered itself as a drop target refuses the drag outright —
/// so a window made of panels, plots, labels and buttons has to register every one of
/// them, which is what this does: the whole tree at the moment it is attached, plus
/// each control added later (mode settings are docked in on demand, filter strips are
/// built as they are added).
/// <para>
/// It never touches a drag it does not recognize. Controls that carry drop targets of
/// their own — the EQ wizard's bank reorders its filter strips by dragging — keep
/// working, because a payload that is not a file drop leaves the effect exactly as the
/// other handler left it.
/// </para>
/// </remarks>
internal sealed class FileDropTarget
{
    // Every control that has been wired, so a control re-added to its parent (or
    // reached twice through two parents on the way down) does not collect a second
    // copy of the handlers.
    private readonly HashSet<Control> registered = [];
    private readonly Control root;
    private readonly Func<IReadOnlyList<string>, bool> accepts;
    private readonly Action<IReadOnlyList<string>> dropped;

    private FileDropTarget(
        Control root,
        Func<IReadOnlyList<string>, bool> accepts,
        Action<IReadOnlyList<string>> dropped)
    {
        this.root = root;
        this.accepts = accepts;
        this.dropped = dropped;
    }

    /// <summary>
    /// Makes <paramref name="root"/> and everything inside it accept dropped files.
    /// </summary>
    /// <param name="accepts">
    /// Whether this set of files can be opened right now. Asked on every drag move, so
    /// it must be cheap, and asked again on the drop.
    /// </param>
    /// <param name="dropped">Opens the files. Called on the UI thread.</param>
    internal static void Attach(
        Control root,
        Func<IReadOnlyList<string>, bool> accepts,
        Action<IReadOnlyList<string>> dropped)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(accepts);
        ArgumentNullException.ThrowIfNull(dropped);
        new FileDropTarget(root, accepts, dropped).Register(root);
    }

    /// <summary>The files a drag carries, empty when it carries something else.</summary>
    internal static IReadOnlyList<string> FilesOf(IDataObject? data) =>
        CarriesFiles(data) && data!.GetData(DataFormats.FileDrop) is string[] files
            ? files
            : [];

    /// <summary>
    /// Whether a drag carries files at all — what another control's own drag handler
    /// asks before refusing a drag, so that refusing its own kind does not also refuse
    /// the window's.
    /// </summary>
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
            // Somebody else's drag — a filter strip being moved within its bank.
            // Leaving the effect alone is what keeps this from cancelling it.
            return;
        }

        e.Effect = CanAccept(files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void HandleDragDrop(object? sender, DragEventArgs e)
    {
        IReadOnlyList<string> files = FilesOf(e.Data);
        if (files.Count == 0 || !CanAccept(files))
        {
            return;
        }

        e.Effect = DragDropEffects.Copy;
        dropped(files);
    }

    private bool CanAccept(IReadOnlyList<string> files) =>
        IsTakingInput() && accepts(files);

    /// <summary>
    /// Whether the window is taking input at all. A modal dialog — the application's
    /// own, or a common one such as Open File — disables its owner at the window level
    /// while it is up, and the managed <see cref="Control.Enabled"/> flag does not say
    /// so. Without this a file dropped on the window behind a dialog would be opened
    /// underneath it, replacing the very measurement the dialog is asking about.
    /// </summary>
    private bool IsTakingInput() =>
        root.IsHandleCreated && IsWindowEnabled(root.Handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);
}
