namespace Resonalyze;

/// <summary>Bridge progress window. Informational, not modal (Auto delay and Auto-tune disable the panel themselves; the crossover wizard needs the foreground), and no Cancel: no step can stop without a half-written tune.</summary>
internal sealed partial class AgentProgressDialog : Form
{
    private readonly List<string> done = [];

    public AgentProgressDialog(string title, string firstStep)
    {
        InitializeComponent();
        Text = title;
        labelStep.Text = firstStep;
    }

    /// <summary>Starts a step; the previous one joins the done list. Thread-safe; no-op once closed.</summary>
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
        labelDone.Text = string.Join(
            Environment.NewLine, done.Skip(Math.Max(0, done.Count - 3)));
        labelStep.Text = step;
        Update();
    }

    /// <summary>Runs <paramref name="work"/> with the window up and closes it whatever happens; a throw reaches the caller unchanged.</summary>
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

            // Painted now, or the window stays blank for the whole first step.
            dialog.Update();
            return await work(dialog);
        }
        finally
        {
            dialog.Close();
        }
    }

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
