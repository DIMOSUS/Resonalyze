using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Resonalyze.Screenshots;

/// <summary>
/// One run of the application, driven to produce screenshots.
/// </summary>
/// <remarks>
/// Four things here are not obvious and were each learned the hard way:
/// <list type="bullet">
/// <item>The shell runs under a real <see cref="Application.Run(Form)"/> loop, with
/// the work driven from <c>Shown</c>. Only that loop installs the WinForms
/// synchronization context; without it an <c>await</c> inside the EQ Wizard's Auto
/// Tune resumes on a thread-pool thread and builds its band controls there, which
/// WinForms then refuses to parent.</item>
/// <item>Waiting is done by pumping messages, never by blocking. The panels marshal
/// their background work back to this thread, so a plain <c>Wait()</c> deadlocks.</item>
/// <item>A mode's settings panel is a separate owned window
/// (<c>DockedModeSettingsHost</c> calls <c>Show</c>), so <see cref="Control.DrawToBitmap"/>
/// on the shell renders everything except it. Those shots come off the SCREEN.</item>
/// <item>That panel docks to whichever side of the shell has room, so the shell is
/// pinned to the right edge of the screen. With space on the right it docks outside
/// the window and lands outside the captured rectangle.</item>
/// </list>
/// </remarks>
internal sealed class ShotSession
{
    /// <summary>The size the committed assets are taken at.</summary>
    public static readonly Size AssetWindowSize = new(1494, 832);

    /// <summary>Roomier, for the manual's figures of the densest panels.</summary>
    public static readonly Size ManualWindowSize = new(1720, 1035);

    private readonly ShotConfig config;
    private readonly Size windowSize;

    private ShotSession(ShotConfig config, Size windowSize, Form1 shell)
    {
        this.config = config;
        this.windowSize = windowSize;
        Shell = shell;
    }

    public Form1 Shell { get; }

    public ShotConfig Config => config;

    /// <summary>
    /// Opens the application, runs <paramref name="body"/> against it, and closes it.
    /// </summary>
    public static void Run(ShotConfig config, Size windowSize, Action<ShotSession> body)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(body);

        Rectangle screen = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, windowSize.Width, windowSize.Height);
        if (screen.Width < windowSize.Width || screen.Height < windowSize.Height)
        {
            throw new InvalidOperationException(
                $"The screen is {screen.Width}x{screen.Height}; the shots need at " +
                $"least {windowSize.Width}x{windowSize.Height}.");
        }

        var shell = new Form1
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(screen.Right - windowSize.Width, screen.Top),
            Size = windowSize
        };

        var session = new ShotSession(config, windowSize, shell);
        Exception? failure = null;
        shell.Shown += (_, _) =>
        {
            try
            {
                session.Pump(1_500);
                body(session);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                shell.Close();
            }
        };

        Application.Run(shell);
        if (failure != null)
        {
            throw new InvalidOperationException("A shot failed.", failure);
        }
    }

    // ------------------------------------------------------------------ waiting

    /// <summary>Runs the message loop for a while without blocking it.</summary>
    public void Pump(int milliseconds)
    {
        for (int elapsed = 0; elapsed < milliseconds; elapsed += 20)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    /// <summary>Awaits work that marshals back to this thread.</summary>
    public void Await(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        while (!task.IsCompleted)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }

        task.GetAwaiter().GetResult();
    }

    // ----------------------------------------------------------------- steering

    /// <summary>Switches modes the way a tab click does — layout included.</summary>
    public void SelectTab(string tabName)
    {
        object controller = Reflect.Field(Shell, "modeController");
        Type tabType = typeof(Form1).Assembly.GetType("Resonalyze.ModeTab")
            ?? throw new InvalidOperationException("No Resonalyze.ModeTab type.");
        Await((Task)Reflect.Invoke(controller, "SelectAsync", Enum.Parse(tabType, tabName))!);
        Pump(1_500);
    }

    /// <summary>Loads an impulse response, a recorded sweep or a REW export.</summary>
    public void LoadMeasurement(string path)
    {
        Await((Task)Reflect.Invoke(Shell, "LoadImpulseResponseLikeAsync", path)!);
        Pump(4_000);
    }

    /// <summary>Opens the current mode's settings panel if it is not already open.</summary>
    public void OpenModeSettings()
    {
        var button = Reflect.Field<Button>(Shell, "buttonCurrentModeSettings");
        if (!button.Enabled)
        {
            return;
        }

        object host = Reflect.Field(Shell, "dockedModeSettingsHost");
        if (!(bool)Reflect.Property(host, "IsOpen"))
        {
            button.PerformClick();
        }

        Pump(1_500);
    }

    /// <summary>The dialog the mode settings panel is currently showing, if any.</summary>
    public Form? ModeSettingsDialog
    {
        get
        {
            object host = Reflect.Field(Shell, "dockedModeSettingsHost");
            return (bool)Reflect.Property(host, "IsOpen")
                ? (Form)Reflect.Field(host, "activeDialog")
                : null;
        }
    }

    // ----------------------------------------------------------------- capturing

    /// <summary>
    /// Captures the shell from the screen, which is the only way to include an owned
    /// window such as the mode settings panel.
    /// </summary>
    public void CaptureScreen(string name)
    {
        // Activate() cannot raise a window while another process owns the foreground
        // — Windows refuses the steal — and a screen grab then copies whatever IS on
        // top. TopMost does not need the foreground, and the check below refuses to
        // write a frame that is not ours rather than saving someone's browser.
        bool wasTopMost = Shell.TopMost;
        try
        {
            Shell.TopMost = true;
            Shell.Activate();
            Pump(600);

            Rectangle bounds = Shell.Bounds;
            EnsureNothingCovers(bounds, name);
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            Write(bitmap, name);
        }
        finally
        {
            Shell.TopMost = wasTopMost;
        }
    }

    /// <summary>
    /// Refuses the shot when another process's window sits over the rectangle about
    /// to be copied. Sampling the corners and the middle catches anything large
    /// enough to matter, and a foreign pixel means the figure would be wrong in a way
    /// no later review would notice.
    /// </summary>
    private static void EnsureNothingCovers(Rectangle bounds, string name)
    {
        Point[] probes =
        [
            new(bounds.Left + 8, bounds.Top + 8),
            new(bounds.Right - 8, bounds.Top + 8),
            new(bounds.Left + 8, bounds.Bottom - 8),
            new(bounds.Right - 8, bounds.Bottom - 8),
            new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)
        ];
        foreach (Point probe in probes)
        {
            nint window = WindowFromPoint(probe);
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != (uint)Environment.ProcessId)
            {
                throw new InvalidOperationException(
                    $"{name}: another window is covering the shell at " +
                    $"{probe.X},{probe.Y}. The run needs the screen to itself — " +
                    "nothing may sit over the application while it shoots.");
            }
        }
    }

    /// <summary>Captures a control's own rendering, owned windows excluded.</summary>
    public void Capture(Control control, string name)
    {
        ArgumentNullException.ThrowIfNull(control);
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        Write(bitmap, name);
    }

    /// <summary>
    /// Shoots a modal dialog: the real button is clicked, and a timer running inside
    /// the dialog's own message loop grabs it and cancels out. <paramref name="pose"/>
    /// runs on that timer, so a dialog can be driven (a value typed, a search run)
    /// before the shot.
    /// </summary>
    public void CaptureModal(
        string name,
        Action open,
        int settleMs = 1_500,
        Action<Form>? pose = null)
    {
        ArgumentNullException.ThrowIfNull(open);
        bool shot = false;
        bool wasNative = false;
        using var timer = new System.Windows.Forms.Timer { Interval = settleMs };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Form? dialog = Application.OpenForms
                .Cast<Form>()
                .LastOrDefault(form => form != Shell && form.Visible && form.Modal);
            if (dialog == null)
            {
                // A MessageBox or a common file dialog is not a Form in OpenForms, so
                // nothing above can see it — and it blocks the click that opened it
                // for ever. Closing turns a hang into the error below.
                wasNative = CloseNativeDialog();
                return;
            }

            pose?.Invoke(dialog);
            Application.DoEvents();
            // DrawToBitmap renders a bordered dialog's frame too, so the bitmap is
            // sized to the WHOLE window; sizing it to ClientSize crops the buttons
            // off the bottom by exactly the title bar's height.
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
            {
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                Write(bitmap, name);
            }

            shot = true;
            dialog.DialogResult = DialogResult.Cancel;
            dialog.Close();
        };
        timer.Start();
        open();
        timer.Stop();
        Pump(400);

        if (!shot)
        {
            throw new InvalidOperationException(wasNative
                ? $"{name}: the click opened a native dialog (a MessageBox or a file " +
                  "dialog), not a Form this tool can capture. Drive the panel a way " +
                  "that does not raise one, or construct the dialog directly."
                : $"{name}: no modal dialog appeared within {settleMs} ms.");
        }
    }

    /// <summary>Captures a dialog opened without a modal loop.</summary>
    public void CaptureDialog(Form dialog, string name)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        dialog.Show();
        Pump(600);
        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
        Write(bitmap, name);
        dialog.Close();
    }

    /// <summary>
    /// Closes a native dialog this process is showing, if any. MessageBox and the
    /// common file dialogs are window class <c>#32770</c> rather than WinForms
    /// windows, so they never appear in <see cref="Application.OpenForms"/>.
    /// </summary>
    private static bool CloseNativeDialog()
    {
        nint found = 0;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint owner);
            if (owner != (uint)Environment.ProcessId || !IsWindowVisible(handle))
            {
                return true;
            }

            var name = new StringBuilder(64);
            GetClassName(handle, name, name.Capacity);
            if (name.ToString() != "#32770")
            {
                return true;
            }

            found = handle;
            return false;
        }, 0);

        if (found == 0)
        {
            return false;
        }

        const uint WM_CLOSE = 0x0010;
        PostMessage(found, WM_CLOSE, 0, 0);
        return true;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    // DllImport rather than LibraryImport: the source generator needs
    // AllowUnsafeBlocks, and it cannot marshal StringBuilder — neither is worth
    // taking on for five calls that run once per shot.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint w, nint l);

    private void Write(Bitmap bitmap, string name)
    {
        string path = config.Resolve(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine($"  {name}  {bitmap.Width}x{bitmap.Height}  ->  {path}");
    }

    /// <summary>The size this session's shell was opened at.</summary>
    public Size WindowSize => windowSize;
}
