namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAlignmentStageTests
{
    [Theory]
    [InlineData(VirtualCrossoverZone.Front, VirtualCrossoverAlignmentStage.FrontChain)]
    [InlineData(VirtualCrossoverZone.Sub, VirtualCrossoverAlignmentStage.FrontChain)]
    [InlineData(VirtualCrossoverZone.Rear, VirtualCrossoverAlignmentStage.Rear)]
    [InlineData(VirtualCrossoverZone.Center, VirtualCrossoverAlignmentStage.Center)]
    public void EachZoneBelongsToOneStage(
        VirtualCrossoverZone zone,
        VirtualCrossoverAlignmentStage stage) =>
        Assert.Equal(stage, VirtualCrossoverAlignmentStages.StageOf(zone));

    [Fact]
    public void OnlyTheFrontChainSearchesItsJunctions()
    {
        // Only the front chain searches; later stages compute from a settled reference, so they cannot pull it out of tune.
        Assert.True(VirtualCrossoverAlignmentStages.SearchesJunctions(
            VirtualCrossoverAlignmentStage.FrontChain));
        Assert.False(VirtualCrossoverAlignmentStages.SearchesJunctions(
            VirtualCrossoverAlignmentStage.Rear));
        Assert.False(VirtualCrossoverAlignmentStages.SearchesJunctions(
            VirtualCrossoverAlignmentStage.Center));
    }

    [Fact]
    public void TheFrontChainRunsFirstBecauseTheOthersAreMeasuredAgainstIt()
    {
        Assert.Equal(
            VirtualCrossoverAlignmentStage.FrontChain,
            VirtualCrossoverAlignmentStages.InOrder[0]);
        Assert.Equal(
            VirtualCrossoverAlignmentStages.InOrder.Distinct().Count(),
            VirtualCrossoverAlignmentStages.InOrder.Count);
        foreach (VirtualCrossoverZone zone in VirtualCrossoverZones.All)
        {
            Assert.Contains(
                VirtualCrossoverAlignmentStages.StageOf(zone),
                VirtualCrossoverAlignmentStages.InOrder);
        }
    }

    [Fact]
    public void AProjectWithoutARearOrACentreNeedsNoStagingAtAll()
    {
        // Pre-zone projects (Front and Sub only) take the single-stage path, which IS the unstaged engine call, not a one-stage staged run.
        Assert.False(VirtualCrossoverAlignmentStages.NeedsStaging(
            [VirtualCrossoverZone.Sub, VirtualCrossoverZone.Front, VirtualCrossoverZone.Front]));
        Assert.False(VirtualCrossoverAlignmentStages.NeedsStaging([]));

        Assert.True(VirtualCrossoverAlignmentStages.NeedsStaging(
            [VirtualCrossoverZone.Front, VirtualCrossoverZone.Rear]));
        Assert.True(VirtualCrossoverAlignmentStages.NeedsStaging(
            [VirtualCrossoverZone.Front, VirtualCrossoverZone.Center]));
    }
}
