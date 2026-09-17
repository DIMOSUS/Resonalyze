namespace Resonalyze.App.Tests;

/// <summary>Instances read and rewrite shared settings/history whole, so the last to close discards the other's session.</summary>
public sealed class SingleInstanceGuardTests
{
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

    // A restart releases the guard explicitly, and the using declaration in Main then releases it again.
    [Fact]
    public void Dispose_Twice_StillFreesTheGuardAndDoesNotThrow()
    {
        string directory = Directory();
        SingleInstanceGuard? first = SingleInstanceGuard.TryAcquire(directory);
        Assert.NotNull(first);

        first.Dispose();
        first.Dispose();

        using SingleInstanceGuard? second = SingleInstanceGuard.TryAcquire(directory);
        Assert.NotNull(second);
    }

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
