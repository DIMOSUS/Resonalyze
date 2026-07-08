namespace Resonalyze;

/// <summary>
/// Debounces a save action: <see cref="Schedule"/> (re)arms the delay so the
/// write happens once the UI has been quiet, <see cref="Flush"/> writes a
/// pending save immediately (close paths, panel-closed hooks). Replaces the
/// timer + pending-flag pair that lived as raw fields on <c>Form1</c>.
/// UI-thread only — built on the WinForms timer.
/// </summary>
internal sealed class DebouncedSaver : IDisposable
{
    private readonly System.Windows.Forms.Timer timer;
    private readonly Action save;
    private bool savePending;

    public DebouncedSaver(int delayMilliseconds, Action save)
    {
        this.save = save;
        timer = new System.Windows.Forms.Timer { Interval = delayMilliseconds };
        timer.Tick += (_, _) => Flush();
    }

    public void Schedule()
    {
        savePending = true;
        timer.Stop();
        timer.Start();
    }

    public void Flush()
    {
        timer.Stop();
        if (!savePending)
        {
            return;
        }

        savePending = false;
        save();
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Dispose();
    }
}
