namespace Resonalyze.Audio.Tests;

public sealed class AsioCallbackWatchdogTests
{
    private static readonly TimeSpan Stall = AsioCallbackWatchdog.StallTimeout;

    [Fact]
    public void ADriverThatStopsCallingBack_IsStalledOnlyOnceTheTimeoutHasPassed()
    {
        var watchdog = new AsioCallbackWatchdog(callbackCount: 0, TimeSpan.Zero);

        Assert.False(watchdog.IsStalled(0, Stall - TimeSpan.FromMilliseconds(1)));
        Assert.True(watchdog.IsStalled(0, Stall));
    }

    [Fact]
    public void EachCallbackRestartsTheTimeout()
    {
        var watchdog = new AsioCallbackWatchdog(callbackCount: 10, TimeSpan.Zero);

        Assert.False(watchdog.IsStalled(11, Stall - TimeSpan.FromSeconds(1)));
        Assert.False(watchdog.IsStalled(11, Stall));
        Assert.False(watchdog.IsStalled(11, 2 * Stall - TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(1)));
        Assert.True(watchdog.IsStalled(11, 2 * Stall - TimeSpan.FromSeconds(1)));
    }
}
