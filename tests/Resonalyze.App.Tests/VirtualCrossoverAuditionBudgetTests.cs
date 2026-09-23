namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAuditionBudgetTests
{
    [Fact]
    public void ThePipeline_HoldsTheSourceItsResampledCopyAndTheRender_AsStereoFloats()
    {
        Assert.Equal(8L * (48_000 + 48_000), VirtualCrossoverAuditionBudget.ProjectedPipelineBytes(48_000, 48_000, 48_000));
        Assert.Equal(8L * (96_000 + 48_000 + 48_000), VirtualCrossoverAuditionBudget.ProjectedPipelineBytes(96_000, 96_000, 48_000));
        Assert.Equal(
            VirtualCrossoverAuditionBudget.ProjectedPipelineBytes(44_100L * 60, 44_100, 48_000),
            VirtualCrossoverAuditionBudget.ProjectedPipelineBytes(new AudioFileInfo(2, 44_100, TimeSpan.FromMinutes(1)), 48_000));
    }

    [Fact]
    public void ATrackOverTenMinutes_IsRefused()
    {
        string? refusal = VirtualCrossoverAuditionBudget.Refusal(
            new AudioFileInfo(2, 8_000, TimeSpan.FromMinutes(11)), 48_000);

        Assert.StartsWith("REFUSED: longer than 10 minutes", refusal);
    }

    [Fact]
    public void AShorterTrackAtAHighRate_IsRefusedByItsBytes_WithTheLengthThatWouldFit()
    {
        string? refusal = VirtualCrossoverAuditionBudget.Refusal(
            new AudioFileInfo(2, 192_000, TimeSpan.FromMinutes(8)), 48_000);

        Assert.NotNull(refusal);
        Assert.Contains("MB of audio in memory (bound 1000 MB)", refusal);
        double perMinute = VirtualCrossoverAuditionBudget.ProjectedPipelineBytes(
            new AudioFileInfo(2, 192_000, TimeSpan.FromMinutes(1)), 48_000);
        Assert.Contains($"under ~{VirtualCrossoverAuditionBudget.MaximumPipelineBytes / perMinute:0} minutes", refusal);
    }

    [Fact]
    public void ATrackWithinBothBounds_IsTaken()
    {
        Assert.Null(VirtualCrossoverAuditionBudget.Refusal(new AudioFileInfo(2, 44_100, TimeSpan.FromMinutes(9)), 48_000));
    }

    [Fact]
    public void ADecodedTrackLargerThanItsHeader_Stops()
    {
        VirtualCrossoverAuditionBudget.CheckDecoded(48_000L * 60, 48_000, 48_000);

        var exception = Assert.Throws<InvalidOperationException>(
            () => VirtualCrossoverAuditionBudget.CheckDecoded(192_000L * 60 * 9, 192_000, 48_000));
        Assert.Contains("larger than its header promised", exception.Message);
    }
}
