using Resonalyze.Ui;

namespace Resonalyze.Ui.Dialogs;

/// <summary>
/// The graph controls on one card, opened with F1 over any graph
/// (<see cref="PlotGestureHelp"/> is what it lists). Modeless and single-instance
/// on purpose: a cheat sheet is read WHILE the other hand tries the gesture, so it
/// must not hold the app, and F1 pressed again brings the one window forward rather
/// than stacking another.
/// </summary>
/// <remarks>
/// The window and its three parts are built in the designer, so the form gets the
/// one scaling pass <c>AutoScaleMode.Dpi</c> owes it and every size stated there is
/// in the designer's 96 DPI. The ROWS are added here because they come from a list,
/// but only into the designer's <c>TableLayoutPanel</c> and only in logical units:
/// each label auto-sizes to the already-scaled font, and the margins and the wrap
/// width are scaled by the same pass. Nothing multiplies a size by the DPI itself —
/// that pass and this one would square the factor.
/// </remarks>
internal sealed partial class GraphHelpDialog : Form
{
    // One window at a time, kept across F1 presses. Static because the window is
    // the app's, not any one plot's: the same card describes every graph.
    private static GraphHelpDialog? openWindow;

    private GraphHelpDialog()
    {
        InitializeComponent();
        Text = PlotGestureHelp.Title;
        labelIntroduction.Text = PlotGestureHelp.Introduction;
        buttonClose.Click += (_, _) => Close();
        BuildRows();
    }

    /// <summary>
    /// Shows the card, or brings the open one forward. <paramref name="owner"/> is
    /// the window it belongs to, so it stays above it and closes with it.
    /// </summary>
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

        // The card is as tall as it needs to be at 96 DPI, and a display that scales
        // it further is not asked to make room: it is trimmed to the desktop rather
        // than hanging off the bottom of it, and what does not fit scrolls. Device
        // pixels on both sides of this line, which is why it converts.
        Rectangle desktop = Screen.FromControl(this).WorkingArea;
        Height = Math.Min(Height, desktop.Height - LogicalToDeviceUnits(40));

        // CenterParent is a MODAL setting; a modeless window has to place itself,
        // and it is placed once it knows its own scaled size.
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
        // Esc closes a modeless window too, but only because it is asked to:
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
            ForeColor = UiPalette.AccentBlueSoft,
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
