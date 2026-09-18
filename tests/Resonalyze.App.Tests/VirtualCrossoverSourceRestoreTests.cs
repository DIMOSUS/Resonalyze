namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSourceRestoreTests
{
    // Field bug: cross-rate import rate-checked each source against not-yet-replaced channels; every wipe must precede the first resolve.
    [Fact]
    public async Task RestoreSourcesAsync_WipesEveryChannelBeforeResolvingAny()
    {
        var session = new VirtualCrossoverSession();
        foreach (string name in new[] { "A", "B", "C" })
        {
            var channel = new VirtualCrossoverChannel(name);
            channel.Pair.Mono = name == "B";
            channel.PhysicalSideState(false).SampleRate = 48_000;
            // Behind B's mono routing too: a stale right slot would come back when the block goes stereo.
            channel.PhysicalSideState(true).SampleRate = 48_000;
            session.Channels.Add(channel);
        }

        var log = new List<string>();
        await session.RestoreSourcesAsync(
            resolveSide: (channel, rightSide) =>
            {
                Assert.All(
                    session.Channels,
                    item => Assert.Equal(
                        (0, 0),
                        (item.PhysicalSideState(false).SampleRate, item.PhysicalSideState(true).SampleRate)));
                log.Add($"resolve {channel.Name} {(rightSide ? "R" : "L")}");
                return Task.CompletedTask;
            },
            channelRestored: channel => log.Add($"done {channel.Name}"));

        Assert.Equal(
            [
                "resolve A L", "resolve A R", "done A",
                "resolve B L", "done B",
                "resolve C L", "resolve C R", "done C"
            ],
            log);
    }
}
