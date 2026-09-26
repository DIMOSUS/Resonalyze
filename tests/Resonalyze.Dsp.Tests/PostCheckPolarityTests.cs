namespace Resonalyze.Dsp.Tests;

public sealed class PostCheckPolarityTests
{
    private const int SampleRate = 48_000;

    private static (DspChannelChain Lower, DspChannelChain Upper) Pair(
        int slopeDbPerOctave, double cornerHz, bool lowerInverted, bool upperInverted)
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, cornerHz, slopeDbPerOctave);
        return (
            new DspChannelChain(
                InvertPolarity: lowerInverted,
                Crossover: new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge)),
            new DspChannelChain(
                InvertPolarity: upperInverted,
                Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge)));
    }

    [Theory]
    [InlineData(36, false, false)]
    [InlineData(36, false, true)]
    [InlineData(36, true, true)]
    [InlineData(24, false, true)]
    [InlineData(24, true, false)]
    public void ForcedFlip_PutsThePairInTheRelationItsSplitSumsIn(
        int slopeDbPerOctave, bool lowerInverted, bool upperInverted)
    {
        (DspChannelChain lower, DspChannelChain upper) = Pair(slopeDbPerOctave, 2_000, lowerInverted, upperInverted);

        bool? flip = PostCheckPolarity.ForcedFlip(lower, upper, SampleRate);

        // Linkwitz-Riley parity: 36 dB/oct sums inverted, 24 in phase, whatever signs the chains were saved with.
        Assert.Equal(slopeDbPerOctave == 36, lowerInverted ^ upperInverted ^ flip);
    }

    [Fact]
    public void ForcedFlip_BelowTheFence_LeavesBothSignsToTheSearch()
    {
        (DspChannelChain lower, DspChannelChain upper) = Pair(36, 180, lowerInverted: false, upperInverted: true);

        Assert.Null(PostCheckPolarity.ForcedFlip(lower, upper, SampleRate));
    }

    [Fact]
    public void Shared_KeepsTheFlipTheSidesAgreeOn_AndForcesNothingWhereTheyDiffer()
    {
        Assert.True(PostCheckPolarity.Shared([true, true]));
        Assert.Null(PostCheckPolarity.Shared([true, false]));
        Assert.Null(PostCheckPolarity.Shared([true, null]));
    }
}
