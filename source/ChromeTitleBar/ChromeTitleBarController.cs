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

    public ChromeTitleBarController(
        Form form,
        Control plotView,
        Action updateMaximizedBounds,
        IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        this.form = form;
        this.plotView = plotView;
        this.updateMaximizedBounds = updateMaximizedBounds;

        form.FormBorderStyle = FormBorderStyle.None;
        updateMaximizedBounds();
        ShiftClientControlsBelowTitleBar();

        titleBar = new Panel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(28, 30, 36),
            Location = Point.Empty,
            Size = new Size(form.ClientSize.Width, Height)
        };
        titleBar.MouseDown += TitleBarMouseDown;

        FlowLayoutPanel tabBar = CreateTabBar();
        AddModeTabs(tabBar, tabActions);

        titleBar.Controls.Add(tabBar);
        AddWindowButton("─", form.ClientSize.Width - 138, MinimizeWindowClick);
        AddWindowButton("☐", form.ClientSize.Width - 92, MaximizeWindowClick);
        AddWindowButton("✕", form.ClientSize.Width - 46, CloseWindowClick);

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
                control.Top += Height;
                control.Height -= Height;
                continue;
            }

            bool topAnchored = control.Anchor.HasFlag(AnchorStyles.Top);
            bool bottomAnchored = control.Anchor.HasFlag(AnchorStyles.Bottom);
            if (topAnchored && !bottomAnchored)
            {
                control.Top += Height;
            }
        }
    }

    private FlowLayoutPanel CreateTabBar()
    {
        var tabBar = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = titleBar.BackColor,
            FlowDirection = FlowDirection.LeftToRight,
            Location = new Point(8, 6),
            Padding = new Padding(0),
            Size = new Size(form.ClientSize.Width - 156, 34),
            WrapContents = false
        };
        tabBar.MouseDown += TitleBarMouseDown;
        return tabBar;
    }

    private void AddModeTabs(
        FlowLayoutPanel tabBar,
        IReadOnlyDictionary<ModeTab, Action> tabActions)
    {
        AddModeTab(tabBar, ModeTab.Impulse, "Impulse", tabActions);
        AddModeTab(tabBar, ModeTab.Frequency, "Frequency", tabActions);
        AddModeTab(tabBar, ModeTab.Phase, "Phase", tabActions);
        AddModeTab(tabBar, ModeTab.GroupDelay, "Group Delay", tabActions);
        AddModeTab(tabBar, ModeTab.Waterfall, "Waterfall", tabActions);
        AddModeTab(tabBar, ModeTab.Burst, "Burst", tabActions);
        AddModeTab(tabBar, ModeTab.LiveSpectrum, "Live Spectrum", tabActions);
        AddModeTab(
            tabBar,
            ModeTab.Autocorrelation,
            "Autocorrelation",
            tabActions);
    }

    private void AddModeTab(
        FlowLayoutPanel tabBar,
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
            Height = 28,
            Margin = new Padding(0, 0, 2, 0),
            Padding = new Padding(10, 0, 10, 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            Width = GetModeTabWidth(text)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => tabActions[tab]();

        modeTabButtons.Add(tab, button);
        tabBar.Controls.Add(button);
        SetModeTabStyle(button, active: false);
    }

    private void AddWindowButton(
        string text,
        int left,
        EventHandler clickHandler)
    {
        Button button = new()
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = titleBar.BackColor,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(230, 232, 238),
            Location = new Point(left, 0),
            Size = new Size(46, Height),
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

    private static int GetModeTabWidth(string text) =>
        Math.Max(
            92,
            TextRenderer.MeasureText(
                text,
                SystemFonts.MessageBoxFont).Width + 46);

    private static void SetModeTabStyle(Button button, bool active)
    {
        button.BackColor = active
            ? Color.FromArgb(64, 116, 255)
            : Color.FromArgb(42, 45, 54);
        button.FlatAppearance.MouseOverBackColor = active
            ? Color.FromArgb(78, 130, 255)
            : Color.FromArgb(54, 58, 68);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 86, 210);
    }
}
