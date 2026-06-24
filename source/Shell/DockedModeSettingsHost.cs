using System.Windows.Forms;

namespace Resonalyze;

internal sealed class DockedModeSettingsHost : IDisposable
{
    public event EventHandler? StateChanged;

    private readonly Form owner;
    private readonly Control anchorControl;
    private Form? activeDialog;
    private ModeTab? activeTab;
    private bool allowProgrammaticClose;
    private bool disposed;

    public bool IsOpen => activeDialog != null;

    public DockedModeSettingsHost(Form owner, Control anchorControl)
    {
        this.owner = owner;
        this.anchorControl = anchorControl;

        owner.LocationChanged += OwnerLayoutChanged;
        owner.Resize += OwnerLayoutChanged;
        owner.SizeChanged += OwnerLayoutChanged;
        anchorControl.SizeChanged += OwnerLayoutChanged;
    }

    public void Toggle<TDialog>(
        ModeTab tab,
        Func<TDialog> create,
        Action<TDialog> initialize,
        Action<TDialog> apply)
        where TDialog : Form
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (activeDialog != null && activeTab == tab)
        {
            Close();
            return;
        }

        Close();

        TDialog dialog = create();
        initialize(dialog);
        ConfigureDialog(dialog, () => apply(dialog));
        dialog.FormClosed += DialogClosed;
        dialog.FormClosing += DialogFormClosing;

        activeDialog = dialog;
        activeTab = tab;

        dialog.Owner = owner;
        dialog.Show();
        PositionDialog();
        dialog.BringToFront();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        if (activeDialog == null)
        {
            return;
        }

        Form dialog = activeDialog;
        activeDialog = null;
        activeTab = null;
        dialog.FormClosed -= DialogClosed;
        dialog.FormClosing -= DialogFormClosing;

        if (!dialog.IsDisposed)
        {
            allowProgrammaticClose = true;
            try
            {
                dialog.Close();
                dialog.Dispose();
            }
            finally
            {
                allowProgrammaticClose = false;
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        owner.LocationChanged -= OwnerLayoutChanged;
        owner.Resize -= OwnerLayoutChanged;
        owner.SizeChanged -= OwnerLayoutChanged;
        anchorControl.SizeChanged -= OwnerLayoutChanged;
        Close();
    }

    private void ConfigureDialog(Form dialog, Action apply)
    {
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.FormBorderStyle = FormBorderStyle.None;
        dialog.ControlBox = false;
        dialog.MaximizeBox = false;
        dialog.MinimizeBox = false;
        dialog.ShowIcon = false;
        dialog.ShowInTaskbar = false;
        dialog.AcceptButton = null;
        dialog.CancelButton = null;

        foreach (Button button in EnumerateControls(dialog).OfType<Button>())
        {
            if (button.DialogResult == DialogResult.Cancel)
            {
                button.Visible = false;
                button.Enabled = false;
                button.TabStop = false;
                continue;
            }

            if (button.DialogResult == DialogResult.OK)
            {
                button.DialogResult = DialogResult.None;
                button.Click += (_, _) => apply();
            }
        }
    }

    private void PositionDialog()
    {
        if (activeDialog == null)
        {
            return;
        }

        if (owner.WindowState == FormWindowState.Minimized)
        {
            activeDialog.Hide();
            return;
        }

        if (!activeDialog.Visible)
        {
            activeDialog.Show(owner);
        }

        activeDialog.Location = HasRoomOutsideOwner(activeDialog)
            ? GetOutsideOwnerLocation()
            : GetInsideAnchorLocation(activeDialog);
    }

    private bool HasRoomOutsideOwner(Form dialog)
    {
        Rectangle workingArea = Screen.FromControl(owner).WorkingArea;
        int availableWidth = workingArea.Right - owner.Bounds.Right;
        return availableWidth >= dialog.Width;
    }

    private Point GetOutsideOwnerLocation() =>
        new(owner.Bounds.Right, owner.Bounds.Top + Scale(ChromeTitleBarController.Height));

    private int Scale(int value) =>
        (int)Math.Round(value * owner.DeviceDpi / 96.0);

    private Point GetInsideAnchorLocation(Form dialog)
    {
        Point topRight = anchorControl.PointToScreen(new Point(anchorControl.Width, 0));
        return new Point(topRight.X - dialog.Width, topRight.Y);
    }

    private void OwnerLayoutChanged(object? sender, EventArgs e)
    {
        if (activeDialog == null)
        {
            return;
        }

        PositionDialog();
    }

    private void DialogClosed(object? sender, FormClosedEventArgs e)
    {
        if (!ReferenceEquals(sender, activeDialog))
        {
            return;
        }

        activeDialog = null;
        activeTab = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DialogFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (allowProgrammaticClose)
        {
            return;
        }

        e.Cancel = true;
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control control in root.Controls)
        {
            yield return control;

            foreach (Control child in EnumerateControls(control))
            {
                yield return child;
            }
        }
    }
}
