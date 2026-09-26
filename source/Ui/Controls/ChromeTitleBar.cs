using System.Runtime.InteropServices;

namespace Resonalyze;

// Tabs, version label and window buttons are built in Initialize: they need the form, DPI and tab actions.
internal sealed class ChromeTitleBar : Panel
{
    public const int BarHeight = 40;
    public const int ResizeGripSize = 8;
    public const int WmNcHitTest = 0x84;
    public const int HtClient = 1;

    // Settings, minimize, maximize, close: the strip the tab bar and the version label must not run into.
    private const int WindowButtonCount = 4;

    private const int WmNcLeftButtonDown = 0xA1;
    private const int HtTransparent = -1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    // Windows "Show animations" (reduced-motion) setting.
    private const int SpiGetClientAreaAnimation = 0x1042;

    // WINDOWPLACEMENT.flags: a minimized window goes back to maximized when restored.
    private const int WpfRestoreToMaximized = 0x2;

    private const int UpdatePulsePeriodMs = 1_800;
    private const int UpdatePulseIntervalMs = 40;

    private readonly Dictionary<ModeTab, Button> modeTabButtons = new();

    private Form form = null!;
    private Action updateMaximizedBounds = null!;
    private FlowLayoutPanel tabBar = null!;
    private LinkLabel versionLabel = null!;
    private Button? toolsDropDownButton;
    private ContextMenuStrip? toolsMenu;
    private ModeTab lastToolsTab = ModeTab.ToolsVirtualCrossover;
    private readonly ToolTip settingsToolTip = new();
    private System.Windows.Forms.Timer? updatePulseTimer;
    private int updatePulseElapsedMs;
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

    public bool IsMaximized => isCustomMaximized || form.WindowState switch
    {
        FormWindowState.Maximized => true,
        FormWindowState.Minimized => RestoresToMaximized(),
        _ => false
    };

    // A real Maximized state (Aero snap) or a minimized window keeps its normal bounds in the form's RestoreBounds.
    public Rectangle NormalBounds =>
        isCustomMaximized ? restoreBounds
        : form.WindowState == FormWindowState.Normal ? form.Bounds
        : form.RestoreBounds;

    public int ScaledResizeGripSize => Scale(ResizeGripSize);

    // The bar covers the top resize strip; HTTRANSPARENT hands the hit test to the form, which maps it to HTTOP*.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest &&
            initialized &&
            !isCustomMaximized &&
            form.WindowState == FormWindowState.Normal)
        {
            Point clientPoint = PointToClient(GetPointFromLParam(m.LParam));
            int grip = ScaledResizeGripSize;
            if (clientPoint.Y <= grip ||
                clientPoint.X <= grip ||
                clientPoint.X >= Width - grip)
            {
                m.Result = (IntPtr)HtTransparent;
                return;
            }
        }

        base.WndProc(ref m);
    }

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

        Button settingsButton = AddWindowButton(
            string.Empty, form.ClientSize.Width - 184, SettingsClick);
        settingsButton.AccessibleName = "Settings";
        settingsButton.Paint += PaintSettingsGlyph;
        settingsToolTip.SetToolTip(
            settingsButton,
            "Settings: the theme and anything else the whole window obeys." +
            Environment.NewLine +
            "Settings for the mode you are in stay behind Mode Settings...");
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref bool value,
        uint update);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public Point MinPosition;
        public Point MaxPosition;
        public int NormalLeft;
        public int NormalTop;
        public int NormalRight;
        public int NormalBottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref NativeWindowPlacement placement);

    // WindowState reads Minimized alone; Windows keeps whether an Aero-snap maximized window was minimized from there.
    private bool RestoresToMaximized()
    {
        var placement = new NativeWindowPlacement { Length = Marshal.SizeOf<NativeWindowPlacement>() };
        return form.IsHandleCreated &&
               GetWindowPlacement(form.Handle, ref placement) &&
               (placement.Flags & WpfRestoreToMaximized) != 0;
    }

    public void SetActiveModeTab(ModeTab activeTab)
    {
        if (IsToolsTab(activeTab))
        {
            lastToolsTab = activeTab;
        }

        foreach (Button button in modeTabButtons.Values.Distinct())
        {
            bool active = modeTabButtons.Any(
                pair => ReferenceEquals(pair.Value, button) && pair.Key == activeTab);
            SetModeTabStyle(button, active);
        }

        if (toolsDropDownButton != null)
        {
            SetModeTabStyle(toolsDropDownButton, IsToolsTab(activeTab));
        }
    }

    public void SetUpdateAvailable(string releaseUrl)
    {
        UpdateVersionLabel(
            $"{ApplicationVersionInfo.GetDisplayVersion()}  Update available",
            releaseUrl);
        StartUpdatePulse();
    }

    public static Point GetPointFromLParam(IntPtr lParam)
    {
        int value = unchecked((int)lParam.ToInt64());
        int x = (short)(value & 0xFFFF);
        int y = (short)((value >> 16) & 0xFFFF);
        return new Point(x, y);
    }

    public static IntPtr GetResizeHitTest(Point point, Size clientSize) =>
        GetResizeHitTest(point, clientSize, ResizeGripSize);

    public static IntPtr GetResizeHitTest(Point point, Size clientSize, int gripSize)
    {
        bool left = point.X <= gripSize;
        bool right = point.X >= clientSize.Width - gripSize;
        bool top = point.Y <= gripSize;
        bool bottom = point.Y >= clientSize.Height - gripSize;

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
        AddToolsModeTab(targetTabBar, tabActions);
    }

    private void AddModeTab(
        FlowLayoutPanel targetTabBar,
        ModeTab tab,
        string text,
        IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        var button = new ReleaseClickButton
        {
            AutoSize = false,
            Font = new Font(form.Font, FontStyle.Regular),
            ForeColor = UiPalette.TitleBarTextActive,
            Height = Math.Max(Scale(28), TextRenderer.MeasureText(text, form.Font).Height + Scale(8)),
            Margin = new Padding(0, 0, Scale(2), 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = true,
            Width = GetModeTabWidth(text)
        };
        UiStyle.ApplySurfaceButton(button, BackColor, UiPalette.TitleBarTextActive);
        button.Click += (_, _) => tabActions[tab]();

        modeTabButtons.Add(tab, button);
        targetTabBar.Controls.Add(button);
        SetModeTabStyle(button, active: false);
    }

    private void AddToolsModeTab(
        FlowLayoutPanel targetTabBar,
        IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        const string text = "Tools";
        int height = Math.Max(Scale(28), TextRenderer.MeasureText(text, form.Font).Height + Scale(8));
        var host = new Panel
        {
            BackColor = BackColor,
            Height = height,
            Margin = new Padding(0, 0, Scale(2), 0),
            Width = GetModeTabWidth(text) + Scale(20)
        };
        host.MouseDown += TitleBarMouseDown;

        var mainButton = new ReleaseClickButton
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = false,
            Font = new Font(form.Font, FontStyle.Regular),
            ForeColor = UiPalette.TitleBarTextActive,
            Location = Point.Empty,
            Margin = Padding.Empty,
            Size = new Size(host.Width - Scale(22), height),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = true
        };
        UiStyle.ApplySurfaceButton(mainButton, BackColor, UiPalette.TitleBarTextActive);
        mainButton.Click += (_, _) => SelectToolsTab(tabActions, lastToolsTab);

        toolsDropDownButton = new ReleaseClickButton
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
            AutoSize = false,
            Font = new Font(form.Font, FontStyle.Regular),
            ForeColor = UiPalette.TitleBarTextActive,
            Location = new Point(host.Width - Scale(22), 0),
            Margin = Padding.Empty,
            Size = new Size(Scale(22), height),
            Text = string.Empty,
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = true
        };
        UiStyle.ApplySurfaceButton(toolsDropDownButton, BackColor, UiPalette.TitleBarTextActive);
        toolsDropDownButton.Paint += PaintToolsDropDownButton;
        toolsDropDownButton.Click += (_, _) => ShowToolsMenu(tabActions);

        modeTabButtons.Add(ModeTab.ToolsEqWizard, mainButton);
        modeTabButtons.Add(ModeTab.ToolsSignalGenerator, mainButton);
        modeTabButtons.Add(ModeTab.ToolsVirtualCrossover, mainButton);
        modeTabButtons.Add(ModeTab.ToolsFirConstructor, mainButton);
        host.Controls.Add(mainButton);
        host.Controls.Add(toolsDropDownButton);
        targetTabBar.Controls.Add(host);
        SetModeTabStyle(mainButton, active: false);
        SetModeTabStyle(toolsDropDownButton, active: false);
    }

    private void ShowToolsMenu(IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        if (toolsDropDownButton == null)
        {
            return;
        }

        toolsMenu ??= BuildToolsMenu(tabActions);
        DropDownMenu.ShowUnder(toolsDropDownButton, toolsMenu);
    }

    private ContextMenuStrip BuildToolsMenu(IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = UiPalette.ButtonBackground,
            ForeColor = UiPalette.TitleBarTextActive,
            ShowImageMargin = false
        };
        AddToolsMenuItem(menu, "Virtual DSP", ModeTab.ToolsVirtualCrossover, tabActions);
        AddToolsMenuItem(menu, "EQ Wizard", ModeTab.ToolsEqWizard, tabActions);
        AddToolsMenuItem(menu, "FIR Constructor", ModeTab.ToolsFirConstructor, tabActions);
        AddToolsMenuItem(menu, "Signal Generator", ModeTab.ToolsSignalGenerator, tabActions);
        return menu;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            toolsMenu?.Dispose();
            settingsToolTip.Dispose();
            DetachUpdatePulse();
        }

        base.Dispose(disposing);
    }

    private void AddToolsMenuItem(
        ContextMenuStrip menu,
        string text,
        ModeTab tab,
        IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        var item = new ToolStripMenuItem(text)
        {
            BackColor = UiPalette.ButtonBackground,
            ForeColor = UiPalette.TitleBarTextActive
        };
        item.Click += (_, _) => SelectToolsTab(tabActions, tab);
        menu.Items.Add(item);
    }

    private void SelectToolsTab(
        IReadOnlyDictionary<ModeTab, Action> tabActions,
        ModeTab tab)
    {
        lastToolsTab = tab;
        tabActions[tab]();
    }

    private void PaintToolsDropDownButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var separatorPen = new Pen(UiPalette.BorderMuted);
        e.Graphics.DrawLine(separatorPen, 0, Scale(5), 0, button.Height - Scale(5));

        int arrowWidth = Scale(7);
        int arrowHeight = Scale(4);
        int centerX = button.Width / 2;
        int centerY = button.Height / 2 + Scale(1);
        Point[] points =
        [
            new(centerX - arrowWidth / 2, centerY - arrowHeight / 2),
            new(centerX + arrowWidth / 2, centerY - arrowHeight / 2),
            new(centerX, centerY + arrowHeight / 2)
        ];
        using var brush = new SolidBrush(UiPalette.TitleBarTextActive);
        e.Graphics.FillPolygon(brush, points);
    }

    private ReleaseClickButton AddWindowButton(
        string text,
        int left,
        EventHandler clickHandler)
    {
        int legacyRightDistance = form.ClientSize.Width - left - 46;
        int width = Scale(46);
        ReleaseClickButton button = new()
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = UiPalette.TitleBarTextActive,
            Location = new Point(form.ClientSize.Width - Scale(legacyRightDistance) - width, 0),
            Size = new Size(width, titleBarHeight),
            Text = text,
        };
        UiStyle.ApplySurfaceButton(button, BackColor, UiPalette.TitleBarTextActive);
        button.FlatAppearance.MouseOverBackColor = text == "✕"
            ? UiPalette.UpdateBadgeFill
            : UiPalette.TitleBarButtonFill;
        button.FlatAppearance.MouseDownBackColor = text == "✕"
            ? UiPalette.UpdateBadgeFillDim
            : UiPalette.AccentFill;
        button.Click += clickHandler;
        Controls.Add(button);
        return button;
    }

    // Eight teeth around a hole, filled in the button's own ink so it follows the theme and the hover state.
    private void PaintSettingsGlyph(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float centreX = button.Width / 2f;
        float centreY = button.Height / 2f;
        float outerRadius = ScaleF(5.9f);
        float rootRadius = ScaleF(4.3f);
        float holeRadius = ScaleF(2.0f);

        const int teeth = 8;
        var points = new PointF[teeth * 4];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = Math.PI * 2 * i / points.Length;
            float radius = i % 4 is 0 or 1 ? outerRadius : rootRadius;
            points[i] = new PointF(
                centreX + (float)(radius * Math.Cos(angle)),
                centreY + (float)(radius * Math.Sin(angle)));
        }

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddPolygon(points);
        path.AddEllipse(
            centreX - holeRadius, centreY - holeRadius, holeRadius * 2, holeRadius * 2);
        using var brush = new SolidBrush(button.ForeColor);
        e.Graphics.FillPath(brush, path);
    }

    private void SettingsClick(object? sender, EventArgs e)
    {
        using var dialog = new ApplicationSettingsDialog(AppearanceSettingsFile.LoadOrDefault());
        dialog.ShowDialog(form);
        if (dialog.RestartRequested)
        {
            ApplicationRestart.Request();
            form.Close();
        }
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
        // Aero-snap yields a real Maximized state without isCustomMaximized; treating it as normal would overwrite restoreBounds.
        if (isCustomMaximized || form.WindowState == FormWindowState.Maximized)
        {
            RestoreWindowBounds();
            return;
        }

        MaximizeToCurrentScreen();
    }

    public void MaximizeToCurrentScreen()
    {
        if (form.WindowState != FormWindowState.Normal)
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
        if (form.WindowState == FormWindowState.Maximized)
        {
            form.WindowState = FormWindowState.Normal;
            isCustomMaximized = false;
            return;
        }

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
        versionLabel.LinkColor = UiPalette.AccentMark;
        versionLabel.ActiveLinkColor = UiPalette.AccentMarkHover;
        versionLabel.VisitedLinkColor = versionLabel.LinkColor;
        versionLabel.Links.Clear();
        versionLabel.Links.Add(0, text.Length, releaseUrl);

        versionLabelWidth = GetVersionLabelWidth(text);
        versionLabel.Size = new Size(versionLabelWidth, titleBarHeight);
        versionLabel.Location = GetVersionLabelLocation();
        UpdateTabBarLayout(tabBar);
    }

    // Pulses until the user opens the link; skipped when Windows animations are off.
    private void StartUpdatePulse()
    {
        if (updatePulseTimer != null || !AnimationsEnabled())
        {
            return;
        }

        updatePulseTimer = new System.Windows.Forms.Timer
        {
            Interval = UpdatePulseIntervalMs
        };
        updatePulseTimer.Tick += UpdatePulseTick;
        // Parks while the window is inactive (25 repaints/s behind another window).
        form.Activated += ResumeUpdatePulse;
        form.Deactivate += SuspendUpdatePulse;
        // The async update check can land while another app is active, after Deactivate already fired: start suspended.
        if (Form.ActiveForm == form)
        {
            updatePulseTimer.Start();
        }
        else
        {
            SetUpdateLinkColor(UiPalette.AccentMark);
        }
    }

    private void ResumeUpdatePulse(object? sender, EventArgs e)
    {
        updatePulseTimer?.Start();
    }

    private void SuspendUpdatePulse(object? sender, EventArgs e)
    {
        if (updatePulseTimer == null)
        {
            return;
        }

        updatePulseTimer.Stop();
        SetUpdateLinkColor(UiPalette.AccentMark);
    }

    private void UpdatePulseTick(object? sender, EventArgs e)
    {
        updatePulseElapsedMs =
            (updatePulseElapsedMs + UpdatePulseIntervalMs) % UpdatePulsePeriodMs;
        // Raised cosine: no visible turnaround corner.
        double amount = 0.5 - 0.5 * Math.Cos(
            2 * Math.PI * updatePulseElapsedMs / UpdatePulsePeriodMs);
        SetUpdateLinkColor(Blend(
            UiPalette.AccentGlow, UiPalette.TitleBarText, amount));
    }

    private void StopUpdatePulse()
    {
        if (updatePulseTimer == null)
        {
            return;
        }

        DetachUpdatePulse();
        SetUpdateLinkColor(UiPalette.AccentMark);
    }

    private void DetachUpdatePulse()
    {
        if (updatePulseTimer == null)
        {
            return;
        }

        form.Activated -= ResumeUpdatePulse;
        form.Deactivate -= SuspendUpdatePulse;
        updatePulseTimer.Stop();
        updatePulseTimer.Dispose();
        updatePulseTimer = null;
    }

    // ActiveLinkColor stays: a pressed link that keeps fading reads as a glitch.
    private void SetUpdateLinkColor(Color color)
    {
        versionLabel.LinkColor = color;
        versionLabel.VisitedLinkColor = color;
    }

    private static Color Blend(Color foreground, Color background, double amount) =>
        Color.FromArgb(
            (int)(foreground.R * amount + background.R * (1 - amount)),
            (int)(foreground.G * amount + background.G * (1 - amount)),
            (int)(foreground.B * amount + background.B * (1 - amount)));

    private static bool AnimationsEnabled()
    {
        bool enabled = true;
        return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0) ||
            enabled;
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
        new(form.ClientSize.Width - windowButtonWidth * WindowButtonCount - versionLabelWidth - Scale(6), 0);

    private void UpdateTabBarLayout(FlowLayoutPanel targetTabBar)
    {
        int rightReservedWidth =
            windowButtonWidth * WindowButtonCount + versionLabelWidth + Scale(12);
        targetTabBar.Size = new Size(
            Math.Max(Scale(200), form.ClientSize.Width - Scale(8) - rightReservedWidth),
            titleBarHeight - Scale(5));
    }

    private void VersionLabelLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (e.Link?.LinkData is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        StopUpdatePulse();
        Form? owner = sender is Control control
            ? control.FindForm()
            : null;
        ApplicationUpdateService.ShowUpdateChoice(owner, url);
    }

    private float ScaleF(float value) => value * dpiScale;

    private int Scale(int value) =>
        (int)Math.Round(value * dpiScale);

    private static bool IsToolsTab(ModeTab tab) =>
        tab is ModeTab.ToolsEqWizard
            or ModeTab.ToolsSignalGenerator
            or ModeTab.ToolsVirtualCrossover
            or ModeTab.ToolsFirConstructor;

    private float GetDpiScale()
    {
        using Graphics graphics = form.CreateGraphics();
        return Math.Max(form.DeviceDpi / 96.0f, graphics.DpiX / 96.0f);
    }

    // The active tab does not lift: a lighter blue puts its label under 4.5:1 (see UiPalette.AccentFill).
    private static void SetModeTabStyle(Button button, bool active)
    {
        button.BackColor = active
            ? UiPalette.AccentFill
            : UiPalette.ButtonBackground;
        button.ForeColor = active
            ? UiPalette.TextOnAccent
            : UiPalette.TitleBarTextActive;
        button.FlatAppearance.MouseOverBackColor = active
            ? UiPalette.AccentFill
            : UiPalette.ButtonHoverBackground;
        button.FlatAppearance.MouseDownBackColor = UiPalette.AccentFillPressed;
    }
}
