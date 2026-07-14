namespace Resonalyze.Audio.Tests;

public sealed class AudioLevelAccumulatorTests
{
    [Fact]
    public void AddBlock_PublishesOnlyAfterThirtyHertzInterval()
    {
        var accumulator = new AudioLevelAccumulator(channelCount: 1, sampleRate: 48000);

        Assert.Null(accumulator.AddBlock([0.25], [40.0], blockSampleCount: 800));
        AudioChannelLevel[]? levels = accumulator.AddBlock([0.5], [160.0], blockSampleCount: 800);

        AudioChannelLevel level = Assert.Single(levels!);
        Assert.Equal(20 * Math.Log10(0.5), level.PeakDbFs, precision: 10);
        Assert.Equal(20 * Math.Log10(Math.Sqrt(200.0 / 1600)), level.RmsDbFs, precision: 10);
    }

    [Fact]
    public void AddBlock_ResetAfterSnapshotStartsIndependentInterval()
    {
        var accumulator = new AudioLevelAccumulator(channelCount: 1, sampleRate: 30);

        AudioChannelLevel first = Assert.Single(accumulator.AddBlock([1.0], [1.0], 1)!);
        AudioChannelLevel second = Assert.Single(accumulator.AddBlock([0.1], [0.01], 1)!);

        Assert.Equal(0, first.PeakDbFs, precision: 10);
        Assert.Equal(-20, second.PeakDbFs, precision: 10);
    }

    [Fact]
    public void AddBlock_CombinesChannelsIndependently()
    {
        var accumulator = new AudioLevelAccumulator(channelCount: 2, sampleRate: 30);

        AudioChannelLevel[] levels = accumulator.AddBlock(
            [0.25, 0.75],
            [0.0625, 0.5625],
            1)!;

        Assert.Equal(2, levels.Length);
        Assert.Equal(20 * Math.Log10(0.25), levels[0].PeakDbFs, precision: 10);
        Assert.Equal(20 * Math.Log10(0.75), levels[1].PeakDbFs, precision: 10);
    }
}
