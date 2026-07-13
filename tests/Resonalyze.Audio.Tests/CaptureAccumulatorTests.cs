namespace Resonalyze.Audio.Tests;

public sealed class CaptureAccumulatorTests
{
    [Fact]
    public void Append_AccumulatesAcrossBlocksAndSnapshots()
    {
        var accumulator = new CaptureAccumulator(
            channelCount: 2, sequenceLength: 0, initialCapacity: 4);

        accumulator.Append(Block([1, 2, 3], [10, 20, 30]), 3);
        accumulator.Append(Block([4, 5], [40, 50]), 2);

        Assert.Equal(5, accumulator.ReadSamples);
        float[][] snapshot = accumulator.Snapshot();
        Assert.Equal(new float[] { 1, 2, 3, 4, 5 }, snapshot[0]);
        Assert.Equal(new float[] { 10, 20, 30, 40, 50 }, snapshot[1]);
    }

    [Fact]
    public void Append_GrowsPastInitialCapacity()
    {
        var accumulator = new CaptureAccumulator(
            channelCount: 1, sequenceLength: 0, initialCapacity: 2);
        var block = Block(Enumerable.Range(0, 100).Select(i => (float)i).ToArray());

        accumulator.Append(block, 100);

        Assert.Equal(100, accumulator.ReadSamples);
        Assert.Equal(99f, accumulator.Snapshot()[0][99]);
    }

    [Fact]
    public void ExtractReadySequences_YieldsContiguousNonOverlappingBlocks()
    {
        var accumulator = new CaptureAccumulator(
            channelCount: 1, sequenceLength: 4, initialCapacity: 16);

        accumulator.Append(Block([1, 2, 3]), 3);
        Assert.Null(accumulator.ExtractReadySequences());

        accumulator.Append(Block([4, 5, 6, 7, 8, 9]), 6);
        List<float[][]>? ready = accumulator.ExtractReadySequences();

        Assert.NotNull(ready);
        Assert.Equal(2, ready!.Count);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, ready[0][0]);
        Assert.Equal(new float[] { 5, 6, 7, 8 }, ready[1][0]);

        // The 9th sample completes the next sequence later.
        accumulator.Append(Block([10, 11, 12]), 3);
        List<float[][]>? next = accumulator.ExtractReadySequences();
        Assert.Single(next!);
        Assert.Equal(new float[] { 9, 10, 11, 12 }, next![0][0]);
    }

    [Fact]
    public void SequenceMode_TrimsConsumedPrefixSoMemoryStaysBounded()
    {
        const int sequence = 64;
        var accumulator = new CaptureAccumulator(
            channelCount: 1, sequenceLength: sequence, initialCapacity: 128);
        var block = new float[sequence];

        // A long "live" run: without trimming this would demand ~64k samples of
        // backing store; with it the retained tail stays under two sequences.
        for (int i = 0; i < 1000; i++)
        {
            block[0] = i;
            accumulator.Append(Block(block), sequence);
            List<float[][]>? ready = accumulator.ExtractReadySequences();
            Assert.Single(ready!);
        }

        Assert.Equal(64_000, accumulator.ReadSamples);
        Assert.Empty(accumulator.Snapshot()[0]);
    }

    [Fact]
    public void MultiChannelSequences_KeepChannelsAligned()
    {
        var accumulator = new CaptureAccumulator(
            channelCount: 2, sequenceLength: 2, initialCapacity: 8);

        accumulator.Append(Block([1, 2, 3, 4], [-1, -2, -3, -4]), 4);
        List<float[][]>? ready = accumulator.ExtractReadySequences();

        Assert.Equal(2, ready!.Count);
        Assert.Equal(new float[] { 1, 2 }, ready[0][0]);
        Assert.Equal(new float[] { -1, -2 }, ready[0][1]);
        Assert.Equal(new float[] { 3, 4 }, ready[1][0]);
        Assert.Equal(new float[] { -3, -4 }, ready[1][1]);
    }

    private static float[][] Block(params float[][] channels) => channels;
}
