using System.Runtime.InteropServices;

namespace Resonalyze;

// Custom window chrome placed on the form in the designer. The control itself is
// the title bar surface; its tabs, version label and window buttons are built in
// Initialize because they depend on the owning form, the DPI scale and the mode
// tab actions, none of which are available to the designer's parameterless ctor.
internal sealed class ChromeTitleBar : Panel
{
    public const int BarHeight = 40;
    public const int ResizeGripSize = 8;
    public const int WmNcHitTest = 0x84;
    public const int HtClient = 1;

    private const int WmNcLeftButtonDown = 0xA1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private readonly Dictionary<ModeTab, Button> modeTabButtons = new();

    private Form form = null!;
    private Action updateMaximizedBounds = null!;
    private FlowLayoutPanel tabBar = null!;
    private LinkLabel versionLabel = null!;
    private float dpiScale = 1f;
    private int titleBarHeight = BarHeight;
    private int windowButtonWidth;
    private int versionLabelWidth;
    private bool isCustomMaximized;
    private bool initialized;
    private Rectangle restoreBounds;

    public ChromeTitleBar()
    {
        BackColor = UiPalette.TitleBarBackground;
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Location = Point.Empty;
    }

    public bool IsCustomMaximized => isCustomMaximized;

    // Wires the title bar to its owning form. Called once after the designer has
    // added the control to the form.
    public void Initialize(
        Form owningForm,
        Action updateMaximizedBoundsAction,
        IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        if (initialized)
        {
            return;
        }
        initialized = true;

        form = owningForm;
        updateMaximizedBounds = updateMaximizedBoundsAction;
        dpiScale = GetDpiScale();
        titleBarHeight = Scale(BarHeight);
        windowButtonWidth = Scale(46);
        versionLabelWidth = GetVersionLabelWidth(ApplicationVersionInfo.GetDisplayVersion());

        form.FormBorderStyle = FormBorderStyle.None;
        updateMaximizedBounds();

        Size = new Size(form.ClientSize.Width, titleBarHeight);
        MouseDown += TitleBarMouseDown;

        tabBar = CreateTabBar();
        AddModeTabs(tabBar, tabActions);
        Controls.Add(tabBar);

        versionLabel = CreateVersionLabel();
        Controls.Add(versionLabel);

        AddWindowButton("─", form.ClientSize.Width - 138, MinimizeWindowClick);
        AddWindowButton("☐", form.ClientSize.Width - 92, MaximizeWindowClick);
        AddWindowButton("✕", form.ClientSize.Width - 46, CloseWindowClick);

        BringToFront();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);

    public void SetActiveModeTab(ModeTab activeTab)
    {
        foreach ((ModeTab tab, Button button) in modeTabButtons)
        {
            SetModeTabStyle(button, tab == activeTab);
        }
    }

    public void SetUpdateAvailable(string releaseUrl)
    {
        UpdateVersionLabel(
            $"{ApplicationVersionInfo.GetDisplayVersion()}  Update available",
            releaseUrl);
    }

    public static Point GetPointFromLParam(IntPtr lParam)
    {
        int value = unchecked((int)lParam.ToInt64());
        int x = (short)(value & 0xFFFF);
        int y = (short)((value >> 16) & 0xFFFF);
        return new Point(x, y);
    }

    public static IntPtr GetResizeHitTest(Point point, Size clientSize)
    {
        bool left = point.X <= ResizeGripSize;
        bool right = point.X >= clientSize.Width - ResizeGripSize;
        bool top = point.Y <= ResizeGripSize;
        bool bottom = point.Y >= clientSize.Height - ResizeGripSize;

        if (left && top)
        {
            return (IntPtr)HtTopLeft;
        }
        if (right && top)
        {
            return (IntPtr)HtTopRight;
        }
        if (left && bottom)
        {
            return (IntPtr)HtBottomLeft;
        }
        if (right && bottom)
        {
            return (IntPtr)HtBottomRight;
        }
        if (left)
        {
            return (IntPtr)HtLeft;
        }
        if (right)
        {
            return (IntPtr)HtRight;
        }
        if (top)
        {
            return (IntPtr)HtTop;
        }
        if (bottom)
        {
            return (IntPtr)HtBottom;
        }

        return (IntPtr)HtClient;
    }

    private FlowLayoutPanel CreateTabBar()
    {
        var result = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = BackColor,
            FlowDirection = FlowDirection.LeftToRight,
            Location = new Point(Scale(8), Scale(6)),
            Padding = new Padding(0),
            Size = new Size(Scale(200), titleBarHeight - Scale(5)),
            WrapContents = false
        };
        result.MouseDown += TitleBarMouseDown;
        UpdateTabBarLayout(result);
        return result;
    }

    private LinkLabel CreateVersionLabel()
    {
        var label = new LinkLabel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoEllipsis = true,
            BackColor = BackColor,
            ForeColor = UiPalette.TitleBarText,
            DisabledLinkColor = UiPalette.TitleBarText,
            Font = new Font(
                form.Font.FontFamily,
                Math.Max(8f, form.Font.Size - 0.5f),
                FontStyle.Regular),
            LinkBehavior = LinkBehavior.NeverUnderline,
            LinkColor = UiPalette.TitleBarText,
            ActiveLinkColor = UiPalette.TitleBarText,
            VisitedLinkColor = UiPalette.TitleBarText,
            Location = GetVersionLabelLocation(),
            Size = new Size(versionLabelWidth, titleBarHeight),
            TabStop = false,
            Text = ApplicationVersionInfo.GetDisplayVersion(),
            TextAlign = ContentAlignment.MiddleRight
        };
        label.MouseDown += VersionLabelMouseDown;
        label.LinkClicked += VersionLabelLinkClicked;
        return label;
    }

    private void AddModeTabs(
        FlowLayoutPanel targetTabBar,
        IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        AddModeTab(targetTabBar, ModeTab.Impulse, "Impulse", tabActions);
        AddModeTab(targetTabBar, ModeTab.Frequency, "Frequency", tabActions);
        AddModeTab(targetTabBar, ModeTab.Phase, "Phase", tabActions);
        AddModeTab(targetTabBar, ModeTab.GroupDelay, "Group Delay", tabActions);
        AddModeTab(targetTabBar, ModeTab.Waterfall, "Waterfall", tabActions);
        AddModeTab(targetTabBar, ModeTab.Burst, "Burst", tabActions);
        AddModeTab(targetTabBar, ModeTab.Autocorrelation, "Autocorrelation", tabActions);
        AddModeTab(targetTabBar, ModeTab.TimeAlignment, "Time Alignment", tabActions);
        AddModeTab(targetTabBar, ModeTab.LiveSpectrum, "Live Spectrum", tabActions);
    }

    private void AddModeTab(
        FlowLayoutPanel targetTabBar,
        ModeTab tab,
        string text,
        IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        var button = new Button
        {
            AutoSize = false,
            Font = new Font(form.Font, FontStyle.Regular),
            ForeColor = UiPalette.TitleBarTextSoft,
            Height = Math.Max(Scale(28), TextRenderer.MeasureText(text, form.Font).Height + Scale(8)),
            Margin = new Padding(0, 0, Scale(2), 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = true,
            Width = GetModeTabWidth(text)
        };
        UiStyle.ApplySurfaceButton(button, BackColor, UiPalette.TitleBarTextSoft);
        button.Click += (_, _) => tabActions[tab]();

        modeTabButtons.Add(tab, button);
        targetTabBar.Controls.Add(button);
        SetModeTabStyle(button, active: false);
    }

    private void AddWindowButton(
        string text,
        int left,
        EventHandler clickHandler)
    {
        int legacyRightDistance = form.ClientSize.Width - left - 46;
        int width = Scale(46);
        Button button = new()
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = UiPalette.TitleBarTextBright,
            Location = new Point(form.ClientSize.Width - Scale(legacyRightDistance) - width, 0),
            Size = new Size(width, titleBarHeight),
            Text = text,
        };
        UiStyle.ApplySurfaceButton(button, BackColor, UiPalette.TitleBarTextBright);
        button.FlatAppearance.MouseOverBackColor = text == "✕" // X
            ? UiPalette.AccentBlueWarning
            : UiPalette.AccentBlueMuted;
        button.FlatAppearance.MouseDownBackColor = text == "✕" // X
            ? UiPalette.AccentBlueMutedAlt
            : UiPalette.AccentBlueStrong;
        button.Click += clickHandler;
        Controls.Add(button);
    }

    private void TitleBarMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        if (e.Clicks >= 2)
        {
            ToggleMaximized();
            return;
        }

        if (isCustomMaximized)
        {
            RestoreWindowBoundsForDrag(sender, e.Location);
        }

        ReleaseCapture();
        SendMessage(
            form.Handle,
            WmNcLeftButtonDown,
            (IntPtr)HtCaption,
            IntPtr.Zero);
    }

    private void VersionLabelMouseDown(object? sender, MouseEventArgs e)
    {
        if (versionLabel.Links.Count > 0)
        {
            return;
        }

        TitleBarMouseDown(sender, e);
    }

    private void MinimizeWindowClick(object? sender, EventArgs e)
    {
        form.WindowState = FormWindowState.Minimized;
    }

    private void MaximizeWindowClick(object? sender, EventArgs e)
    {
        ToggleMaximized();
    }

    private void ToggleMaximized()
    {
        if (isCustomMaximized)
        {
            RestoreWindowBounds();
            return;
        }

        MaximizeToCurrentScreen();
    }

    private void MaximizeToCurrentScreen()
    {
        if (form.WindowState == FormWindowState.Minimized)
        {
            return;
        }

        restoreBounds = form.Bounds;
        updateMaximizedBounds();
        Rectangle workingArea = Screen.FromRectangle(form.Bounds).WorkingArea;
        isCustomMaximized = true;
        form.Bounds = workingArea;
    }

    private void RestoreWindowBounds()
    {
        if (!isCustomMaximized)
        {
            return;
        }

        isCustomMaximized = false;
        if (restoreBounds.Width > 0 &&
            restoreBounds.Height > 0)
        {
            form.Bounds = restoreBounds;
        }
    }

    private void RestoreWindowBoundsForDrag(object? sender, Point localPoint)
    {
        if (!isCustomMaximized ||
            restoreBounds.Width <= 0 ||
            restoreBounds.Height <= 0)
        {
            return;
        }

        Control origin = sender as Control ?? this;
        Point screenPoint = origin.PointToScreen(localPoint);
        Rectangle workingArea = Screen.FromPoint(screenPoint).WorkingArea;
        double horizontalRatio = Math.Clamp(
            (double)screenPoint.X - form.Left,
            0,
            Math.Max(1, form.Width)) / Math.Max(1, form.Width);

        isCustomMaximized = false;
        int restoredLeft = screenPoint.X - (int)Math.Round(restoreBounds.Width * horizontalRatio);
        int restoredTop = screenPoint.Y - Math.Max(1, titleBarHeight / 2);
        restoredLeft = Math.Max(
            workingArea.Left,
            Math.Min(restoredLeft, workingArea.Right - restoreBounds.Width));
        restoredTop = Math.Max(
            workingArea.Top,
            Math.Min(restoredTop, workingArea.Bottom - restoreBounds.Height));

        form.Bounds = new Rectangle(
            restoredLeft,
            restoredTop,
            restoreBounds.Width,
            restoreBounds.Height);
    }

    private void CloseWindowClick(object? sender, EventArgs e)
    {
        form.Close();
    }

    private void UpdateVersionLabel(string text, string releaseUrl)
    {
        versionLabel.Text = text;
        versionLabel.LinkBehavior = LinkBehavior.HoverUnderline;
        versionLabel.LinkColor = UiPalette.AccentBlueSoft;
        versionLabel.ActiveLinkColor = UiPalette.AccentBlueSoftHover;
        versionLabel.VisitedLinkColor = versionLabel.LinkColor;
        versionLabel.Links.Clear();
        versionLabel.Links.Add(0, text.Length, releaseUrl);

        versionLabelWidth = GetVersionLabelWidth(text);
        versionLabel.Size = new Size(versionLabelWidth, titleBarHeight);
        versionLabel.Location = GetVersionLabelLocation();
        UpdateTabBarLayout(tabBar);
    }

    private int GetModeTabWidth(string text) =>
        Math.Max(
            Scale(70),
            TextRenderer.MeasureText(text, form.Font).Width + Scale(12));

    private int GetVersionLabelWidth(string versionText) =>
        Math.Max(
            Scale(88),
            TextRenderer.MeasureText(versionText, form.Font).Width + Scale(18));

    private Point GetVersionLabelLocation() =>
        new(form.ClientSize.Width - windowButtonWidth * 3 - versionLabelWidth - Scale(6), 0);

    private void UpdateTabBarLayout(FlowLayoutPanel targetTabBar)
    {
        int rightReservedWidth = windowButtonWidth * 3 + versionLabelWidth + Scale(12);
        targetTabBar.Size = new Size(
            Math.Max(Scale(200), form.ClientSize.Width - Scale(8) - rightReservedWidth),
            titleBarHeight - Scale(5));
    }

    private static void VersionLabelLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (e.Link?.LinkData is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Form? owner = sender is Control control
            ? control.FindForm()
            : null;
        ApplicationUpdateService.ShowUpdateChoice(owner, url);
    }

    private int Scale(int value) =>
        (int)Math.Round(value * dpiScale);

    private float GetDpiScale()
    {
        using Graphics graphics = form.CreateGraphics();
        return Math.Max(form.DeviceDpi / 96.0f, graphics.DpiX / 96.0f);
    }

    private static void SetModeTabStyle(Button button, bool active)
    {
        button.BackColor = active
            ? UiPalette.AccentBlue
            : UiPalette.ButtonBackground;
        button.FlatAppearance.MouseOverBackColor = active
            ? UiPalette.AccentBlueHover
            : UiPalette.ButtonHoverBackground;
        button.FlatAppearance.MouseDownBackColor = UiPalette.AccentBlueStrong;
    }
}
