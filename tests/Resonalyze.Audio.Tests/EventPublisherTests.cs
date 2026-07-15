namespace Resonalyze.Audio.Tests;

public sealed class EventPublisherTests
{
    [Fact]
    public void Publish_ParameterlessContinuesAfterSubscriberThrows()
    {
        int received = 0;
        Action handlers = () => throw new InvalidOperationException("broken subscriber");
        handlers += () => received++;

        EventPublisher.Publish(handlers);

        Assert.Equal(1, received);
    }

    [Fact]
    public void Publish_ContinuesAfterSubscriberThrows()
    {
        var received = new List<int>();
        Action<int> handlers = _ => throw new InvalidOperationException("broken subscriber");
        handlers += received.Add;

        EventPublisher.Publish(handlers, 42);

        Assert.Equal([42], received);
    }
}
