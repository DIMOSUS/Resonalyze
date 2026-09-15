namespace Resonalyze.App.Tests;

/// <summary>Drives Schedule/Flush directly: the timer needs a message pump.</summary>
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
