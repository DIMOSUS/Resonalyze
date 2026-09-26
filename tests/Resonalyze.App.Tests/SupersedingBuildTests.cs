namespace Resonalyze.App.Tests;

public sealed class SupersedingBuildTests
{
    [Fact]
    public async Task ANewerBuild_CancelsTheOneInFlight_AndOnlyItLands()
    {
        var builds = new SupersedingBuild();
        var landed = new List<string>();
        using var started = new ManualResetEventSlim();
        CancellationToken firstToken = default;

        Task first = builds.RunAsync(
            token =>
            {
                firstToken = token;
                started.Set();
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                token.ThrowIfCancellationRequested();
                return "first";
            },
            landed.Add);
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        Task second = builds.RunAsync(_ => "second", landed.Add);
        await Task.WhenAll(first, second);

        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal(["second"], landed);
        // The superseded source is released too, once its build has stopped.
        Assert.Throws<ObjectDisposedException>(() => firstToken.WaitHandle);
    }

    [Fact]
    public async Task ASupersededBuild_LandsNothing_EvenWhenItIgnoresTheCancellation()
    {
        var builds = new SupersedingBuild();
        var landed = new List<string>();
        using var release = new ManualResetEventSlim();

        Task first = builds.RunAsync(
            _ =>
            {
                release.Wait(TimeSpan.FromSeconds(10));
                return "first";
            },
            landed.Add);
        builds.Cancel();
        release.Set();
        await first;

        Assert.Empty(landed);
    }

    [Fact]
    public async Task AFailure_FaultsTheCurrentBuild_ButNotASupersededOne()
    {
        var builds = new SupersedingBuild();
        using var release = new ManualResetEventSlim();

        Task superseded = builds.RunAsync<string>(
            _ =>
            {
                release.Wait(TimeSpan.FromSeconds(10));
                throw new InvalidOperationException("superseded");
            },
            _ => { });
        Task current = builds.RunAsync<string>(_ => throw new InvalidOperationException("current"), _ => { });
        release.Set();

        await superseded;
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => current);
        Assert.Equal("current", failure.Message);
    }
}
