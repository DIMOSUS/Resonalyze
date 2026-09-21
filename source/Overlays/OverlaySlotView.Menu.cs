using OxyPlot.Series;

namespace Resonalyze;

internal sealed partial class OverlaySlotView
{
    private readonly ContextMenuStrip captureMenu;
    private readonly ToolStripMenuItem captureCurveMenuItem;
    private readonly ToolStripItem exportDeviationMenuItem;
    private readonly ToolStripItem targetMenuItem;
    private readonly ToolStripItem settingsMenuItem;
    private readonly ToolStripItem clearSlotMenuItem;
    private readonly System.Windows.Forms.Timer longPressTimer;
    private bool longPressTriggered;

    public void CloseCaptureMenu()
    {
        if (captureMenu.Visible)
        {
            captureMenu.Close();
        }
    }

    private ContextMenuStrip BuildCaptureMenu(
        out ToolStripMenuItem captureCurveItem,
        out ToolStripItem exportDeviationItem,
        out ToolStripItem targetItem,
        out ToolStripItem settingsItem,
        out ToolStripItem clearSlotItem)
    {
        var menu = new ContextMenuStrip();
        captureCurveItem = new ToolStripMenuItem("Capture curve…");
        captureCurveItem.Click += CaptureCurveMenuItemClick;
        captureCurveItem.DropDownOpening += CaptureCurveMenuItemDropDownOpening;
        menu.Items.Add(captureCurveItem);
        menu.Items.Add("Import from text…", null, (_, _) => ImportFromText());
        menu.Items.Add("Export to text…", null, (_, _) => ExportToText());
        exportDeviationItem = menu.Items.Add("Export deviation…", null, (_, _) => ExportDeviationToText());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("ƒ  Calculated overlay…", null, (_, _) => ConfigureOperation());
        targetItem = menu.Items.Add("△  Target…", null, (_, _) => ConfigureTarget());
        menu.Items.Add(new ToolStripSeparator());
        settingsItem = menu.Items.Add("⚙  Settings…", null, (_, _) => OpenSettings());
        clearSlotItem = menu.Items.Add("✕  Clear slot", null, (_, _) => Session.ClearSlot(slot));
        return menu;
    }

    private void OpenCaptureMenu()
    {
        if (longPressTriggered)
        {
            longPressTriggered = false;
            return;
        }

        // Open on Click, not mouse-down: the mouse-up would land outside the new menu and close it.
        if (captureMenu.Visible)
        {
            captureMenu.Close();
            return;
        }

        ShowCaptureMenu();
    }

    private void CaptureButtonMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        longPressTriggered = false;
        longPressTimer.Start();
    }

    private void CaptureButtonMouseUp(object? sender, MouseEventArgs e)
    {
        longPressTimer.Stop();
    }

    private void LongPressTimerTick(object? sender, EventArgs e)
    {
        longPressTimer.Stop();
        CloseCaptureMenu();

        // Only a hold that actually opened something swallows its click (empty slots open nothing).
        longPressTriggered = OpenSettings();
    }

    private void ShowCaptureMenu()
    {
        if (captureMenu.Visible)
        {
            return;
        }

        owner.CloseCaptureMenus();

        RebuildCaptureCurveMenu();
        OverlaySlotState state = slot.State;
        exportDeviationMenuItem.Visible = state.Kind == OverlayKind.Target;
        exportDeviationMenuItem.Text = state.Target?.DeviationMode == TargetDeviationMode.Correction
            ? "Export EQ correction…"
            : "Export deviation…";
        targetMenuItem.Visible = OverlayTargets.SupportsMode(Session.Sources.CurrentOverlayMode);
        settingsMenuItem.Enabled = Session.CanConfigure(slot);
        clearSlotMenuItem.Enabled = settingsMenuItem.Enabled;
        DropDownMenu.ShowUnder(captureButton, captureMenu);
    }

    private void RebuildCaptureCurveMenu()
    {
        captureCurveMenuItem.DropDownItems.Clear();

        List<LineSeries> candidates = Session.Sources.CaptureCandidates();
        captureCurveMenuItem.Enabled = candidates.Count > 0;
        if (candidates.Count <= 1)
        {
            return;
        }

        foreach (LineSeries series in candidates)
        {
            var item = new ToolStripMenuItem(OverlayCapture.CandidateTitle(series));
            item.Click += (_, _) => Session.Capture(slot, series);
            captureCurveMenuItem.DropDownItems.Add(item);
        }
    }

    private void CaptureCurveMenuItemClick(object? sender, EventArgs e)
    {
        List<LineSeries> candidates = Session.Sources.CaptureCandidates();
        if (candidates.Count == 1)
        {
            Session.Capture(slot, candidates[0]);
        }
    }

    private void CaptureCurveMenuItemDropDownOpening(object? sender, EventArgs e)
    {
        captureCurveMenuItem.DropDownDirection = ShouldOpenCaptureSubmenuLeft()
            ? ToolStripDropDownDirection.Left
            : ToolStripDropDownDirection.Right;
    }

    private bool ShouldOpenCaptureSubmenuLeft()
    {
        if (captureCurveMenuItem.DropDownItems.Count == 0)
        {
            return false;
        }

        Rectangle screen = Screen.FromControl(captureButton).WorkingArea;
        Point menuRight = captureMenu.PointToScreen(new Point(captureMenu.Width, 0));
        int submenuWidth = captureCurveMenuItem.DropDown.GetPreferredSize(Size.Empty).Width;
        return menuRight.X + submenuWidth > screen.Right;
    }
}
