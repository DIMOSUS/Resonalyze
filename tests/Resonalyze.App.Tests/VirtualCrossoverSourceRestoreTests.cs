namespace Resonalyze.App.Tests;

/// <summary>
/// The project-bind source restore sequence (see
/// <see cref="VirtualCrossoverPanel.RestoreProjectSourcesAsync{TChannel}"/>):
/// its ORDER is the cross-rate import contract.
/// </summary>
public sealed class VirtualCrossoverSourceRestoreTests
{
    // The field bug this pins: importing a session at a different sample
    // rate lost every channel but the last, because each new source was
    // rate-checked against the previous project's not-yet-replaced
    // channels. Every wipe must precede the FIRST resolve.
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
                // The mono pair resolves its single slot once.
                "resolve B L", "done B",
                "resolve C L", "resolve C R", "done C"
            ],
            log);
    }
}
