namespace Resonalyze;

/// <summary>UI-thread only (WinForms timer). <see cref="Flush"/> writes a pending save now, for close paths.</summary>
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
