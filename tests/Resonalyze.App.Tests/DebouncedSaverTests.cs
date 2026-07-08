namespace Resonalyze.App.Tests;

/// <summary>
/// The save-debounce timer/pending-flag pair moved off Form1 into
/// <see cref="DebouncedSaver"/>; these pin the flush semantics the close
/// paths rely on (the timer itself needs a message pump, so the tests drive
/// Schedule/Flush directly).
/// </summary>
public sealed class DebouncedSaverTests
{
    [Fact]
    public void Flush_WithoutSchedule_DoesNotSave()
    {
        int saves = 0;
        using var saver = new DebouncedSaver(1000, () => saves++);

        saver.Flush();

        Assert.Equal(0, saves);
    }

    [Fact]
    public void Flush_AfterSchedule_SavesExactlyOnce()
    {
        int saves = 0;
        using var saver = new DebouncedSaver(1000, () => saves++);

        saver.Schedule();
        saver.Flush();
        saver.Flush();

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Schedule_AfterFlush_ArmsANewSave()
    {
        int saves = 0;
        using var saver = new DebouncedSaver(1000, () => saves++);

        saver.Schedule();
        saver.Flush();
        saver.Schedule();
        saver.Flush();

        Assert.Equal(2, saves);
    }
}
