using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Resonalyze;

internal sealed class ChromeTitleBarController
{
    public const int Height = 40;
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

    private readonly Form form;
    private readonly Control plotView;
    private readonly Action updateMaximizedBounds;
    private readonly Dictionary<ModeTab, Button> modeTabButtons = new();
    private readonly Panel titleBar;
    private readonly FlowLayoutPanel tabBar;
    private readonly LinkLabel versionLabel;
    private readonly int titleBarHeight;
    private readonly float dpiScale;
    private readonly int windowButtonWidth;
    private int versionLabelWidth;

    public ChromeTitleBarController(
        Form form,
        Control plotView,
        Action updateMaximizedBounds,
        IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        this.form = form;
        this.plotView = plotView;
        this.updateMaximizedBounds = updateMaximizedBounds;
        dpiScale = GetDpiScale();
        titleBarHeight = Scale(Height);
        windowButtonWidth = Scale(46);
        versionLabelWidth = GetVersionLabelWidth(ApplicationVersionInfo.GetDisplayVersion());

        form.FormBorderStyle = FormBorderStyle.None;
        updateMaximizedBounds();
        ShiftClientControlsBelowTitleBar();

        titleBar = new Panel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(28, 30, 36),
            Location = Point.Empty,
            Size = new Size(form.ClientSize.Width, titleBarHeight)
        };
        titleBar.MouseDown += TitleBarMouseDown;

        tabBar = CreateTabBar();
        AddModeTabs(tabBar, tabActions);
        titleBar.Controls.Add(tabBar);

        versionLabel = CreateVersionLabel();
        titleBar.Controls.Add(versionLabel);

        AddWindowButton("\u2500", form.ClientSize.Width - 138, MinimizeWindowClick); // -
        AddWindowButton("\u2610", form.ClientSize.Width - 92, MaximizeWindowClick); // []
        AddWindowButton("\u2715", form.ClientSize.Width - 46, CloseWindowClick); // x

        form.Controls.Add(titleBar);
        titleBar.BringToFront();
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

    private void ShiftClientControlsBelowTitleBar()
    {
        foreach (Control control in form.Controls)
        {
            if (control == plotView)
            {
                control.Top += titleBarHeight;
                control.Height -= titleBarHeight;
                continue;
            }

            bool topAnchored = control.Anchor.HasFlag(AnchorStyles.Top);
            bool bottomAnchored = control.Anchor.HasFlag(AnchorStyles.Bottom);
            if (topAnchored && !bottomAnchored)
            {
                control.Top += titleBarHeight;
            }
        }
    }

    private FlowLayoutPanel CreateTabBar()
    {
        var result = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = titleBar.BackColor,
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
            BackColor = titleBar.BackColor,
            ForeColor = Color.FromArgb(168, 176, 190),
            DisabledLinkColor = Color.FromArgb(168, 176, 190),
            Font = new Font(
                form.Font.FontFamily,
                Math.Max(8f, form.Font.Size - 0.5f),
                FontStyle.Regular),
            LinkBehavior = LinkBehavior.NeverUnderline,
            LinkColor = Color.FromArgb(168, 176, 190),
            ActiveLinkColor = Color.FromArgb(168, 176, 190),
            VisitedLinkColor = Color.FromArgb(168, 176, 190),
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
        AddModeTab(targetTabBar, ModeTab.LiveSpectrum, "Live Spectrum", tabActions);
        AddModeTab(targetTabBar, ModeTab.Autocorrelation, "Autocorrelation", tabActions);
        AddModeTab(targetTabBar, ModeTab.TimeAlignment, "Time Alignment", tabActions);
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
            FlatStyle = FlatStyle.Flat,
            Font = new Font(form.Font, FontStyle.Regular),
            ForeColor = Color.FromArgb(220, 224, 232),
            Height = Math.Max(Scale(28), TextRenderer.MeasureText(text, form.Font).Height + Scale(8)),
            Margin = new Padding(0, 0, Scale(2), 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = true,
            UseVisualStyleBackColor = false,
            Width = GetModeTabWidth(text)
        };
        button.FlatAppearance.BorderSize = 0;
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
            BackColor = titleBar.BackColor,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(230, 232, 238),
            Location = new Point(form.ClientSize.Width - Scale(legacyRightDistance) - width, 0),
            Size = new Size(width, titleBarHeight),
            Text = text,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = text == "✕"
            ? Color.FromArgb(196, 43, 28)
            : Color.FromArgb(54, 58, 68);
        button.FlatAppearance.MouseDownBackColor = text == "✕"
            ? Color.FromArgb(150, 32, 22)
            : Color.FromArgb(36, 86, 210);
        button.Click += clickHandler;
        titleBar.Controls.Add(button);
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
        updateMaximizedBounds();
        form.WindowState = form.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void CloseWindowClick(object? sender, EventArgs e)
    {
        form.Close();
    }

    private void UpdateVersionLabel(string text, string releaseUrl)
    {
        versionLabel.Text = text;
        versionLabel.LinkBehavior = LinkBehavior.HoverUnderline;
        versionLabel.LinkColor = Color.FromArgb(106, 173, 255);
        versionLabel.ActiveLinkColor = Color.FromArgb(150, 210, 255);
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
        if (e.Link.LinkData is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
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
            ? Color.FromArgb(64, 116, 255)
            : Color.FromArgb(50, 55, 80);
        button.FlatAppearance.MouseOverBackColor = active
            ? Color.FromArgb(78, 130, 255)
            : Color.FromArgb(50, 55, 120);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 86, 210);
    }
}
