namespace Resonalyze.App.Tests;

public sealed class LoopbackSequencePairerTests
{
    [Fact]
    public void PushMicrophoneThenLoopback_EmitsCombinedMicAndLoopbackBlock()
    {
        var emitted = new List<float[][]>();
        var pairer = new LoopbackSequencePairer(
            microphoneChannel: 0,
            loopbackChannel: 1,
            emitPair: emitted.Add);

        pairer.PushMicrophone([[1f, 2f]]);
        Assert.Empty(emitted); // no loopback yet, nothing to pair

        pairer.PushLoopback([[9f, 9f], [3f, 4f]]);

        float[][] pair = Assert.Single(emitted);
        Assert.Equal([1f, 2f], pair[0]);
        Assert.Equal([3f, 4f], pair[1]);
    }

    [Fact]
    public void Pairs_AreEmittedInArrivalOrder()
    {
        var emitted = new List<float[][]>();
        var pairer = new LoopbackSequencePairer(0, 0, emitted.Add);

        pairer.PushMicrophone([[1f]]);
        pairer.PushMicrophone([[2f]]);
        pairer.PushLoopback([[10f]]);
        pairer.PushLoopback([[20f]]);

        Assert.Equal(2, emitted.Count);
        Assert.Equal([1f], emitted[0][0]);
        Assert.Equal([10f], emitted[0][1]);
        Assert.Equal([2f], emitted[1][0]);
        Assert.Equal([20f], emitted[1][1]);
    }

    [Fact]
    public void Pair_IsTrimmedToTheShorterBlock()
    {
        var emitted = new List<float[][]>();
        var pairer = new LoopbackSequencePairer(0, 0, emitted.Add);

        pairer.PushMicrophone([[1f, 2f, 3f]]);
        pairer.PushLoopback([[7f, 8f]]);

        float[][] pair = Assert.Single(emitted);
        Assert.Equal([1f, 2f], pair[0]);
        Assert.Equal([7f, 8f], pair[1]);
    }

    [Fact]
    public void OverflowingDeviceDropsOldestUnpairedBlocks()
    {
        var emitted = new List<float[][]>();
        var pairer = new LoopbackSequencePairer(0, 0, emitted.Add, maxQueueDepth: 2);

        // Four microphone blocks with only depth 2: the two oldest are dropped.
        pairer.PushMicrophone([[1f]]);
        pairer.PushMicrophone([[2f]]);
        pairer.PushMicrophone([[3f]]);
        pairer.PushMicrophone([[4f]]);
        pairer.PushLoopback([[100f]]);

        float[][] pair = Assert.Single(emitted);
        Assert.Equal([3f], pair[0]); // 1 and 2 were dropped
        Assert.Equal([100f], pair[1]);
    }
}
