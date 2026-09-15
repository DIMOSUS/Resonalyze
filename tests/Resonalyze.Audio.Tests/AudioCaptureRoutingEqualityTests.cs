namespace Resonalyze.Audio.Tests;

/// <summary>Routings naming the same channels are equal; the record compared array references, so every settings edit
/// with an array configured reopened the device (and on ASIO could hit a busy driver).</summary>
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
        // Mic and loopback may not sit on an array channel.
        Assert.NotEqual(routing, new AudioCaptureRouting(0, 6) { ArrayChannels = [2, 3] });
        Assert.NotEqual(routing, new AudioCaptureRouting(5, 1) { ArrayChannels = [2, 3] });
    }

    [Fact]
    public void OrderIsPartOfTheRouting()
    {
        // Positional channels: two orders are two arrays.
        Assert.NotEqual(
            new AudioCaptureRouting(0, 1) { ArrayChannels = [2, 3] },
            new AudioCaptureRouting(0, 1) { ArrayChannels = [3, 2] });
    }
}
