namespace Resonalyze.Audio.Tests;

/// <summary>
/// Two routings that name the same channels are the same routing.
/// </summary>
/// <remarks>
/// A record compares its FIELDS, and this one holds an array of array channels: two
/// routings built separately held two different arrays and compared unequal. The
/// empty case hid it, because `[]` is a singleton — so a routing without an array
/// compared equal and nothing looked wrong until one had an array.
/// <para>
/// What suffers is the settings panel's live apply, which asks whether the audio
/// request actually changed before reopening the device. With an array configured the
/// answer was always yes, so every edit anywhere on the panel paid for a device
/// warm-up nothing had asked for — and on ASIO an unnecessary open is not free: the
/// panel's own driver probe can land on a driver that is busy and get a shorter
/// answer than the truth.
/// </para>
/// </remarks>
public sealed class AudioCaptureRoutingEqualityTests
{
    [Fact]
    public void SameChannelsAreEqualWithAnArray()
    {
        var first = new AudioCaptureRouting(0, 1) { ArrayChannels = [2, 3, 4] };
        var second = new AudioCaptureRouting(0, 1) { ArrayChannels = [2, 3, 4] };

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void SameChannelsAreEqualWithoutOne()
    {
        Assert.Equal(new AudioCaptureRouting(0, 1), new AudioCaptureRouting(0, 1));
    }

    [Fact]
    public void DifferentChannelsAreNotEqual()
    {
        var routing = new AudioCaptureRouting(0, 1) { ArrayChannels = [2, 3] };

        Assert.NotEqual(routing, new AudioCaptureRouting(0, 1) { ArrayChannels = [2, 4] });
        Assert.NotEqual(routing, new AudioCaptureRouting(0, 1) { ArrayChannels = [2, 3, 4] });
        Assert.NotEqual(routing, new AudioCaptureRouting(0, 1));
        // A routing refuses to put the microphone or the loopback on an array
        // channel, so the differing pair moves to channels the array does not hold.
        Assert.NotEqual(routing, new AudioCaptureRouting(0, 6) { ArrayChannels = [2, 3] });
        Assert.NotEqual(routing, new AudioCaptureRouting(5, 1) { ArrayChannels = [2, 3] });
    }

    [Fact]
    public void OrderIsPartOfTheRouting()
    {
        // The channels are positional: the i-th is the i-th configured microphone, so
        // two orders are two different arrays and the device is reopened for the swap.
        Assert.NotEqual(
            new AudioCaptureRouting(0, 1) { ArrayChannels = [2, 3] },
            new AudioCaptureRouting(0, 1) { ArrayChannels = [3, 2] });
    }
}
