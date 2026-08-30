namespace Resonalyze.App.Tests;

/// <summary>
/// Which zones each main-plot view draws, sums and reports on. These rules
/// decide what four separate things describe — the curves, the sum, the loss
/// trace and the read-out — so they are pinned once here rather than trusted
/// at each of those places.
/// </summary>
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
        // The reason is not cosmetic: a centre plays a signal synthesised from L
        // and R, so how much of the programme reaches it is a property of the
        // track. Adding its path to the front's would state a division of signal
        // that no measurement knows.
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
        // Front against rear combs however well either is tuned — no filter hands
        // one band from one to the other — so a loss figure there would report
        // damage that nothing can repair on a system that is correct.
        Assert.Equal(
            VirtualCrossoverZone.Front,
            VirtualCrossoverGroupViews.LossChainZone(VirtualCrossoverGroupView.FrontAndSub));
        Assert.Equal(
            VirtualCrossoverZone.Rear,
            VirtualCrossoverGroupViews.LossChainZone(VirtualCrossoverGroupView.RearAndSub));
        // The centre sits beside the front stage without entering its sum, so the
        // loss still describes the front chain alone.
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
        // The rule that keeps a view from being silent: whenever the loss is
        // withheld, the read-out owes the reader the numbers a tuner does set
        // between groups — the arrival difference and the level difference.
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
        // Front + Center is the awkward one and the reason this rule is written
        // down: it DOES quote a loss (of the front chain), and it also draws a
        // channel that is not in the sum. The junction read-outs therefore have
        // to be built from the summed subset, not from what is on screen —
        // otherwise the centre is paired with its neighbouring front driver as
        // if a crossover existed between them, and a front-only loss figure gets
        // labelled with that invented junction.
        //
        // Stated here as an invariant over the views rather than as a fact about
        // one call site: any view where the two sets differ is a view whose
        // junction metrics must follow the sum.
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

            // The only view in that corner today. If another joins it, this test
            // is the reminder that its junction rows need the same care.
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
        // Front + Sub must stay first and must be the enum's zero: a project
        // saved before views existed opens on it, and that is the behaviour the
        // tool has always had.
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
