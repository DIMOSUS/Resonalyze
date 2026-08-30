namespace Resonalyze.App.Tests;

/// <summary>
/// The order Auto delay settles a complex installation in, and the property
/// that makes the order safe: only the front chain searches, so no later stage
/// can pull an earlier one out of tune.
/// </summary>
public sealed class VirtualCrossoverAlignmentStageTests
{
    [Theory]
    [InlineData(VirtualCrossoverZone.Front, VirtualCrossoverAlignmentStage.FrontChain)]
    // The subwoofers join the FRONT chain rather than forming one of their own:
    // that is where their junctions are, and how they are tuned by hand.
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
        // This is the whole reason the staging is safe rather than merely
        // ordered. A stage that searched would be free to move what it was
        // measured against; one that computes a placement from a settled
        // reference cannot disagree with it, so the stages are one-way and no
        // later group can pull the front stage out of tune.
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
        // Every stage a zone can name is one the run actually performs.
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
        // The compatibility guarantee, stated as code rather than as a hope:
        // every project written before zones existed migrates into Front and Sub
        // only, so it takes the single-stage path — which IS the old engine call,
        // not a staged run that happens to have one stage. The session battery is
        // therefore unchanged by construction.
        Assert.False(VirtualCrossoverAlignmentStages.NeedsStaging(
            [VirtualCrossoverZone.Sub, VirtualCrossoverZone.Front, VirtualCrossoverZone.Front]));
        Assert.False(VirtualCrossoverAlignmentStages.NeedsStaging([]));

        Assert.True(VirtualCrossoverAlignmentStages.NeedsStaging(
            [VirtualCrossoverZone.Front, VirtualCrossoverZone.Rear]));
        Assert.True(VirtualCrossoverAlignmentStages.NeedsStaging(
            [VirtualCrossoverZone.Front, VirtualCrossoverZone.Center]));
    }
}
