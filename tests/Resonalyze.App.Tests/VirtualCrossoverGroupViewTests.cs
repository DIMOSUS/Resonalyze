namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverGroupViewTests
{
    [Theory]
    [InlineData(VirtualCrossoverGroupView.FrontAndSub, VirtualCrossoverZone.Front, true)]
    [InlineData(VirtualCrossoverGroupView.FrontAndSub, VirtualCrossoverZone.Sub, true)]
    [InlineData(VirtualCrossoverGroupView.FrontAndSub, VirtualCrossoverZone.Rear, false)]
    [InlineData(VirtualCrossoverGroupView.FrontAndSub, VirtualCrossoverZone.Center, false)]
    [InlineData(VirtualCrossoverGroupView.RearAndSub, VirtualCrossoverZone.Rear, true)]
    [InlineData(VirtualCrossoverGroupView.RearAndSub, VirtualCrossoverZone.Sub, true)]
    [InlineData(VirtualCrossoverGroupView.RearAndSub, VirtualCrossoverZone.Front, false)]
    [InlineData(VirtualCrossoverGroupView.FrontAndCenter, VirtualCrossoverZone.Center, true)]
    [InlineData(VirtualCrossoverGroupView.FrontAndCenter, VirtualCrossoverZone.Sub, false)]
    [InlineData(VirtualCrossoverGroupView.Everything, VirtualCrossoverZone.Center, true)]
    [InlineData(VirtualCrossoverGroupView.GroupsCompared, VirtualCrossoverZone.Rear, true)]
    public void EachViewDrawsItsOwnZones(
        VirtualCrossoverGroupView view,
        VirtualCrossoverZone zone,
        bool shown) =>
        Assert.Equal(shown, VirtualCrossoverGroupViews.IsShown(view, zone));

    [Fact]
    public void TheCentreIsDrawnEverywhereItAppearsAndSummedNowhere()
    {
        // A centre plays a signal synthesised from L and R, so adding its path would state an unknown signal division.
        foreach (VirtualCrossoverGroupView view in VirtualCrossoverGroupViews.All)
        {
            Assert.False(VirtualCrossoverGroupViews.ParticipatesInTotalSum(
                view, VirtualCrossoverZone.Center));
        }

        Assert.True(VirtualCrossoverGroupViews.IsShown(
            VirtualCrossoverGroupView.FrontAndCenter, VirtualCrossoverZone.Center));
        Assert.True(VirtualCrossoverGroupViews.IsShown(
            VirtualCrossoverGroupView.Everything, VirtualCrossoverZone.Center));
    }

    [Fact]
    public void OnlySingleChainViewsQuoteASummationLoss()
    {
        // Front vs rear combs however well tuned, so a loss there would report unrepairable damage.
        Assert.Equal(
            VirtualCrossoverZone.Front,
            VirtualCrossoverGroupViews.LossChainZone(VirtualCrossoverGroupView.FrontAndSub));
        Assert.Equal(
            VirtualCrossoverZone.Rear,
            VirtualCrossoverGroupViews.LossChainZone(VirtualCrossoverGroupView.RearAndSub));
        Assert.Equal(
            VirtualCrossoverZone.Front,
            VirtualCrossoverGroupViews.LossChainZone(VirtualCrossoverGroupView.FrontAndCenter));
        Assert.Null(
            VirtualCrossoverGroupViews.LossChainZone(VirtualCrossoverGroupView.GroupsCompared));
        Assert.Null(
            VirtualCrossoverGroupViews.LossChainZone(VirtualCrossoverGroupView.Everything));
    }

    [Fact]
    public void AViewWithoutALossFigureCompensatesWithACrossGroupComparison()
    {
        foreach (VirtualCrossoverGroupView view in VirtualCrossoverGroupViews.All)
        {
            bool quotesLoss = VirtualCrossoverGroupViews.LossChainZone(view) != null;
            bool compares = VirtualCrossoverGroupViews.ComparedAgainstFront(view).Count > 0;
            Assert.True(
                quotesLoss || compares,
                $"{view} reports neither a summation loss nor a cross-group comparison.");
        }
    }

    [Fact]
    public void TheGroupsViewSumsPerGroupInsteadOfDrawingDrivers()
    {
        Assert.False(VirtualCrossoverGroupViews.DrawsChannelCurves(
            VirtualCrossoverGroupView.GroupsCompared));
        Assert.True(VirtualCrossoverGroupViews.DrawsGroupSums(
            VirtualCrossoverGroupView.GroupsCompared));

        foreach (VirtualCrossoverGroupView view in VirtualCrossoverGroupViews.All
            .Where(item => item != VirtualCrossoverGroupView.GroupsCompared))
        {
            Assert.True(VirtualCrossoverGroupViews.DrawsChannelCurves(view));
            Assert.False(VirtualCrossoverGroupViews.DrawsGroupSums(view));
        }
    }

    [Fact]
    public void AViewThatQuotesALossMustNotDrawAnUnsummedChannelIntoIt()
    {
        // Front + Center quotes a front-only loss but draws the centre: junction rows must come from the summed subset.
        foreach (VirtualCrossoverGroupView view in VirtualCrossoverGroupViews.All)
        {
            bool drawsSomethingUnsummed = VirtualCrossoverZones.All.Any(zone =>
                VirtualCrossoverGroupViews.IsShown(view, zone) &&
                !VirtualCrossoverGroupViews.ParticipatesInTotalSum(view, zone));
            if (!drawsSomethingUnsummed ||
                VirtualCrossoverGroupViews.LossChainZone(view) == null)
            {
                continue;
            }

            Assert.Equal(VirtualCrossoverGroupView.FrontAndCenter, view);
            Assert.Equal(
                VirtualCrossoverZone.Front,
                VirtualCrossoverGroupViews.LossChainZone(view));
            Assert.False(VirtualCrossoverGroupViews.ParticipatesInTotalSum(
                view, VirtualCrossoverZone.Center));
        }
    }

    [Fact]
    public void TheDefaultViewIsWhatEverySingleStageProjectAlreadyWas()
    {
        // Front + Sub must be the enum's zero: pre-view projects open on it.
        Assert.Equal(VirtualCrossoverGroupView.FrontAndSub, default);
        Assert.Equal(VirtualCrossoverGroupView.FrontAndSub, VirtualCrossoverGroupViews.All[0]);
    }

    [Fact]
    public void EveryViewHasAName()
    {
        foreach (VirtualCrossoverGroupView view in VirtualCrossoverGroupViews.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(
                VirtualCrossoverGroupViews.DisplayName(view)));
        }

        Assert.Equal(
            VirtualCrossoverGroupViews.All.Count,
            VirtualCrossoverGroupViews.All
                .Select(VirtualCrossoverGroupViews.DisplayName)
                .Distinct()
                .Count());
    }
}
