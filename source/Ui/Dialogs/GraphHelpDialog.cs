using Resonalyze.Ui;

namespace Resonalyze.Ui.Dialogs;

/// <summary>F1 graph-controls card (<see cref="PlotGestureHelp"/>), modeless and single-instance: read while trying the gesture.</summary>
/// <remarks>Rows are added in logical units into the designer's table; never multiply by DPI here, or the factor is squared.</remarks>
internal sealed partial class GraphHelpDialog : Form
{
    private static GraphHelpDialog? openWindow;

    private GraphHelpDialog()
    {
        InitializeComponent();
        Text = PlotGestureHelp.Title;
        labelIntroduction.Text = PlotGestureHelp.Introduction;
        buttonClose.Click += (_, _) => Close();
        BuildRows();
    }

    public static void ShowFor(IWin32Window? owner)
    {
        if (openWindow is { IsDisposed: false } already)
        {
            already.Activate();
            return;
        }

        var window = new GraphHelpDialog();
        openWindow = window;
        window.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(openWindow, window))
            {
                openWindow = null;
            }
        };

        if (owner == null)
        {
            window.Show();
        }
        else
        {
            window.Show(owner);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Trimmed to the desktop (device pixels on both sides); what does not fit scrolls.
        Rectangle desktop = Screen.FromControl(this).WorkingArea;
        Height = Math.Min(Height, desktop.Height - LogicalToDeviceUnits(40));

        // CenterParent only applies to modal windows.
        if (Owner is not Form parent)
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Location = new Point(
            parent.Left + ((parent.Width - Width) / 2),
            parent.Top + ((parent.Height - Height) / 2));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // DialogResult does nothing outside ShowDialog.
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildRows()
    {
        tableRows.SuspendLayout();
        foreach (PlotGestureHelpSection section in PlotGestureHelp.Sections)
        {
            AddHeading(section.Title, first: tableRows.RowCount == 0);
            foreach (PlotGestureHelpEntry entry in section.Entries)
            {
                AddEntry(entry);
            }
        }

        tableRows.ResumeLayout(performLayout: false);
    }

    private void AddHeading(string title, bool first)
    {
        var heading = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = UiPalette.AccentMark,
            Margin = new Padding(0, first ? 0 : 14, 0, 4),
            Text = title,
        };
        int row = NextRow();
        tableRows.Controls.Add(heading, 0, row);
        tableRows.SetColumnSpan(heading, 2);
    }

    private void AddEntry(PlotGestureHelpEntry entry)
    {
        var gesture = new Label
        {
            AutoSize = true,
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(0, 2, 18, 2),
            Text = entry.Gesture,
        };
        var effect = new Label
        {
            AutoSize = true,
            ForeColor = UiPalette.TextSecondary,
            Margin = new Padding(0, 2, 0, 2),
            MaximumSize = new Size(380, 0),
            Text = entry.Effect,
        };

        int row = NextRow();
        tableRows.Controls.Add(gesture, 0, row);
        tableRows.Controls.Add(effect, 1, row);
    }

    private int NextRow()
    {
        tableRows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return tableRows.RowCount++;
    }
}
