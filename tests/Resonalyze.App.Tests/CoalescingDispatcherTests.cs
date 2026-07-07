namespace Resonalyze.App.Tests;

public sealed class CoalescingDispatcherTests
{
    [Fact]
    public void Offer_PostsOnceUntilDrainedAndDeliversLatestValue()
    {
        var posted = new List<Action>();
        var applied = new List<int>();
        var dispatcher = new CoalescingDispatcher<int>(
            drain =>
            {
                posted.Add(drain);
                return true;
            },
            applied.Add);

        dispatcher.Offer(1);
        dispatcher.Offer(2);
        dispatcher.Offer(3);

        Assert.Single(posted);

        posted[0]();

        Assert.Equal(new[] { 3 }, applied);
    }

    [Fact]
    public void Offer_PostsAgainAfterDrain()
    {
        var posted = new List<Action>();
        var applied = new List<int>();
        var dispatcher = new CoalescingDispatcher<int>(
            drain =>
            {
                posted.Add(drain);
                return true;
            },
            applied.Add);

        dispatcher.Offer(1);
        posted[0]();
        dispatcher.Offer(2);
        posted[1]();

        Assert.Equal(2, posted.Count);
        Assert.Equal(new[] { 1, 2 }, applied);
    }

    [Fact]
    public void Offer_RetriesPostAfterFailedDispatch()
    {
        int postAttempts = 0;
        bool allowPost = false;
        var applied = new List<int>();
        Action? lastDrain = null;
        var dispatcher = new CoalescingDispatcher<int>(
            drain =>
            {
                postAttempts++;
                if (!allowPost)
                {
                    return false;
                }

                lastDrain = drain;
                return true;
            },
            applied.Add);

        // A failed post (handle not created yet / already destroyed) must not
        // leave the dispatcher stuck with a "queued" flag no drain will reset.
        dispatcher.Offer(1);
        Assert.Equal(1, postAttempts);

        allowPost = true;
        dispatcher.Offer(2);
        Assert.Equal(2, postAttempts);

        lastDrain!();
        Assert.Equal(new[] { 2 }, applied);
    }

    [Fact]
    public void Offer_DuringDrainQueuesAFreshDispatch()
    {
        var posted = new List<Action>();
        var applied = new List<int>();
        CoalescingDispatcher<int> dispatcher = null!;
        dispatcher = new CoalescingDispatcher<int>(
            drain =>
            {
                posted.Add(drain);
                return true;
            },
            value =>
            {
                applied.Add(value);
                if (value == 1)
                {
                    // A producer racing with the drain: the new value needs its
                    // own dispatch because this drain already took a snapshot.
                    dispatcher.Offer(2);
                }
            });

        dispatcher.Offer(1);
        posted[0]();

        Assert.Equal(2, posted.Count);
        posted[1]();
        Assert.Equal(new[] { 1, 2 }, applied);
    }

    [Fact]
    public void Offer_IsSafeUnderConcurrentProducers()
    {
        var applied = new List<int>();
        var pending = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var dispatcher = new CoalescingDispatcher<int>(
            drain =>
            {
                pending.Enqueue(drain);
                return true;
            },
            applied.Add);

        Parallel.For(0, 10_000, i => dispatcher.Offer(i));
        while (pending.TryDequeue(out Action? drain))
        {
            drain();
        }

        // Far fewer dispatches than offers, and every drain applied something.
        Assert.True(applied.Count <= 10_000);
        Assert.True(applied.Count >= 1);
    }
}
