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

    [Fact]
    public void Flush_WhenTheSaveThrows_KeepsItPending()
    {
        int attempts = 0;
        using var saver = new DebouncedSaver(1000, () =>
        {
            if (++attempts == 1)
            {
                throw new IOException("The settings file is locked.");
            }
        });

        saver.Schedule();
        Assert.Throws<IOException>(saver.Flush);
        saver.Flush();
        saver.Flush();

        Assert.Equal(2, attempts);
    }
}
