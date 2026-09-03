namespace Resonalyze;

/// <summary>
/// What the user watches while the bridge works: the step running now, the
/// steps already done, and a moving bar. The work behind it takes from a second
/// (gathering a package) to tens of seconds (an import that probes a junction,
/// tunes it and then realigns), and without this the panel sits there looking
/// hung — the wait cursor alone says "busy", never "still going" or "at what".
/// </summary>
/// <remarks>
/// <para>
/// Informational, not modal. The steps that must not have the tune changed
/// under them — Auto delay, Auto-tune — disable the panel themselves, and one
/// of them (the crossover wizard) opens a window of its own that a modal
/// progress box would have to fight for the foreground. So this one only
/// reports, owned by the form so it stays above it, and takes nothing away.
/// </para>
/// <para>
/// There is no Cancel: every step behind it is one computation the panel cannot
/// interrupt without leaving a half-written tune, and a button that cannot keep
/// its word is worse than none.
/// </para>
/// </remarks>
internal sealed partial class AgentProgressDialog : Form
{
    private readonly List<string> done = [];

    public AgentProgressDialog(string title, string firstStep)
    {
        InitializeComponent();
        Text = title;
        labelStep.Text = firstStep;
    }

    /// <summary>
    /// Names the step that is starting; the one before it joins the list below,
    /// so the window shows work advancing rather than one line changing. Safe
    /// from any thread, and a no-op once the window has closed.
    /// </summary>
    public void Report(string step)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => Report(step));
            return;
        }

        done.Add(labelStep.Text);
        // The last few only: the window is small, and the whole list is in the
        // summary the import ends with.
        labelDone.Text = string.Join(
            Environment.NewLine, done.Skip(Math.Max(0, done.Count - 3)));
        labelStep.Text = step;
        Update();
    }

    /// <summary>
    /// Runs <paramref name="work"/> with the window up, closing it whatever
    /// happens — including a throw, which reaches the caller as it would have
    /// without the window.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        IWin32Window? owner,
        string title,
        string firstStep,
        Func<AgentProgressDialog, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        using var dialog = new AgentProgressDialog(title, firstStep);
        try
        {
            if (owner is Form form && !form.IsDisposed)
            {
                dialog.Show(form);
            }
            else
            {
                dialog.Show();
            }

            // Painted before the first step starts, or the window would appear
            // blank for as long as that step runs.
            dialog.Update();
            return await work(dialog);
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>The same for work with nothing to return.</summary>
    public static Task RunAsync(
        IWin32Window? owner, string title, string firstStep, Func<AgentProgressDialog, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return RunAsync<bool>(owner, title, firstStep, async dialog =>
        {
            await work(dialog);
            return true;
        });
    }
}
