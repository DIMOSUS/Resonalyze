namespace Resonalyze.App.Tests;

public sealed class OverlapReframerTests
{
    [Fact]
    public void Push_WithoutOverlap_EmitsContiguousNonOverlappingFrames()
    {
        var reframer = new OverlapReframer(frameSize: 4, hopSize: 4);

        List<float[][]> frames = new();
        frames.AddRange(reframer.Push(Block(0, 1, 2, 3)));
        frames.AddRange(reframer.Push(Block(4, 5, 6, 7)));

        Assert.Equal(2, frames.Count);
        Assert.Equal(new float[] { 0, 1, 2, 3 }, frames[0][0]);
        Assert.Equal(new float[] { 4, 5, 6, 7 }, frames[1][0]);
    }

    [Fact]
    public void Push_WithHalfOverlap_SlidesByHalfFrameAcrossBlockBoundaries()
    {
        var reframer = new OverlapReframer(frameSize: 4, hopSize: 2);

        List<float[][]> frames = new();
        frames.AddRange(reframer.Push(Block(0, 1, 2, 3)));
        frames.AddRange(reframer.Push(Block(4, 5, 6, 7)));
        frames.AddRange(reframer.Push(Block(8, 9, 10, 11)));

        float[][] expected =
        [
            [0, 1, 2, 3],
            [2, 3, 4, 5],
            [4, 5, 6, 7],
            [6, 7, 8, 9],
            [8, 9, 10, 11]
        ];

        Assert.Equal(expected.Length, frames.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], frames[i][0]);
        }
    }

    [Fact]
    public void Push_WithBlockSmallerThanFrame_BuffersUntilFrameAvailable()
    {
        var reframer = new OverlapReframer(frameSize: 4, hopSize: 2);

        Assert.Empty(reframer.Push(Block(0, 1)));
        List<float[][]> frames = reframer.Push(Block(2, 3)).ToList();

        Assert.Single(frames);
        Assert.Equal(new float[] { 0, 1, 2, 3 }, frames[0][0]);
    }

    [Fact]
    public void Push_PreservesAllChannels()
    {
        var reframer = new OverlapReframer(frameSize: 2, hopSize: 2);

        float[][] block =
        [
            [10, 11],
            [20, 21]
        ];

        List<float[][]> frames = reframer.Push(block).ToList();

        Assert.Single(frames);
        Assert.Equal(2, frames[0].Length);
        Assert.Equal(new float[] { 10, 11 }, frames[0][0]);
        Assert.Equal(new float[] { 20, 21 }, frames[0][1]);
    }

    [Fact]
    public void Reset_DiscardsBufferedTail()
    {
        var reframer = new OverlapReframer(frameSize: 4, hopSize: 2);

        reframer.Push(Block(0, 1, 2, 3)).ToList();
        reframer.Reset();

        Assert.Empty(reframer.Push(Block(4, 5)));
        List<float[][]> frames = reframer.Push(Block(6, 7)).ToList();

        Assert.Single(frames);
        Assert.Equal(new float[] { 4, 5, 6, 7 }, frames[0][0]);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(4, 0)]
    [InlineData(4, 5)]
    public void Constructor_RejectsInvalidArguments(int frameSize, int hopSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OverlapReframer(frameSize, hopSize));
    }

    private static float[][] Block(params float[] samples) => [samples];
}
