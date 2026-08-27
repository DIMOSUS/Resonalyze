namespace Resonalyze.Audio.Tests;

public sealed class CaptureChannelLayoutTests
{
    private static AudioCaptureRouting Routing(
        int microphone,
        int? loopback,
        params int[] arrayChannels) =>
        new(microphone, loopback) { ArrayChannels = arrayChannels };

    [Theory]
    [InlineData(0, null, 1)]
    [InlineData(1, null, 2)]
    [InlineData(0, 1, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(3, 1, 4)]
    public void RequiredWaveInputChannelCount_CoversBothChannels(
        int microphone,
        int? loopback,
        int expected)
    {
        Assert.Equal(
            expected,
            CaptureChannelLayout.RequiredWaveInputChannelCount(Routing(microphone, loopback)));
    }

    [Theory]
    [InlineData(2, null, 2, 1)]
    [InlineData(2, 5, 2, 4)]
    [InlineData(5, 2, 2, 4)]
    [InlineData(3, 3, 3, 1)]
    public void AsioWindow_SpansMicrophoneAndLoopback(
        int microphone,
        int? loopback,
        int expectedFirst,
        int expectedCount)
    {
        AudioCaptureRouting routing = Routing(microphone, loopback);

        Assert.Equal(expectedFirst, CaptureChannelLayout.AsioFirstInputOffset(routing));
        Assert.Equal(expectedCount, CaptureChannelLayout.AsioInputChannelCount(routing));
    }

    [Fact]
    public void WaveCount_ReachesTheHighestArrayMicrophone()
    {
        // The array is what makes the capture wide: microphone and loopback sit
        // on 0/1, and a microphone on input 6 still has to be recorded.
        Assert.Equal(
            7,
            CaptureChannelLayout.RequiredWaveInputChannelCount(Routing(0, 1, 4, 6, 2)));
    }

    [Fact]
    public void AsioWindow_SpansTheArrayInBothDirections()
    {
        // An array microphone BELOW the measurement pair moves the window's
        // start, not just its width — the driver is asked for inputs 1..7.
        AudioCaptureRouting routing = Routing(4, 5, 1, 7);

        Assert.Equal(1, CaptureChannelLayout.AsioFirstInputOffset(routing));
        Assert.Equal(7, CaptureChannelLayout.AsioInputChannelCount(routing));
    }

    [Fact]
    public void AsioRelative_RebasesEveryChannelOntoTheWindow()
    {
        AudioCaptureRouting relative =
            CaptureChannelLayout.ToAsioRelative(Routing(4, 5, 1, 7));

        Assert.Equal(3, relative.MicrophoneChannel);
        Assert.Equal(4, relative.LoopbackChannel);
        Assert.Equal([0, 6], relative.ArrayChannels);
    }

    [Fact]
    public void AsioRelative_KeepsTheArrayInItsRequestedOrder()
    {
        // The order is the identity of each microphone: the caller pairs these
        // with its configured array entries by position, so sorting them here
        // would hand every reading to the wrong microphone.
        AudioCaptureRouting relative =
            CaptureChannelLayout.ToAsioRelative(Routing(0, 1, 6, 2, 4));

        Assert.Equal([6, 2, 4], relative.ArrayChannels);
    }

    [Fact]
    public void Routing_RefusesAnArrayChannelThatIsAlreadyAMeasurementRole()
    {
        Assert.Throws<ArgumentException>(() => Routing(0, 1, 3, 1));
        Assert.Throws<ArgumentException>(() => Routing(0, 1, 0));
    }

    [Fact]
    public void Routing_RefusesTheSameArrayChannelTwice()
    {
        Assert.Throws<ArgumentException>(() => Routing(0, 1, 4, 4));
    }

    [Fact]
    public void Routing_RefusesANegativeArrayChannel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Routing(0, 1, -1));
    }

    [Fact]
    public void Routing_WithoutAnArrayIsUnchanged()
    {
        AudioCaptureRouting routing = new(0, 1);

        Assert.Empty(routing.ArrayChannels);
        Assert.Equal(2, CaptureChannelLayout.RequiredWaveInputChannelCount(routing));
    }
}
