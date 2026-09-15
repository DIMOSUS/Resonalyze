namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSourceRestoreTests
{
    // Field bug: cross-rate import rate-checked each source against not-yet-replaced channels; every wipe must precede the first resolve.
    [Fact]
    public async Task RestoreProjectSourcesAsync_WipesEveryChannelBeforeResolvingAny()
    {
        var log = new List<string>();
        string[] channels = ["A", "B", "C"];

        await VirtualCrossoverPanel.RestoreProjectSourcesAsync(
            channels,
            isMono: channel => channel == "B",
            clearBothSlots: channel => log.Add($"clear {channel}"),
            resolveSide: (channel, rightSide) =>
            {
                log.Add($"resolve {channel} {(rightSide ? "R" : "L")}");
                return Task.CompletedTask;
            },
            channelRestored: channel => log.Add($"done {channel}"));

        Assert.Equal(
            [
                "clear A", "clear B", "clear C",
                "resolve A L", "resolve A R", "done A",
                "resolve B L", "done B",
                "resolve C L", "resolve C R", "done C"
            ],
            log);
    }
}
