using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Resonalyze.Screenshots;

/// <summary>One application run driven to produce screenshots.</summary>
/// <remarks>Runs under a real Application.Run loop (async Auto Tune needs its sync context); waits pump messages (Wait() deadlocks);
/// the mode settings panel is an owned window, so those shots come off the screen, with the shell pinned right so it docks inside.</remarks>
internal sealed class ShotSession
{
    public static readonly Size AssetWindowSize = new(1494, 832);

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

        using (var watchdog = new MessageBoxWatchdog())
        {
            Application.Run(shell);
            if (failure == null && watchdog.Answered is { } blocked)
            {
                throw new InvalidOperationException(
                    $"A message box stopped the scene and was answered by the watchdog: {blocked}");
            }
        }

        if (failure != null)
        {
            throw new InvalidOperationException("A shot failed.", failure);
        }
    }

    /// <summary>A MessageBox raised outside <see cref="CaptureModal"/> (a click's question, a result report) is invisible to
    /// <see cref="Application.OpenForms"/> and blocks the run for ever: after a grace period this records its text,
    /// answers it with Cancel, No or OK, whichever it has, and the scene fails with that text.</summary>
    private sealed class MessageBoxWatchdog : IDisposable
    {
        private static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);
        private readonly Dictionary<nint, DateTime> firstSeen = [];
        private readonly System.Threading.Timer timer;

        // Written on the timer's thread, read on the UI thread once the loop has ended.
        private string? answered;

        public MessageBoxWatchdog() => timer = new System.Threading.Timer(_ => Check(), null, 2_000, 2_000);

        public string? Answered => Volatile.Read(ref answered);

        private void Check()
        {
            // Timer callbacks can overlap; one at a time keeps the table whole.
            if (!Monitor.TryEnter(firstSeen))
            {
                return;
            }

            try
            {
                AnswerStuckDialogs();
            }
            finally
            {
                Monitor.Exit(firstSeen);
            }
        }

        private void AnswerStuckDialogs()
        {
            const uint WM_COMMAND = 0x0111;
            foreach (nint dialog in OwnNativeDialogs())
            {
                DateTime now = DateTime.UtcNow;
                if (!firstSeen.TryGetValue(dialog, out DateTime since))
                {
                    firstSeen[dialog] = now;
                    continue;
                }

                if (now - since < Grace)
                {
                    continue;
                }

                Interlocked.CompareExchange(ref answered, DialogText(dialog), null);
                Console.Error.WriteLine($"  watchdog: answering a message box: {DialogText(dialog)}");
                // IDCANCEL, IDNO, IDOK: a box ignores a command for a button it does not have.
                foreach (int command in new[] { 2, 7, 1 })
                {
                    PostMessage(dialog, WM_COMMAND, command, 0);
                }
            }
        }

        private static List<nint> OwnNativeDialogs()
        {
            var found = new List<nint>();
            EnumWindows((handle, _) =>
            {
                GetWindowThreadProcessId(handle, out uint owner);
                if (owner == (uint)Environment.ProcessId && IsWindowVisible(handle) && ClassOf(handle) == "#32770")
                {
                    found.Add(handle);
                }

                return true;
            }, 0);
            return found;
        }

        private static string DialogText(nint dialog)
        {
            var texts = new List<string>();
            EnumChildWindows(dialog, (child, _) =>
            {
                if (ClassOf(child) == "Static")
                {
                    var text = new StringBuilder(1024);
                    GetWindowText(child, text, text.Capacity);
                    if (text.Length > 0)
                    {
                        texts.Add(text.ToString().ReplaceLineEndings(" "));
                    }
                }

                return true;
            }, 0);
            return texts.Count > 0 ? string.Join(" / ", texts) : "(no text)";
        }

        private static string ClassOf(nint window)
        {
            var name = new StringBuilder(64);
            GetClassName(window, name, name.Capacity);
            return name.ToString();
        }

        public void Dispose() => timer.Dispose();
    }

    public void Pump(int milliseconds)
    {
        for (int elapsed = 0; elapsed < milliseconds; elapsed += 20)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

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

    public void SelectTab(string tabName)
    {
        object controller = Reflect.Field(Shell, "modeController");
        Type tabType = typeof(Form1).Assembly.GetType("Resonalyze.ModeTab")
            ?? throw new InvalidOperationException("No Resonalyze.ModeTab type.");
        Await((Task)Reflect.Invoke(controller, "SelectAsync", Enum.Parse(tabType, tabName))!);
        Pump(1_500);
    }

    public void LoadMeasurement(string path)
    {
        Await((Task)Reflect.Invoke(Shell, "LoadImpulseResponseLikeAsync", path)!);
        Pump(4_000);
    }

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

    /// <param name="afterRaise">Opens what must sit over the shell (a menu) once the shell is raised, so raising it cannot bury that.</param>
    public void CaptureScreen(string name, Action? afterRaise = null)
    {
        using Bitmap bitmap = GrabScreen(name, afterRaise);
        Write(bitmap, name);
    }

    public Bitmap GrabScreen(string name, Action? afterRaise = null)
    {
        // Activate() cannot steal the foreground from another process; TopMost can, and the check below refuses a foreign frame.
        bool wasTopMost = Shell.TopMost;
        try
        {
            Shell.TopMost = true;
            Shell.Activate();
            Pump(600);
            if (afterRaise != null)
            {
                afterRaise();
                Pump(600);
            }

            Rectangle bounds = Shell.Bounds;
            EnsureNothingCovers(bounds, name);
            var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            return bitmap;
        }
        finally
        {
            Shell.TopMost = wasTopMost;
        }
    }

    /// <summary>A control's rectangle in the pixels <see cref="CaptureScreen"/> writes.</summary>
    public Rectangle ShellBounds(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Point screen = control.PointToScreen(Point.Empty);
        return new Rectangle(
            screen.X - Shell.Left, screen.Y - Shell.Top, control.Width, control.Height);
    }

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

    public void Capture(Control control, string name)
    {
        ArgumentNullException.ThrowIfNull(control);
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        Write(bitmap, name);
    }

    /// <summary>A timer inside the dialog's own modal loop runs <paramref name="pose"/>, grabs the dialog and cancels it.</summary>
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
                // A MessageBox or file dialog is not in OpenForms and would block forever; closing turns the hang into an error.
                wasNative = CloseNativeDialog();
                return;
            }

            pose?.Invoke(dialog);
            Application.DoEvents();
            // Whole window size: DrawToBitmap includes the frame, and ClientSize crops the bottom buttons.
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

    /// <summary>MessageBox and common file dialogs are class <c>#32770</c>, never in <see cref="Application.OpenForms"/>.</summary>
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

    // DllImport: LibraryImport needs AllowUnsafeBlocks and cannot marshal StringBuilder.
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(nint window, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint w, nint l);

    public void Write(Bitmap bitmap, string name)
    {
        string path = config.Resolve(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine($"  {name}  {bitmap.Width}x{bitmap.Height}  ->  {path}");
    }

    public Size WindowSize => windowSize;
}
