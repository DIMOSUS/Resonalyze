namespace Resonalyze.App.Tests;

/// <summary>
/// Two instances share one settings file and one history file, each read whole
/// at startup and written back whole — so the copy that closes last silently
/// discards the other's session.
/// </summary>
public sealed class SingleInstanceGuardTests
{
    // Unique per test run so a leftover kernel object from an earlier run, or a
    // parallel run, cannot make these flaky.
    private static string Directory() =>
        Path.Combine(Path.GetTempPath(), $"resonalyze-guard-{Guid.NewGuid():N}");

    [Fact]
    public void TryAcquire_TheFirstCaller_GetsTheGuard()
    {
        using SingleInstanceGuard? guard = SingleInstanceGuard.TryAcquire(Directory());

        Assert.NotNull(guard);
    }

    [Fact]
    public void TryAcquire_WhileHeld_RefusesTheSecondCaller()
    {
        string directory = Directory();
        using SingleInstanceGuard? first = SingleInstanceGuard.TryAcquire(directory);

        SingleInstanceGuard? second = SingleInstanceGuard.TryAcquire(directory);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_AfterTheFirstIsDisposed_Succeeds()
    {
        string directory = Directory();
        SingleInstanceGuard? first = SingleInstanceGuard.TryAcquire(directory);
        Assert.NotNull(first);
        first.Dispose();

        using SingleInstanceGuard? second = SingleInstanceGuard.TryAcquire(directory);

        Assert.NotNull(second);
    }

    /// <summary>
    /// A portable copy keeps its data beside the executable, so it is a
    /// different directory and must be free to run alongside an installed one.
    /// The same applies to a second Windows user, who has their own
    /// %LocalAppData%.
    /// </summary>
    [Fact]
    public void TryAcquire_ADifferentDataDirectory_IsNotBlocked()
    {
        using SingleInstanceGuard? installed = SingleInstanceGuard.TryAcquire(Directory());

        using SingleInstanceGuard? portable = SingleInstanceGuard.TryAcquire(Directory());

        Assert.NotNull(installed);
        Assert.NotNull(portable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryAcquire_RejectsAnEmptyDirectory(string directory)
    {
        Assert.Throws<ArgumentException>(() => SingleInstanceGuard.TryAcquire(directory));
    }

    [Fact]
    public void TryAcquire_IgnoresCaseAndTrailingSeparators()
    {
        string directory = Directory();
        using SingleInstanceGuard? first = SingleInstanceGuard.TryAcquire(directory);

        SingleInstanceGuard? second = SingleInstanceGuard.TryAcquire(
            directory.ToUpperInvariant() + Path.DirectorySeparatorChar);

        Assert.NotNull(first);
        Assert.Null(second);
    }
}
