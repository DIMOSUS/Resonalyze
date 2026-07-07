using System.Windows.Forms;

namespace Resonalyze;

internal sealed class DockedModeSettingsHost : IDisposable
{
    public event EventHandler? StateChanged;

    private readonly Form owner;
    private readonly Control anchorControl;
    private Form? activeDialog;
    private object? activeKey;
    private bool allowProgrammaticClose;
    private bool disposed;

    public bool IsOpen => activeDialog != null;

    public DockedModeSettingsHost(Form owner, Control anchorControl)
    {
        this.owner = owner;
        this.anchorControl = anchorControl;

        owner.LocationChanged += OwnerLayoutChanged;
        owner.SizeChanged += OwnerLayoutChanged;
        anchorControl.SizeChanged += OwnerLayoutChanged;
    }

    public void Toggle<TDialog>(
        object key,
        Func<TDialog> create,
        Action<TDialog> initialize,
        Func<TDialog, Task> apply,
        bool applyOnChange = false)
        where TDialog : Form
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (activeDialog != null && Equals(activeKey, key))
        {
            Close();
            return;
        }

        Close();

        TDialog dialog = create();
        initialize(dialog);
        ConfigureDialog(dialog, () => apply(dialog), applyOnChange);
        dialog.FormClosed += DialogClosed;
        dialog.FormClosing += DialogFormClosing;

        activeDialog = dialog;
        activeKey = key;

        dialog.Owner = owner;
        if (owner.WindowState != FormWindowState.Minimized)
        {
            dialog.Location = GetDialogLocation(dialog);
            dialog.Show(owner);
        }
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
        activeKey = null;
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
        owner.SizeChanged -= OwnerLayoutChanged;
        anchorControl.SizeChanged -= OwnerLayoutChanged;
        Close();
    }

    private void ConfigureDialog(Form dialog, Func<Task> apply, bool applyOnChange)
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
                if (applyOnChange)
                {
                    button.Visible = false;
                    button.Enabled = false;
                    button.TabStop = false;
                    continue;
                }

                button.DialogResult = DialogResult.None;
                button.Click += async (_, _) =>
                    {
                        if (!button.Enabled)
                        {
                            return;
                        }

                        button.Enabled = false;
                        try
                        {
                            await ApplySafelyAsync(dialog, apply);
                        }
                        finally
                        {
                            if (!button.IsDisposed)
                            {
                                button.Enabled = true;
                            }
                        }
                    };
            }
        }

        if (applyOnChange)
        {
            WireLiveApply(dialog, apply);
        }
    }

    // Both apply paths run in async void contexts (a Click handler and a
    // BeginInvoke continuation); an exception escaping them would kill the
    // process instead of surfacing as a dialog.
    private static async Task ApplySafelyAsync(Form dialog, Func<Task> apply)
    {
        try
        {
            await apply();
        }
        catch (Exception exception)
        {
            if (!dialog.IsDisposed)
            {
                MessageBox.Show(
                    dialog,
                    $"Applying the settings failed.\r\n\r\n{exception.Message}",
                    "Settings",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }

    private static void WireLiveApply(Form dialog, Func<Task> apply)
    {
        bool applying = false;
        bool pending = false;
        bool scheduled = false;

        void ScheduleApply()
        {
            if (dialog.IsDisposed)
            {
                return;
            }

            pending = true;
            if (applying || scheduled)
            {
                return;
            }

            scheduled = true;
            dialog.BeginInvoke((MethodInvoker)ApplyPending);
        }

        async void ApplyPending()
        {
            scheduled = false;
            if (applying || dialog.IsDisposed || !pending)
            {
                return;
            }

            applying = true;
            pending = false;
            try
            {
                await ApplySafelyAsync(dialog, apply);
            }
            finally
            {
                applying = false;
            }

            if (pending && !dialog.IsDisposed)
            {
                ApplyPending();
            }
        }

        foreach (Control control in EnumerateControls(dialog))
        {
            switch (control)
            {
                case Button:
                    break;
                case NumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => ScheduleApply();
                    break;
                case DarkNumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => ScheduleApply();
                    break;
                case ComboBox comboBox:
                    comboBox.SelectionChangeCommitted += (_, _) => ScheduleApply();
                    break;
                case DarkComboBox comboBox:
                    comboBox.SelectionChangeCommitted += (_, _) => ScheduleApply();
                    break;
                case CheckBox checkBox:
                    checkBox.CheckedChanged += (_, _) => ScheduleApply();
                    break;
                case RadioButton radioButton:
                    radioButton.CheckedChanged += (_, _) => ScheduleApply();
                    break;
                case TextBox textBox:
                    textBox.Validated += (_, _) => ScheduleApply();
                    break;
                case TrackBar trackBar:
                    trackBar.ValueChanged += (_, _) => ScheduleApply();
                    break;
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

        Point location = GetDialogLocation(activeDialog);
        if (!activeDialog.Visible)
        {
            activeDialog.Location = location;
            activeDialog.Show(owner);
            return;
        }

        activeDialog.Location = location;
    }

    private Point GetDialogLocation(Form dialog) =>
        HasRoomOutsideOwner(dialog)
            ? GetOutsideOwnerLocation()
            : GetInsideAnchorLocation(dialog);

    private bool HasRoomOutsideOwner(Form dialog)
    {
        // Dock to the right only when the dialog fits on the monitor that holds the
        // window's right edge. Keying on the right edge (rather than the window
        // centre, which is what Screen.FromControl uses) keeps the decision stable
        // while the window is dragged across a monitor boundary — the dialog always
        // lands just past owner.Bounds.Right, so that edge's monitor is the one that
        // actually matters.
        Rectangle workingArea = GetRightEdgeScreen().WorkingArea;
        int availableWidth = workingArea.Right - owner.Bounds.Right;
        return availableWidth >= dialog.Width;
    }

    private Screen GetRightEdgeScreen()
    {
        Rectangle bounds = owner.Bounds;
        Point rightEdge = new(bounds.Right - 1, bounds.Top + bounds.Height / 2);
        return Screen.FromPoint(rightEdge);
    }

    private Point GetOutsideOwnerLocation() =>
        new(owner.Bounds.Right, owner.Bounds.Top + Scale(ChromeTitleBar.BarHeight));

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
        activeKey = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void InvokeIfOpen<TDialog>(Action<TDialog> action)
        where TDialog : Form
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (activeDialog is TDialog dialog)
        {
            action(dialog);
        }
    }

    private void DialogFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Only swallow direct user closes (the docked panel has no close UI of
        // its own); cancelling owner/shutdown closes would block app exit and
        // Windows logoff.
        if (allowProgrammaticClose || e.CloseReason != CloseReason.UserClosing)
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
